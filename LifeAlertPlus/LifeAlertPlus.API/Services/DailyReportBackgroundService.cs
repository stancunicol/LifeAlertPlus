using Microsoft.Extensions.Hosting;

namespace LifeAlertPlus.API.Services
{
    public class DailyReportBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DailyReportBackgroundService> _logger;

        public DailyReportBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<DailyReportBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Daily Report Background Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    var nextMidnight = now.Date.AddDays(1);
                    var timeUntilMidnight = nextMidnight - now;

                    if (timeUntilMidnight.TotalSeconds < 0)
                        timeUntilMidnight = timeUntilMidnight.Add(TimeSpan.FromDays(1));

                    _logger.LogInformation("Next report scheduled in {Hours}h {Minutes}m",
                        (int)timeUntilMidnight.TotalHours,
                        timeUntilMidnight.Minutes);

                    await Task.Delay(timeUntilMidnight, stoppingToken);

                    if (stoppingToken.IsCancellationRequested)
                        break;

                    _logger.LogInformation("Executing daily reports at {Time} UTC", DateTime.UtcNow);

                    using var scope = _scopeFactory.CreateScope();
                    var reportService = scope.ServiceProvider.GetRequiredService<DailyReportService>();
                    await reportService.SendDailyReportsAsync();

                    _logger.LogInformation("Daily reports completed at {Time} UTC", DateTime.UtcNow);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Daily Report Service is stopping");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Daily Report Background Service");
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }

            _logger.LogInformation("Daily Report Background Service stopped");
        }
    }
}
