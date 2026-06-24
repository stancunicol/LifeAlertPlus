using System.Collections.Concurrent;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using LifeAlertPlus.Shared.DTOs.Requests.ESP;
using LifeAlertPlus.Shared.Helpers;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.API.Services
{
    // Gestionează simulările de date ESP și cache-ul de date live în memorie
    // Rol dublu: (1) stochează ultimul pachet real primit de la ESP pentru acces rapid din UI
    //            (2) rulează simulări automate de date când nu există dispozitiv fizic
    public class SimulationManager : ISimulationManager
    {
        // Simulările active: cheie = ID persoană, valoare = (token de anulare, task-ul simulării)
        private readonly ConcurrentDictionary<Guid, (CancellationTokenSource Cts, Task RunningTask)> _runs = new();
        // Cache de date ESP (ultimul pachet per serial): cheie = serial dispozitiv
        private readonly ConcurrentDictionary<string, ESPDataResponseDTO> _simulatedData = new(StringComparer.OrdinalIgnoreCase);
        // Momentul de start al simulării pentru fiecare persoană (calculăm faza de alertă)
        private readonly ConcurrentDictionary<Guid, DateTime> _simulationStartTimes = new();
        // Ultimul heartbeat primit de la fiecare dispozitiv (diagnostice tehnice)
        private readonly ConcurrentDictionary<string, (DateTime ReceivedAt, ESPHeartbeatDTO Data)> _heartbeats = new(StringComparer.OrdinalIgnoreCase);
        private readonly IServiceScopeFactory _scopeFactory; // Singleton nu poate injecta Scoped direct
        private readonly ILogger<SimulationManager> _logger;
        private readonly AlertMonitorService _alertMonitor; // Trimitem datele simulate și la sistemul de alertă

        // Duration from simulation start during which alert-level data is generated.
        // Primele 3 minute de simulare generează date anormale (pentru a testa sistemul de alertă)
        private static readonly TimeSpan AlertPhaseDuration = TimeSpan.FromMinutes(3);

        public SimulationManager(IServiceScopeFactory scopeFactory, ILogger<SimulationManager> logger, AlertMonitorService alertMonitor)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _alertMonitor = alertMonitor;
        }

        // Returnează ultimele date ESP din cache pentru un serial dat (null dacă nu există)
        public ESPDataResponseDTO? GetData(string serial)
        {
            if (_simulatedData.TryGetValue(serial, out var data))
                return data;
            return null;
        }

        // Stochează un pachet ESP în cache-ul în memorie
        public void SetData(ESPDataResponseDTO payload)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.Serial))
                return;

            // Normalize: if Bpm/Spo2 are missing but Max30100 is present, backfill
            // Unele versiuni de firmware nu populează Bpm/Spo2 direct, ci prin Max30100[]
            if (!payload.Bpm.HasValue && payload.Max30100?.Count >= 1)
                payload.Bpm = payload.Max30100[0]; // Max30100[0] = puls
            if (!payload.Spo2.HasValue && payload.Max30100?.Count >= 2)
                payload.Spo2 = payload.Max30100[1]; // Max30100[1] = SpO2

            _simulatedData[payload.Serial.Trim()] = payload; // Suprascrie dacă există deja
        }

        // Șterge datele din cache pentru un serial (folosit la oprirea simulării)
        // Returnează true dacă au existat date, false dacă cache-ul era deja gol
        public bool ClearData(string serial)
        {
            var key = serial.Trim();
            var removed = _simulatedData.TryRemove(key, out _); // Ștergem datele ESP
            _heartbeats.TryRemove(key, out _); // Ștergem și heartbeat-urile
            return removed;
        }

        // Stochează un heartbeat primit de la dispozitiv (cu timestamp-ul primirii)
        public void SetHeartbeat(string serial, ESPHeartbeatDTO data)
            => _heartbeats[serial] = (DateTime.UtcNow, data);

        // Returnează ultimul heartbeat pentru un serial (null dacă nu a primit niciodată)
        public (DateTime ReceivedAt, ESPHeartbeatDTO Data)? GetHeartbeat(string serial)
            => _heartbeats.TryGetValue(serial, out var h) ? h : null;

        // Returnează lista ID-urilor persoanelor cu simulare activă în acest moment
        public IEnumerable<Guid> GetRunningPersonIds() => _runs.Keys.ToList();

        // Pornește simulări pentru toate persoanele monitorizate care au un serial configurat
        public async Task StartAllAsync(TimeSpan? interval = null)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
            // Citim doar persoanele care au un serial de dispozitiv — celelalte nu au unde trimite date
            var monitoreds = await db.Monitoreds
                .Where(m => !string.IsNullOrWhiteSpace(m.DeviceSerialNumber))
                .ToListAsync();

            foreach (var monitored in monitoreds)
            {
                await StartSimulationAsync(monitored.Id, interval);
            }
        }

        // Populează SimulationManager cu ultimele măsurători din DB la startup.
        // Astfel, după restart, utilizatorii văd date reale imediat (nu "no data"),
        // până ESP-ul trimite un nou pachet (~5 secunde).
        public async Task SeedFromDatabaseAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();

            // Citim doar persoanele active cu dispozitiv asociat
            var monitoreds = await db.Monitoreds
                .Where(m => !string.IsNullOrWhiteSpace(m.DeviceSerialNumber) && !m.IsArchived)
                .ToListAsync();

            var todayUtc = DateTime.UtcNow.Date;

            foreach (var m in monitoreds)
            {
                var serial = m.DeviceSerialNumber.Trim();

                // Citim ultima măsurătoare din DB pentru această persoană
                var last = await db.Measurements
                    .Where(ms => ms.IdMonitored == m.Id)
                    .OrderByDescending(ms => ms.CreatedAt)
                    .FirstOrDefaultAsync();

                // Populate live-data cache for the cards.
                // Date is set to 0 so the freshness check treats seeded data as "unknown age"
                // (shows last known values) rather than immediately marking the device offline.
                // The real Date gets set on the next actual ESP ingest or simulation cycle.
                // Nu suprascrie dacă există deja date mai recente (ex: ESP a trimis deja)
                if (!_simulatedData.ContainsKey(serial) && last != null)
                {
                    // Construim un pachet ESP sintetic din ultima măsurătoare din DB
                    _simulatedData[serial] = new LifeAlertPlus.Shared.DTOs.Responses.ESP.ESPDataResponseDTO
                    {
                        Serial      = serial,
                        Date        = 0, // 0 = vârstă necunoscută, UI nu marchează offline
                        IsAvailable = true,
                        Bpm         = (int)last.Pulse,
                        Spo2        = (int)last.SpO2,
                        Temperature = last.Temperature,
                        Neo6m       = string.IsNullOrWhiteSpace(last.Coordinates) ? null : last.Coordinates,
                        IsFall      = last.IsFall,
                        Activity    = last.Activity,
                        Mpu6050     = new List<int>(), // Liste goale (fără date de accelerometru)
                        Gyro        = new List<int>()
                    };
                }
            }
        }

        // Generates measurements spread every 30 minutes from midnight to now for today,
        // so the charts have data immediately after a fresh deploy or DB reset.
        // Inserts directly into DbContext to avoid triggering alert notifications.
        // Populează baza de date cu măsurători simulate pentru ziua curentă (la 30 min interval)
        public async Task SeedTodayAsync(Guid personId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Context.LifeAlertPlusDbContext>();

            // Verificăm că persoana există și are un serial de dispozitiv
            var monitored = await db.Monitoreds.FirstOrDefaultAsync(m => m.Id == personId);
            if (monitored == null || string.IsNullOrWhiteSpace(monitored.DeviceSerialNumber)) return;

            var serial = monitored.DeviceSerialNumber.Trim();
            var nowUtc = DateTime.UtcNow;
            var startOfDay = nowUtc.Date; // Miezul nopții în UTC

            var measurements = new List<Domain.Entities.Measurement>();
            var timestamp = startOfDay;
            // Generăm o măsurătoare la fiecare 30 de minute de la miezul nopții până acum
            while (timestamp <= nowUtc)
            {
                var payload = ESPDataGenerator.GeneratePayload(serial); // Generăm date normale (non-alerta)
                measurements.Add(new Domain.Entities.Measurement
                {
                    Id          = Guid.NewGuid(),
                    Name        = "Seed Data", // Marcăm ca date seed (nu reale)
                    Activity    = "stationary",
                    IsFall      = false,
                    IdMonitored = personId,
                    Pulse       = payload.Bpm ?? 75,
                    SpO2        = payload.Spo2 ?? 97,
                    Temperature = payload.Temperature ?? 36.6,
                    Coordinates = payload.Neo6m ?? string.Empty,
                    CreatedAt   = timestamp // Backdate-ul fiecărei înregistrări
                });
                timestamp = timestamp.AddMinutes(30); // Avansăm cu 30 de minute
            }

            await db.Measurements.AddRangeAsync(measurements);
            await db.SaveChangesAsync();
            _logger.LogInformation("Seeded {Count} measurements for today for person {PersonId}", measurements.Count, personId);
        }

        // Deletes all zero-SpO2 seed measurements for today and regenerates with correct SpO2 values.
        // Șterge datele seed cu SpO2=0 (generate de versiuni vechi de firmware) și le regenerează
        public async Task ReseedTodayAsync(Guid personId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Context.LifeAlertPlusDbContext>();

            var todayUtc = DateTime.UtcNow.Date;

            // Găsim toate înregistrările seed de azi cu SpO2 invalid (0)
            var oldSeed = db.Measurements
                .Where(ms => ms.IdMonitored == personId
                          && ms.CreatedAt >= todayUtc
                          && ms.SpO2 == 0
                          && ms.Name == "Seed Data"); // Identificăm după eticheta "Seed Data"
            db.Measurements.RemoveRange(oldSeed); // Ștergem în bloc
            await db.SaveChangesAsync();

            await SeedTodayAsync(personId); // Regenerăm cu date corecte
            _logger.LogInformation("Reseeded today's data with SpO2 for person {PersonId}", personId);
        }

        // Pornește o simulare de date ESP pentru o persoană monitorizată
        // Simularea rulează într-un Task separat și generează date la intervalul specificat
        public async Task StartSimulationAsync(Guid personId, TimeSpan? interval = null)
        {
            // Check if already running
            // Nu pornim o a doua simulare dacă una deja rulează pentru această persoană
            if (_runs.ContainsKey(personId))
            {
                _logger.LogWarning("Simulation for person {PersonId} is already running", personId);
                return;
            }

            // Resolve a scoped DbContext to read the monitored device
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
            var monitored = await db.Monitoreds.FirstOrDefaultAsync(m => m.Id == personId);

            if (monitored == null)
            {
                _logger.LogWarning("Monitored person {PersonId} not found", personId);
                return;
            }

            if (string.IsNullOrWhiteSpace(monitored.DeviceSerialNumber))
            {
                _logger.LogWarning("Monitored person {PersonId} has no device serial number", personId);
                return;
            }

            var delay = interval ?? SimulationConfig.DefaultInterval; // Intervalul dintre generări (ex: 5 secunde)
            var serial = monitored.DeviceSerialNumber.Trim();
            var cts = new CancellationTokenSource(); // Token pentru oprirea simulării
            _simulationStartTimes[personId] = DateTime.UtcNow; // Marcăm momentul startului

            _logger.LogInformation("Starting simulation for person {PersonId} with serial {Serial}, interval {Interval}",
                personId, serial, delay);

            // Pornim bucla de simulare ca Task independent (nu blochează thread-ul curent)
            var task = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested) // Rulăm până la oprire
                {
                    try
                    {
                        // Determine if we're in the alert phase
                        // Primele 3 minute generează date de alertă (puls/SpO2 anormal)
                        var inAlertPhase = AlertPhaseDuration > TimeSpan.Zero
                            && _simulationStartTimes.TryGetValue(personId, out var started)
                            && (DateTime.UtcNow - started) < AlertPhaseDuration;

                        // Alegem generatorul corespunzător fazei curente
                        var payload = inAlertPhase
                            ? ESPDataGenerator.GenerateAlertPayload(serial) // Date anormale (testare alertă)
                            : ESPDataGenerator.GeneratePayload(serial); // Date normale (puls/SpO2 în limite)
                        _simulatedData[serial] = payload; // Actualizăm cache-ul în memorie

                        // Extragem valorile din payload pentru procesare
                        var pulse = payload.Max30100?[0] ?? 0;
                        var spo2 = payload.Max30100?[1] ?? 0;
                        var temp = payload.Temperature ?? 0;

                        _logger.LogDebug("Generated {Mode} data for {Serial}: Pulse={Pulse}, Temp={Temp}, SpO2={SpO2}",
                            inAlertPhase ? "ALERT" : "normal", serial, pulse, temp, spo2);

                        // Save to database
                        // Persistăm măsurătoarea în DB (același flux ca datele reale de la ESP)
                        using var innerScope = _scopeFactory.CreateScope();
                        var measurementService = innerScope.ServiceProvider.GetRequiredService<LifeAlertPlus.Application.IServices.IMeasurementService>();

                        var measurement = new LifeAlertPlus.Domain.Entities.Measurement
                        {
                            Id = Guid.NewGuid(),
                            Name = inAlertPhase ? "Alert Simulation" : "Simulated Data", // Eticheta indică sursa
                            Activity = "Auto-generated",
                            IsFall = false,
                            IdMonitored = personId,
                            Pulse = pulse,
                            SpO2 = spo2,
                            Temperature = temp,
                            Coordinates = payload.Neo6m ?? string.Empty,
                            CreatedAt = DateTime.UtcNow
                        };

                        await measurementService.AddMeasurementAsync(measurement);

                        // Feed data to the alert monitor so notifications can trigger
                        // Trimitem datele simulate și la sistemul de alertă (poate declanșa notificări)
                        await _alertMonitor.ProcessMeasurementAsync(personId, pulse, temp, spo2, false,
                            coordinates: payload.Neo6m ?? string.Empty);

                        _logger.LogDebug("Saved measurement to database for person {PersonId}", personId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error generating simulation data for person {PersonId}", personId);
                    }

                    try
                    {
                        // Așteptăm intervalul configurat înainte de următoarea generare
                        await Task.Delay(delay, cts.Token); // Delay anulabil (se oprește imediat la cancel)
                    }
                    catch (OperationCanceledException)
                    {
                        break; // Simularea a fost oprită — ieșim din buclă
                    }
                }

                _logger.LogInformation("Simulation loop ended for person {PersonId}", personId);
            }, CancellationToken.None); // Task-ul rulează independent, nu cu token-ul extern

            _runs.TryAdd(personId, (cts, task)); // Înregistrăm simularea ca activă
        }

        // Oprește simularea pentru o persoană specifică
        public async Task StopSimulationAsync(Guid personId)
        {
            // TryRemove returnează false dacă simularea nu era activă
            if (!_runs.TryRemove(personId, out var entry))
            {
                _logger.LogWarning("Attempted to stop non-running simulation for person {PersonId}", personId);
                return;
            }

            _logger.LogInformation("Stopping simulation for person {PersonId}", personId);
            await CancelAndWaitAsync(entry, personId); // Anulăm și așteptăm finalizarea
        }

        // Oprește toate simulările active (apelat la shutdown-ul aplicației)
        public async Task StopAllAsync()
        {
            var entries = _runs.ToArray(); // Snapshot al tuturor simulărilor active
            if (!entries.Any())
            {
                _logger.LogInformation("No running simulations to stop");
                return;
            }

            _logger.LogInformation("Stopping all {Count} running simulations", entries.Length);

            // Cancel all
            // Trimitem semnalul de anulare la toate simulările simultan
            foreach (var kvp in entries)
            {
                try
                {
                    kvp.Value.Cts.Cancel(); // Semnalizăm anularea (non-blocant)
                    _logger.LogDebug("Cancelled simulation for person {PersonId}", kvp.Key);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error cancelling simulation for person {PersonId}", kvp.Key);
                }
            }

            // Wait for all to complete
            // Așteptăm finalizarea tuturor task-urilor (sau timeout-ul de siguranță)
            var tasks = entries.Select(e => e.Value.RunningTask).ToArray();
            try
            {
                // WhenAny: continuăm dacă fie toate task-urile s-au terminat, fie timeout-ul a expirat
                await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(SimulationConfig.StopTimeout));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error waiting for simulations to complete");
            }

            // Cleanup
            // Eliberăm resursele CancellationTokenSource și curățăm dicționarul
            foreach (var kvp in entries)
            {
                try { kvp.Value.Cts.Dispose(); } catch (Exception) { } // Best-effort dispose.
                _runs.TryRemove(kvp.Key, out _);
            }

            _logger.LogInformation("All simulations stopped");
        }

        // Helper: anulează o simulare și așteaptă finalizarea ei cu timeout de siguranță
        private async Task CancelAndWaitAsync((CancellationTokenSource Cts, Task RunningTask) entry, Guid personId)
        {
            try
            {
                entry.Cts.Cancel(); // Trimitem semnalul de anulare
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cancelling simulation for person {PersonId}", personId);
            }

            try
            {
                // Așteptăm maxim StopTimeout secunde pentru finalizarea task-ului
                await Task.WhenAny(entry.RunningTask, Task.Delay(SimulationConfig.StopTimeout));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error waiting for simulation to stop for person {PersonId}", personId);
            }

            try
            {
                entry.Cts.Dispose(); // Eliberăm memoria CancellationTokenSource
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing cancellation token for person {PersonId}", personId);
            }
        }
    }
}
