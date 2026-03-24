using System.Collections.Concurrent;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.API.Services
{
    public class SimulationManager
    {
        private readonly ConcurrentDictionary<Guid, (CancellationTokenSource Cts, Task RunningTask)> _runs = new();
        private readonly ConcurrentDictionary<string, ESPDataResponseDTO> _simulatedData = new(StringComparer.OrdinalIgnoreCase);
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SimulationManager> _logger;

        public SimulationManager(IServiceScopeFactory scopeFactory, ILogger<SimulationManager> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
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

            _simulatedData[payload.Serial.Trim()] = payload;
        }

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

        public async Task StartSimulationAsync(Guid personId, TimeSpan? interval = null)
        {
            // Resolve a scoped DbContext to read the monitored device
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
            var monitored = await db.Monitoreds.FirstOrDefaultAsync(m => m.Id == personId);
            if (monitored == null || string.IsNullOrWhiteSpace(monitored.DeviceSerialNumber))
                return;

            var delay = interval ?? TimeSpan.FromMinutes(2);
            var serial = monitored.DeviceSerialNumber.Trim();
            var cts = new CancellationTokenSource();

            var task = Task.Run(async () =>
            {
                var rnd = Random.Shared;
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        var pulse = rnd.Next(62, 101);
                        var spo2 = rnd.Next(93, 99);
                        var temp = 36.2 + rnd.NextDouble() * 1.2;
                        var battery = 30 + rnd.NextDouble() * 70;

                        var payload = new ESPDataResponseDTO
                        {
                            Serial = serial,
                            Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            IsAvailable = true,
                            Mpu6050 = new List<int> { rnd.Next(-16000, 16001), rnd.Next(-16000, 16001), rnd.Next(-16000, 16001) },
                            Gyro = new List<int> { rnd.Next(-5000, 5001), rnd.Next(-5000, 5001), rnd.Next(-5000, 5001) },
                            Max30100 = new List<int> { pulse, spo2 },
                            Neo6m = "$GPRMC,123519,A,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W*6A",
                            Temperature = Math.Round(temp, 1),
                            Battery = Math.Round(battery, 1),
                            ErrorMessage = null
                        };

                        _simulatedData[serial] = payload;
                        _logger.LogInformation("Simulation generated data for {Serial}: Pulse={Pulse}, Temp={Temp}", serial, pulse, temp);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in simulation loop for person {PersonId}", personId);
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
            }, CancellationToken.None);

            if (!_runs.TryAdd(personId, (cts, task)))
            {
                if (_runs.TryGetValue(personId, out var existing))
                {
                    try { existing.Cts.Cancel(); } catch { }
                }
                _runs[personId] = (cts, task);
            }
        }

        public async Task StopSimulationAsync(Guid personId)
        {
            if (_runs.TryRemove(personId, out var entry))
            {
                try { entry.Cts.Cancel(); } catch { }
                try { await Task.WhenAny(entry.RunningTask, Task.Delay(5000)); } catch { }
                try { entry.Cts.Dispose(); } catch { }
            }
        }

        public async Task StopAllAsync()
        {
            var entries = _runs.ToArray();
            foreach (var kvp in entries)
            {
                try { kvp.Value.Cts.Cancel(); } catch { }
            }

            var tasks = entries.Select(e => e.Value.RunningTask).ToArray();
            try { await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(5000)); } catch { }

            foreach (var kvp in entries)
            {
                try { kvp.Value.Cts.Dispose(); } catch { }
                _runs.TryRemove(kvp.Key, out _);
            }
        }
    }
}
