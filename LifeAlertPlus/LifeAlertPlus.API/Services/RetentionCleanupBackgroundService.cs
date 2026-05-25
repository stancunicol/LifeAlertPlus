using Microsoft.Extensions.Hosting;

namespace LifeAlertPlus.API.Services
{
    // Runs the per-Monitored retention cleanup once per day at ~03:00 UTC.
    public class RetentionCleanupBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RetentionCleanupBackgroundService> _logger;

        public RetentionCleanupBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<RetentionCleanupBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Retention Cleanup Background Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    var nextRun = now.Date.AddDays(1).AddHours(3); // tomorrow 03:00 UTC
                    if (nextRun <= now) nextRun = nextRun.AddDays(1);
                    var delay = nextRun - now;

                    _logger.LogInformation("Next retention cleanup scheduled in {Hours}h {Minutes}m",
                        (int)delay.TotalHours, delay.Minutes);

                    await Task.Delay(delay, stoppingToken);

                    if (stoppingToken.IsCancellationRequested) break;

                    _logger.LogInformation("Executing retention cleanup at {Time} UTC", DateTime.UtcNow);

                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<RetentionCleanupService>();
                    var deleted = await service.RunAsync(stoppingToken);

                    _logger.LogInformation("Retention cleanup completed at {Time} UTC, total rows deleted: {Deleted}",
                        DateTime.UtcNow, deleted);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Retention Cleanup Service is stopping");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Retention Cleanup Background Service");
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }

            _logger.LogInformation("Retention Cleanup Background Service stopped");
        }
    }
}
