using Microsoft.Extensions.Hosting;

namespace LifeAlertPlus.API.Services
{
    // Rebuilds the 7-day rolling behavioral profile for every active monitored
    // person once a day at ~02:00 UTC. This makes the profile "roll forward" by
    // one day automatically, instead of relying on lazy rebuilds triggered by
    // incoming measurements.
    public class ActivityProfileRebuildBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ActivityProfileRebuildBackgroundService> _logger;

        public ActivityProfileRebuildBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<ActivityProfileRebuildBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Activity Profile Rebuild Background Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    var nextRun = now.Date.AddDays(1).AddHours(2); // tomorrow 02:00 UTC
                    if (nextRun <= now) nextRun = nextRun.AddDays(1);
                    var delay = nextRun - now;

                    _logger.LogInformation("Next activity profile rebuild scheduled in {Hours}h {Minutes}m",
                        (int)delay.TotalHours, delay.Minutes);

                    await Task.Delay(delay, stoppingToken);

                    if (stoppingToken.IsCancellationRequested) break;

                    _logger.LogInformation("Executing activity profile rebuild at {Time} UTC", DateTime.UtcNow);

                    using var scope = _scopeFactory.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<ActivityProfileService>();
                    var rebuilt = await svc.RebuildAllActiveAsync(stoppingToken);

                    _logger.LogInformation("Activity profile rebuild completed at {Time} UTC, profiles rebuilt: {Count}",
                        DateTime.UtcNow, rebuilt);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Activity Profile Rebuild Service is stopping");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Activity Profile Rebuild Background Service");
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }

            _logger.LogInformation("Activity Profile Rebuild Background Service stopped");
        }
    }
}
