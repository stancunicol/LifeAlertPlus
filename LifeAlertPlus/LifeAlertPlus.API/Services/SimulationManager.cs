using System.Collections.Concurrent;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using LifeAlertPlus.Shared.Helpers;
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

            _logger.LogInformation("Starting simulation for person {PersonId} with serial {Serial}, interval {Interval}", 
                personId, serial, delay);

            var task = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        var payload = ESPDataGenerator.GeneratePayload(serial);
                        _simulatedData[serial] = payload;
                        
                        _logger.LogDebug("Generated simulation data for {Serial}: Pulse={Pulse}, Temp={Temp}, SpO2={SpO2}", 
                            serial, payload.Max30100?[0], payload.Temperature, payload.Max30100?[1]);
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
                try { kvp.Value.Cts.Dispose(); } catch { }
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
