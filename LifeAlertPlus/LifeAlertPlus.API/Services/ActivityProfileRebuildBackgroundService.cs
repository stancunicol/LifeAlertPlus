using Microsoft.Extensions.Hosting;

namespace LifeAlertPlus.API.Services
{
    // Serviciu de background care reconstruiește zilnic profilul comportamental al tuturor pacienților activi.
    // Rulează la 02:00 UTC (devreme dimineața, înaintea RetentionCleanup care pornește la 03:00 UTC).
    // SCOPUL: Profilul este bazat pe ultimele 7 zile — fără rebuild zilnic, "fereastra" nu avansează
    // și profilul rămâne îmbătrânit. Rebuild-ul zilnic asigură că profilul reflectă
    // comportamentul recent al pacientului, nu cel din urmă cu 8+ zile.
    public class ActivityProfileRebuildBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory; // Scope pentru ActivityProfileService
        private readonly ILogger<ActivityProfileRebuildBackgroundService> _logger;

        public ActivityProfileRebuildBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<ActivityProfileRebuildBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger       = logger;
        }

        // Apelată o singură dată de host la pornirea aplicației (BackgroundService o rulează automat ca
        // IHostedService singleton) și ține bucla activă pe toată durata de viață a procesului — nu e
        // re-invocată periodic din exterior, bucla "while" de mai jos E mecanismul de repetare zilnică.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Activity Profile Rebuild Background Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now     = DateTime.UtcNow;
                    // Ora 02:00 UTC (înainte de RetentionCleanup la 03:00) — profilul este actualizat
                    // ÎNAINTE ca datele vechi să fie șterse, astfel profilul rămâne consistent.
                    // NOTĂ: calculul e mereu "ZIUA URMĂTOARE la 02:00", nu "următorul 02:00 care vine" —
                    // chiar dacă serviciul pornește la, ex., 00:30 (înainte ca 02:00 de azi să treacă),
                    // tot programează "mâine 02:00", nu "azi 02:00". Primul rebuild după un (re)pornire
                    // e deci mereu la cel puțin ~24h distanță, niciodată mai rapid.
                    var nextRun = now.Date.AddDays(1).AddHours(2);
                    if (nextRun <= now) nextRun = nextRun.AddDays(1); // Guard defensiv — în condiții normale nu se declanșează niciodată, fiindcă nextRun e deja mereu în viitor
                    var delay   = nextRun - now;

                    _logger.LogInformation("Next activity profile rebuild scheduled in {Hours}h {Minutes}m",
                        (int)delay.TotalHours, delay.Minutes);

                    // Task.Delay e asincron — NU blochează un thread cât așteaptă (poate fi ore).
                    // Primește stoppingToken: dacă aplicația se închide în timpul așteptării, delay-ul
                    // se întrerupe imediat (aruncă OperationCanceledException) în loc să mai stea degeaba.
                    await Task.Delay(delay, stoppingToken);

                    // Verificare defensivă suplimentară — pentru cazul rar în care token-ul a fost semnalat
                    // exact când Task.Delay se termina natural (race condition minoră la shutdown)
                    if (stoppingToken.IsCancellationRequested) break;

                    _logger.LogInformation("Executing activity profile rebuild at {Time} UTC", DateTime.UtcNow);

                    // Creăm un scope DI nou de fiecare dată — BackgroundService e singleton (trăiește cât
                    // toată aplicația), dar ActivityProfileService depinde de DbContext, care e scoped
                    // (nu poate fi ținut deschis la nesfârșit). Scope-ul e aruncat automat la finalul
                    // blocului 'using', imediat după ce rebuild-ul s-a terminat.
                    using var scope = _scopeFactory.CreateScope();
                    var svc         = scope.ServiceProvider.GetRequiredService<ActivityProfileService>();
                    var rebuilt     = await svc.RebuildAllActiveAsync(stoppingToken); // Reconstruiește profilul tuturor pacienților activi (fereastra de 7 zile)

                    _logger.LogInformation("Activity profile rebuild completed at {Time} UTC, profiles rebuilt: {Count}",
                        DateTime.UtcNow, rebuilt);
                }
                catch (OperationCanceledException)
                {
                    // Shutdown normal al aplicației (Task.Delay sau RebuildAllActiveAsync întrerupte de stoppingToken) — nu e o eroare reală
                    _logger.LogInformation("Activity Profile Rebuild Service is stopping");
                    break; // Oprire elegantă — ieșim din while, metoda se termină
                }
                catch (Exception ex)
                {
                    // Eroare reală (ex: DB indisponibilă) — NU oprim serviciul, doar logăm și continuăm bucla.
                    // IMPORTANT: pauza de 1 oră de mai jos NU e o reîncercare a rebuild-ului în aceeași zi —
                    // după ea, bucla se reîntoarce la începutul while-ului și recalculează nextRun ca
                    // "ziua următoare la 02:00" (la fel ca mai sus), care e tot la ~23-24h distanță.
                    // Rolul real al pauzei: previne o avalanșă de log-uri de eroare dacă problema e
                    // persistentă (ex: DB complet picată) — e un throttle pe rata de logare, nu o strategie
                    // de retry same-day a rebuild-ului propriu-zis.
                    _logger.LogError(ex, "Error in Activity Profile Rebuild Background Service");
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }

            _logger.LogInformation("Activity Profile Rebuild Background Service stopped");
        }
    }
}
