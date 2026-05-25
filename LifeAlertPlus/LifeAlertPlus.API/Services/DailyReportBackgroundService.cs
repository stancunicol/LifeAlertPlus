using Microsoft.Extensions.Hosting;

namespace LifeAlertPlus.API.Services
{
    public class DailyReportBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DailyReportBackgroundService> _logger;

        public DailyReportBackgroundService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<DailyReportBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        // Resolve the configured local time zone. Falls back to UTC if the ID is
        // missing or unknown (e.g. on Windows hosts that ship IANA differently).
        private TimeZoneInfo GetLocalTimeZone()
        {
            var tzId = _configuration["DailyReport:LocalTimeZone"];
            if (string.IsNullOrWhiteSpace(tzId)) return TimeZoneInfo.Utc;
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(tzId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unknown DailyReport:LocalTimeZone '{Tz}'. Falling back to UTC.", tzId);
                return TimeZoneInfo.Utc;
            }
        }

        // Next 00:00 in the configured local time zone, expressed as UTC.
        private DateTime ComputeNextLocalMidnightUtc(TimeZoneInfo tz)
        {
            var nowUtc = DateTime.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
            var nextLocalMidnight = DateTime.SpecifyKind(nowLocal.Date.AddDays(1), DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(nextLocalMidnight, tz);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Daily Report Background Service started");
            var tz = GetLocalTimeZone();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var nextRunUtc = ComputeNextLocalMidnightUtc(tz);
                    var delay = nextRunUtc - DateTime.UtcNow;
                    if (delay < TimeSpan.Zero) delay = TimeSpan.FromMinutes(1);

                    _logger.LogInformation(
                        "Next daily report scheduled at {LocalTime} {Tz} ({Hours}h {Minutes}m from now)",
                        TimeZoneInfo.ConvertTimeFromUtc(nextRunUtc, tz),
                        tz.Id,
                        (int)delay.TotalHours,
                        delay.Minutes);

                    await Task.Delay(delay, stoppingToken);

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
