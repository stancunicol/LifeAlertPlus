using Microsoft.Extensions.Hosting;

namespace LifeAlertPlus.API.Services
{
    // Serviciu de background care rulează DailyReportService zilnic la miezul nopții LOCAL.
    // Ora de executare este configurată prin DailyReport:LocalTimeZone în appsettings.
    // Dacă fusul orar nu este configurat sau e invalid, se folosește UTC ca fallback.
    // MOTIVARE: Raportul trebuie trimis la miezul nopții ora locală a utilizatorului
    // (nu UTC) pentru că acoperă "ziua de ieri" din perspectiva utilizatorului.
    public class DailyReportBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;    // Scope pentru DailyReportService
        private readonly IConfiguration _configuration;         // Citire DailyReport:LocalTimeZone
        private readonly ILogger<DailyReportBackgroundService> _logger;

        public DailyReportBackgroundService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<DailyReportBackgroundService> logger)
        {
            _scopeFactory   = scopeFactory;
            _configuration  = configuration;
            _logger         = logger;
        }

        // Rezolvă fusul orar configurabil.
        // Dacă ID-ul fusului orar lipsește sau e necunoscut (IANA vs Windows diferă),
        // se folosește UTC ca fallback sigur.
        private TimeZoneInfo GetLocalTimeZone()
        {
            var tzId = _configuration["DailyReport:LocalTimeZone"]; // Ex: "Europe/Bucharest"
            if (string.IsNullOrWhiteSpace(tzId)) return TimeZoneInfo.Utc;
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(tzId); // IANA sau Windows format
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unknown DailyReport:LocalTimeZone '{Tz}'. Falling back to UTC.", tzId);
                return TimeZoneInfo.Utc;
            }
        }

        // Calculează data și ora UTC pentru miezul nopții LOCALE de mâine.
        // Ex: dacă e 22:00 EET (UTC+2), miezul nopții locale = 22:00 UTC
        private DateTime ComputeNextLocalMidnightUtc(TimeZoneInfo tz)
        {
            var nowUtc           = DateTime.UtcNow;
            var nowLocal         = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz); // Convertim la ora locală
            var nextLocalMidnight = DateTime.SpecifyKind(nowLocal.Date.AddDays(1), DateTimeKind.Unspecified); // Mâine 00:00:00 local
            return TimeZoneInfo.ConvertTimeToUtc(nextLocalMidnight, tz); // Convertim înapoi la UTC
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Daily Report Background Service started");
            var tz = GetLocalTimeZone(); // Citim fusul orar o singură dată la start

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Calculăm cât timp mai așteptăm până la miezul nopții locale
                    var nextRunUtc = ComputeNextLocalMidnightUtc(tz);
                    var delay      = nextRunUtc - DateTime.UtcNow;
                    if (delay < TimeSpan.Zero) delay = TimeSpan.FromMinutes(1); // Failsafe: nu așteptăm negativ

                    _logger.LogInformation(
                        "Next daily report scheduled at {LocalTime} {Tz} ({Hours}h {Minutes}m from now)",
                        TimeZoneInfo.ConvertTimeFromUtc(nextRunUtc, tz), // Afișăm ora locală în log
                        tz.Id,
                        (int)delay.TotalHours,
                        delay.Minutes);

                    await Task.Delay(delay, stoppingToken); // Așteptăm până la miezul nopții

                    if (stoppingToken.IsCancellationRequested)
                        break;

                    _logger.LogInformation("Executing daily reports at {Time} UTC", DateTime.UtcNow);

                    // Creăm scope nou și apelăm DailyReportService
                    using var scope   = _scopeFactory.CreateScope();
                    var reportService = scope.ServiceProvider.GetRequiredService<DailyReportService>();
                    await reportService.SendDailyReportsAsync();

                    _logger.LogInformation("Daily reports completed at {Time} UTC", DateTime.UtcNow);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Daily Report Service is stopping");
                    break; // Oprire normală
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Daily Report Background Service");
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken); // Reîncercăm după 1 oră
                }
            }

            _logger.LogInformation("Daily Report Background Service stopped");
        }
    }
}
