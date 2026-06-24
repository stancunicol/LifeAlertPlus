using Microsoft.Extensions.Hosting;

namespace LifeAlertPlus.API.Services
{
    // Serviciu de background care rulează RetentionCleanupService zilnic la 03:00 UTC.
    // Ora 03:00 UTC a fost aleasă deoarece este perioada cu cel mai puțin trafic
    // (dimineața devreme în Europa, noaptea în SUA) — minimizăm impactul asupra performanței.
    // Extinde BackgroundService (hosted service ASP.NET Core) — rulează pe toată durata aplicației.
    public class RetentionCleanupBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory; // Creare scope pentru RetentionCleanupService (Scoped)
        private readonly ILogger<RetentionCleanupBackgroundService> _logger;

        public RetentionCleanupBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<RetentionCleanupBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger       = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Retention Cleanup Background Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    // Calculăm ora următoarei rulări: mâine la 03:00 UTC
                    var nextRun = now.Date.AddDays(1).AddHours(3);
                    if (nextRun <= now) nextRun = nextRun.AddDays(1); // Siguranță: nu rulăm în trecut
                    var delay = nextRun - now; // Cât timp mai așteptăm

                    _logger.LogInformation("Next retention cleanup scheduled in {Hours}h {Minutes}m",
                        (int)delay.TotalHours, delay.Minutes);

                    await Task.Delay(delay, stoppingToken); // Așteptăm până la 03:00 UTC

                    if (stoppingToken.IsCancellationRequested) break; // Aplicația se închide

                    _logger.LogInformation("Executing retention cleanup at {Time} UTC", DateTime.UtcNow);

                    // Creăm un scope nou pentru a accesa RetentionCleanupService (Scoped)
                    using var scope   = _scopeFactory.CreateScope();
                    var service       = scope.ServiceProvider.GetRequiredService<RetentionCleanupService>();
                    var deleted       = await service.RunAsync(stoppingToken); // Execuție efectivă

                    _logger.LogInformation("Retention cleanup completed at {Time} UTC, total rows deleted: {Deleted}",
                        DateTime.UtcNow, deleted);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Retention Cleanup Service is stopping");
                    break; // Oprire normală la shutdown-ul aplicației
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Retention Cleanup Background Service");
                    // La eroare neașteptată: așteptăm 1 oră și reîncercăm (nu blocăm loop-ul pentru totdeauna)
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }

            _logger.LogInformation("Retention Cleanup Background Service stopped");
        }
    }
}
