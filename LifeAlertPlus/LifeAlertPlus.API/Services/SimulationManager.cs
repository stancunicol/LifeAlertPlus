using System.Collections.Concurrent;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using LifeAlertPlus.Shared.DTOs.Requests.ESP;
using LifeAlertPlus.Shared.Helpers;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.API.Services
{
    public class SimulationManager
    {
        private readonly ConcurrentDictionary<Guid, (CancellationTokenSource Cts, Task RunningTask)> _runs = new();
        private readonly ConcurrentDictionary<string, ESPDataResponseDTO> _simulatedData = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<Guid, DateTime> _simulationStartTimes = new();
        private readonly ConcurrentDictionary<string, (DateTime ReceivedAt, ESPHeartbeatDTO Data)> _heartbeats = new(StringComparer.OrdinalIgnoreCase);
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SimulationManager> _logger;
        private readonly AlertMonitorService _alertMonitor;

        // Duration from simulation start during which alert-level data is generated.
        private static readonly TimeSpan AlertPhaseDuration = TimeSpan.FromMinutes(3);

        public SimulationManager(IServiceScopeFactory scopeFactory, ILogger<SimulationManager> logger, AlertMonitorService alertMonitor)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _alertMonitor = alertMonitor;
        }

        public ESPDataResponseDTO? GetData(string serial)
        {
            if (_simulatedData.TryGetValue(serial, out var data))
                return data;
            return null;
        }

        public void SetData(ESPDataResponseDTO payload)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.Serial))
                return;

            // Normalize: if Bpm/Spo2 are missing but Max30100 is present, backfill
            if (!payload.Bpm.HasValue && payload.Max30100?.Count >= 1)
                payload.Bpm = payload.Max30100[0];
            if (!payload.Spo2.HasValue && payload.Max30100?.Count >= 2)
                payload.Spo2 = payload.Max30100[1];

            _simulatedData[payload.Serial.Trim()] = payload;
        }

        public bool ClearData(string serial)
        {
            var key = serial.Trim();
            var removed = _simulatedData.TryRemove(key, out _);
            _heartbeats.TryRemove(key, out _);
            return removed;
        }

        public void SetHeartbeat(string serial, ESPHeartbeatDTO data)
            => _heartbeats[serial] = (DateTime.UtcNow, data);

        public (DateTime ReceivedAt, ESPHeartbeatDTO Data)? GetHeartbeat(string serial)
            => _heartbeats.TryGetValue(serial, out var h) ? h : null;

        public IEnumerable<Guid> GetRunningPersonIds() => _runs.Keys.ToList();

        public async Task StartAllAsync(TimeSpan? interval = null)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
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

            var monitoreds = await db.Monitoreds
                .Where(m => !string.IsNullOrWhiteSpace(m.DeviceSerialNumber) && !m.IsArchived)
                .ToListAsync();

            var todayUtc = DateTime.UtcNow.Date;

            foreach (var m in monitoreds)
            {
                var serial = m.DeviceSerialNumber.Trim();

                var last = await db.Measurements
                    .Where(ms => ms.IdMonitored == m.Id)
                    .OrderByDescending(ms => ms.CreatedAt)
                    .FirstOrDefaultAsync();

                // Populate live-data cache for the cards
                if (!_simulatedData.ContainsKey(serial) && last != null)
                {
                    _simulatedData[serial] = new LifeAlertPlus.Shared.DTOs.Responses.ESP.ESPDataResponseDTO
                    {
                        Serial      = serial,
                        Date        = new DateTimeOffset(last.CreatedAt).ToUnixTimeSeconds(),
                        IsAvailable = true,
                        Bpm         = (int)last.Pulse,
                        Spo2        = (int)last.SpO2,
                        Temperature = last.Temperature,
                        Neo6m       = string.IsNullOrWhiteSpace(last.Coordinates) ? null : last.Coordinates,
                        IsFall      = last.IsFall,
                        Activity    = last.Activity,
                        Mpu6050     = new List<int>(),
                        Gyro        = new List<int>()
                    };
                }

                // Seed today if there are no measurements with valid SpO2 for today
                var hasTodaySpO2 = await db.Measurements
                    .AnyAsync(ms => ms.IdMonitored == m.Id
                                 && ms.CreatedAt >= todayUtc
                                 && ms.SpO2 > 0);

                if (!hasTodaySpO2)
                {
                    _logger.LogInformation("No SpO2 data for today for {PersonId} — auto-seeding chart data", m.Id);
                    await SeedTodayAsync(m.Id);
                }
            }
        }

        // Generates measurements spread every 30 minutes from midnight to now for today,
        // so the charts have data immediately after a fresh deploy or DB reset.
        // Inserts directly into DbContext to avoid triggering alert notifications.
        public async Task SeedTodayAsync(Guid personId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Context.LifeAlertPlusDbContext>();

            var monitored = await db.Monitoreds.FirstOrDefaultAsync(m => m.Id == personId);
            if (monitored == null || string.IsNullOrWhiteSpace(monitored.DeviceSerialNumber)) return;

            var serial = monitored.DeviceSerialNumber.Trim();
            var nowUtc = DateTime.UtcNow;
            var startOfDay = nowUtc.Date;

            var measurements = new List<Domain.Entities.Measurement>();
            var timestamp = startOfDay;
            while (timestamp <= nowUtc)
            {
                var payload = ESPDataGenerator.GeneratePayload(serial);
                measurements.Add(new Domain.Entities.Measurement
                {
                    Id          = Guid.NewGuid(),
                    Name        = "Seed Data",
                    Activity    = "stationary",
                    IsFall      = false,
                    IdMonitored = personId,
                    Pulse       = payload.Bpm ?? 75,
                    SpO2        = payload.Spo2 ?? 97,
                    Temperature = payload.Temperature ?? 36.6,
                    Coordinates = payload.Neo6m ?? string.Empty,
                    CreatedAt   = timestamp
                });
                timestamp = timestamp.AddMinutes(30);
            }

            await db.Measurements.AddRangeAsync(measurements);
            await db.SaveChangesAsync();
            _logger.LogInformation("Seeded {Count} measurements for today for person {PersonId}", measurements.Count, personId);
        }

        // Deletes all zero-SpO2 seed measurements for today and regenerates with correct SpO2 values.
        public async Task ReseedTodayAsync(Guid personId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Context.LifeAlertPlusDbContext>();

            var todayUtc = DateTime.UtcNow.Date;

            var oldSeed = db.Measurements
                .Where(ms => ms.IdMonitored == personId
                          && ms.CreatedAt >= todayUtc
                          && ms.SpO2 == 0
                          && ms.Name == "Seed Data");
            db.Measurements.RemoveRange(oldSeed);
            await db.SaveChangesAsync();

            await SeedTodayAsync(personId);
            _logger.LogInformation("Reseeded today's data with SpO2 for person {PersonId}", personId);
        }

        public async Task StartSimulationAsync(Guid personId, TimeSpan? interval = null)
        {
            // Check if already running
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

            var delay = interval ?? SimulationConfig.DefaultInterval;
            var serial = monitored.DeviceSerialNumber.Trim();
            var cts = new CancellationTokenSource();
            _simulationStartTimes[personId] = DateTime.UtcNow;

            _logger.LogInformation("Starting simulation for person {PersonId} with serial {Serial}, interval {Interval}", 
                personId, serial, delay);

            var task = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        // Determine if we're in the alert phase
                        var inAlertPhase = AlertPhaseDuration > TimeSpan.Zero
                            && _simulationStartTimes.TryGetValue(personId, out var started)
                            && (DateTime.UtcNow - started) < AlertPhaseDuration;

                        var payload = inAlertPhase
                            ? ESPDataGenerator.GenerateAlertPayload(serial)
                            : ESPDataGenerator.GeneratePayload(serial);
                        _simulatedData[serial] = payload;

                        var pulse = payload.Max30100?[0] ?? 0;
                        var spo2 = payload.Max30100?[1] ?? 0;
                        var temp = payload.Temperature ?? 0;
                        
                        _logger.LogDebug("Generated {Mode} data for {Serial}: Pulse={Pulse}, Temp={Temp}, SpO2={SpO2}", 
                            inAlertPhase ? "ALERT" : "normal", serial, pulse, temp, spo2);

                        // Save to database
                        using var innerScope = _scopeFactory.CreateScope();
                        var measurementService = innerScope.ServiceProvider.GetRequiredService<LifeAlertPlus.Application.IServices.IMeasurementService>();
                        
                        var measurement = new LifeAlertPlus.Domain.Entities.Measurement
                        {
                            Id = Guid.NewGuid(),
                            Name = inAlertPhase ? "Alert Simulation" : "Simulated Data",
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
                        await Task.Delay(delay, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                _logger.LogInformation("Simulation loop ended for person {PersonId}", personId);
            }, CancellationToken.None);

            _runs.TryAdd(personId, (cts, task));
        }

        public async Task StopSimulationAsync(Guid personId)
        {
            if (!_runs.TryRemove(personId, out var entry))
            {
                _logger.LogWarning("Attempted to stop non-running simulation for person {PersonId}", personId);
                return;
            }

            _logger.LogInformation("Stopping simulation for person {PersonId}", personId);
            await CancelAndWaitAsync(entry, personId);
        }

        public async Task StopAllAsync()
        {
            var entries = _runs.ToArray();
            if (!entries.Any())
            {
                _logger.LogInformation("No running simulations to stop");
                return;
            }

            _logger.LogInformation("Stopping all {Count} running simulations", entries.Length);

            // Cancel all
            foreach (var kvp in entries)
            {
                try 
                { 
                    kvp.Value.Cts.Cancel(); 
                    _logger.LogDebug("Cancelled simulation for person {PersonId}", kvp.Key);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error cancelling simulation for person {PersonId}", kvp.Key);
                }
            }

            // Wait for all to complete
            var tasks = entries.Select(e => e.Value.RunningTask).ToArray();
            try 
            { 
                await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(SimulationConfig.StopTimeout)); 
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error waiting for simulations to complete");
            }

            // Cleanup
            foreach (var kvp in entries)
            {
                try { kvp.Value.Cts.Dispose(); } catch (Exception) { } // Best-effort dispose.
                _runs.TryRemove(kvp.Key, out _);
            }

            _logger.LogInformation("All simulations stopped");
        }

        private async Task CancelAndWaitAsync((CancellationTokenSource Cts, Task RunningTask) entry, Guid personId)
        {
            try 
            { 
                entry.Cts.Cancel(); 
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cancelling simulation for person {PersonId}", personId);
            }

            try 
            { 
                await Task.WhenAny(entry.RunningTask, Task.Delay(SimulationConfig.StopTimeout)); 
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error waiting for simulation to stop for person {PersonId}", personId);
            }

            try 
            { 
                entry.Cts.Dispose(); 
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing cancellation token for person {PersonId}", personId);
            }
        }
    }
}
