using System.Collections.Concurrent;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LifeAlertPlus.API.Services
{
    // Serviciu pentru construirea și utilizarea profilului comportamental al pacientului.
    // Profilul reprezintă tiparele normale de activitate per oră (24 ore) pe baza ultimelor 7 zile.
    // Detectează anomalii comportamentale comparând activitatea curentă cu tiparul normal:
    //   - InactivityAnomaly: persoana nu s-a mișcat 15 minute la o oră în care era de obicei activă
    //   - NightActivity: mișcare detectată la o oră în care doarme de obicei
    // Profilul este recalculat zilnic (ActivityProfileRebuildBackgroundService) și cașat 4 ore în memorie.
    public class ActivityProfileService
    {
        // ServiceCollectionExtensions/Program.cs înregistrează acest serviciu ca Singleton (un singur obiect
        // pentru toată aplicația, partajat între cereri) — de aceea nu putem injecta direct DbContext (Scoped)
        // în constructor; trebuie creat un scope nou de fiecare dată când avem nevoie de DB (vezi mai jos).
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ActivityProfileService> _logger;

        // Buffer de activitate din ultimele 15 minute per persoană — ținut în memorie, NU în DB
        // (e date temporare, folosite doar pentru detecția de anomalii în timp real, nu pentru istoricul permanent).
        // Sincronizat cu lock pentru a preveni coruperea Queue la scrieri simultane
        // (mai multe pachete ESP pot ajunge în același timp, pe thread-uri diferite ale serverului web)
        private sealed class ActivityBuffer
        {
            public readonly object Sync = new(); // Lock per persoană (nu unul global) — scrierile pentru pacienți diferiți nu se blochează reciproc
            public Queue<(DateTime Timestamp, bool IsMoving)> Readings = new();
        }
        // ConcurrentDictionary — sigur pentru acces concurent din mai multe thread-uri fără lock extern pe dicționarul însuși
        // (lock-ul intern e doar pe Queue-ul fiecărei persoane, văzut mai sus)
        private readonly ConcurrentDictionary<Guid, ActivityBuffer> _activityBuffers = new();
        private static readonly TimeSpan ActivityWindow = TimeSpan.FromMinutes(15); // Fereastra glisantă pentru detecția de inactivitate

        // Cache profil orar în memorie: evităm interogări DB la fiecare măsurătoare (fiecare ~30 secunde per pacient) —
        // profilul se schimbă o singură dată pe zi (rebuild la 02:00 UTC), deci un cache de ore e perfect sigur
        private readonly ConcurrentDictionary<Guid, (DateTime CachedAt, ActivityProfile?[] Hours)> _profileCache = new();
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(4); // Cache valabil 4 ore — suficient de lung ca să reducă presiunea pe DB, suficient de scurt ca să prindă un rebuild nou în aceeași zi

        // Previne build-uri simultane ale profilului pentru aceeași persoană (ex: declanșat manual din UI
        // ȚI automat de rebuild-ul zilnic, exact în același moment) — folosit ca un "lock" ușor (TryAdd/TryRemove)
        private readonly ConcurrentDictionary<Guid, byte> _buildInProgress = new();

        // Cooldown de anomalie: nu repetăm o alertă comportamentală mai des de 30 minute — altfel, dacă persoana
        // rămâne nemișcată ore în șir, s-ar genera o notificare la fiecare măsurătoare (la fiecare ~30s), spam total
        private readonly ConcurrentDictionary<Guid, DateTime> _anomalyCooldowns = new();
        private static readonly TimeSpan AnomalyCooldown = TimeSpan.FromMinutes(30);

        private const int MinDataPoints = 20;              // Minim 20 de puncte de date per oră pentru a fi siguri de profil (sub acest prag, statistica orei e nesigură — prea puține măsurători)
        private const int MinInactiveReadings = 8;         // Minim 8 citiri consecutive fără mișcare ca să declanșăm anomalia (evită alerte false dintr-o singură citire ratată de senzor)
        private const double ActiveHourThreshold = 0.65;   // Ora e considerată "activă" dacă >65% din timp are mișcare — prag destul de înalt ca să evite fals-pozitive pe ore "moderat active"
        private const double SleepHourThreshold = 0.70;    // Ora e considerată "somn" dacă >70% din timp fără mișcare

        // Etichetele de activitate considerate ca "fără mișcare" (sedentare)
        private static readonly HashSet<string> SedentaryLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            "lying", "sleeping", "resting", "sedentary", "stationary", "sitting", "idle"
        };

        public ActivityProfileService(IServiceScopeFactory scopeFactory, ILogger<ActivityProfileService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger       = logger;
        }

        // Determină dacă eticheta de activitate înseamnă mișcare (orice altceva decât sedentare)
        private static bool IsMoving(string activity) =>
            !string.IsNullOrWhiteSpace(activity) && !SedentaryLabels.Contains(activity.Trim());

        // Verifică dacă activitatea curentă reprezintă o anomalie față de profilul normal al pacientului.
        // Apelată la fiecare măsurătoare nouă ESP (inclusiv din AlertMonitorService).
        // Returnează: (areAnomalie, mesajRO, mesajEN, tipAnomalie)
        public async Task<(bool HasAnomaly, string? MessageRo, string? MessageEn, string? AnomalyType)> CheckAnomalyAsync(
            Guid monitoredId, string activity, double pulse, DateTime now)
        {
            bool moving = IsMoving(activity);   // Clasificăm citirea curentă: mișcare / fără mișcare (vezi IsMoving + SedentaryLabels)

            // GetOrAdd: creează bufferul la prima măsurătoare a unei persoane, îl reutilizează la următoarele
            var buf = _activityBuffers.GetOrAdd(monitoredId, _ => new ActivityBuffer());
            bool[] recentReadings;
            lock (buf.Sync)   // Lock pe durata scrierii/citirii Queue-ului — previne coruperea dacă 2 măsurători ajung simultan pe thread-uri diferite
            {
                buf.Readings.Enqueue((now, moving));   // Adăugăm citirea curentă la finalul cozii
                // Eliminăm din capul cozii toate citirile mai vechi de 15 minute — fereastra glisantă rămâne mereu "ultimele 15 min"
                while (buf.Readings.Count > 0 && (now - buf.Readings.Peek().Timestamp) > ActivityWindow)
                    buf.Readings.Dequeue();
                // Facem un snapshot (.ToArray()) sub lock — restul metodei lucrează pe array, fără să mai țină lock-ul
                // (operațiile de mai jos pot dura — interogări DB — și n-ar trebui să blocheze alte thread-uri care scriu în buffer)
                recentReadings = buf.Readings.Select(x => x.IsMoving).ToArray();
            }

            // Verificăm cooldown-ul de anomalie — dacă am alertat deja în ultimele 30 minute pentru acest pacient,
            // ieșim imediat fără să mai facem interogări DB (optimizare + evitare spam de notificări)
            if (_anomalyCooldowns.TryGetValue(monitoredId, out var lastAnomaly) &&
                (now - lastAnomaly) < AnomalyCooldown)
                return (false, null, null, null);

            // Citim profilul orar din cache (sau DB dacă nu e în cache) — vezi GetCachedProfileAsync mai jos
            var profile = await GetCachedProfileAsync(monitoredId, now);
            if (profile == null) return (false, null, null, null); // Nu avem încă profil construit pentru acest pacient

            var hourProfile = profile[now.Hour]; // Indexare directă în array-ul de 24 poziții — profilul exact al orei curente
            if (hourProfile == null || hourProfile.DataPoints < MinDataPoints)
                return (false, null, null, null); // Ora curentă nu are suficiente date istorice ca să fie de încredere

            // ANOMALIE 1 — InactivityAnomaly: persoana e de obicei activă la această oră (>65% din zilele
            // anterioare avea mișcare), DAR în fereastra curentă de 15 minute nu s-a mișcat deloc.
            // Dublă condiție de siguranță: recentReadings.Length >= 8 (avem destule citiri recente, nu doar 1-2)
            // ȚI toate sunt "fără mișcare" (.All(m => !m)) — o singură citire ratată de senzor nu declanșează alerta.
            if (hourProfile.MovementRate > ActiveHourThreshold)
            {
                if (recentReadings.Length >= MinInactiveReadings && recentReadings.All(m => !m))
                {
                    _anomalyCooldowns[monitoredId] = now; // Setăm cooldown — următoarea alertă posibilă abia după 30 minute
                    int minutes = (int)ActivityWindow.TotalMinutes;
                    return (true,
                        $"Persoana nu s-a mișcat de {minutes} minute, deși la ora {now.Hour:00}:00 este de obicei activă ({hourProfile.MovementRate:P0} din timp).",
                        $"No movement detected for {minutes} minutes, even though the person is usually active at {now.Hour:00}:00 ({hourProfile.MovementRate:P0} of the time).",
                        "InactivityAnomaly");
                }
            }

            // ANOMALIE 2 — NightActivity: persoana doarme de obicei la această oră (>70% probabilitate somn
            // în istoricul de 7 zile), DAR măsurătoarea curentă arată mișcare. Spre diferență de Anomalia 1,
            // aici e suficientă O SINGURĂ citire cu mișcare (`moving`, nu recentReadings) — o trezire nocturnă
            // bruscă (posibil cădere/dezorientare) trebuie semnalată imediat, nu abia după 15 minute de pattern.
            if (hourProfile.SleepProbability > SleepHourThreshold && moving)
            {
                _anomalyCooldowns[monitoredId] = now;
                return (true,
                    $"Activitate detectată la ora {now.Hour:00}:00, oră la care persoana doarme de obicei ({hourProfile.SleepProbability:P0} din timp).",
                    $"Activity detected at {now.Hour:00}:00, an hour when the person is usually asleep ({hourProfile.SleepProbability:P0} of the time).",
                    "NightActivity");
            }

            return (false, null, null, null); // Niciuna din cele 2 anomalii nu s-a declanșat
        }

        // Fereastra temporală pentru construirea profilului (7 zile)
        // Publică pentru a fi referențiată din background job
        public static readonly TimeSpan ProfileWindow = TimeSpan.FromDays(7);

        // Construiește sau reconstruiește profilul orar din ultimele 7 zile de măsurători
        // Calculează per oră: rata de mișcare, probabilitatea de somn, pulsul mediu
        public async Task BuildProfileAsync(Guid monitoredId)
        {
            try
            {
                // Scope DI nou — la fel ca în ActivityProfileRebuildBackgroundService: serviciul e Singleton,
                // dar DbContext e Scoped, deci nu poate fi injectat direct în constructor
                using var scope = _scopeFactory.CreateScope();
                var db   = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
                var repo = scope.ServiceProvider.GetRequiredService<IActivityProfileRepository>();

                // Nu construim profilul pentru persoane arhivate (nu mai monitorizăm) — interogare minimală,
                // selectăm doar IsArchived (nu toată entitatea), suficient pentru decizie
                var monitored = await db.Monitoreds
                    .Where(m => m.Id == monitoredId)
                    .Select(m => new { m.IsArchived })
                    .FirstOrDefaultAsync();
                if (monitored == null || monitored.IsArchived)
                {
                    _logger.LogDebug("BuildProfile skipped for {MonitoredId} — archived or missing", monitoredId);
                    return;
                }

                // Citim TOATE măsurătorile din ultimele 7 zile dintr-o singură interogare (nu pe ore separat) —
                // gruparea pe ore se face în memorie mai jos (.GroupBy), mai eficient decât 24 interogări DB separate
                var cutoff      = DateTime.UtcNow - ProfileWindow;
                var measurements = await db.Measurements
                    .Where(m => m.IdMonitored == monitoredId && m.CreatedAt >= cutoff)
                    .ToListAsync();

                if (measurements.Count == 0)
                {
                    _logger.LogInformation("No measurements found for activity profile build of {MonitoredId}.", monitoredId);
                    return;
                }

                var now    = DateTime.UtcNow;
                var byHour = measurements.GroupBy(m => m.CreatedAt.Hour); // Grupăm pe ora din zi (0-23), indiferent de ZIUA calendaristică — combină toate cele 7 zile pe aceeași oră

                // Calculăm statisticile pentru fiecare oră care are cel puțin o măsurătoare în fereastra de 7 zile
                // (orele fără nicio măsurătoare nu apar deloc în byHour — rămân null în array-ul folosit la citire)
                foreach (var group in byHour)
                {
                    var list = group.ToList();
                    // Rata de mișcare = proporția măsurătorilor cu activitate non-sedentară din TOATE măsurătorile orei
                    // (agregat pe toate cele 7 zile — ex: ora 14 are ~7×6=42 măsurători la frecvența implicită)
                    double movementRate     = list.Count(m => IsMoving(m.Activity)) / (double)list.Count;
                    // Probabilitate somn = fără mișcare ȘI puls sub 75 bpm (tipic pentru repaus/somn) —
                    // dublă condiție: doar "fără mișcare" ar confunda somnul cu a sta nemișcat treaz (ex: la birou)
                    double sleepProbability = list.Count(m =>
                        !IsMoving(m.Activity) && m.Pulse > 0 && m.Pulse < 75) / (double)list.Count;
                    // Media pulsului, excluzând citirile 0 (senzor fără citire validă) — DefaultIfEmpty(0) evită
                    // o excepție InvalidOperationException de la .Average() dacă TOATE citirile orei au Pulse=0
                    double avgPulse         = list.Where(m => m.Pulse > 0).Select(m => m.Pulse).DefaultIfEmpty(0).Average();

                    // UPSERT: actualizăm profilul orei dacă există deja un rând pentru (monitoredId, oră), altfel îl creăm —
                    // vezi ActivityProfileRepository.UpsertAsync (caută după cheia compusă, apoi update sau insert)
                    await repo.UpsertAsync(new ActivityProfile
                    {
                        IdMonitored      = monitoredId,
                        HourOfDay        = group.Key,         // Ora (0-23) — cheia compusă alături de IdMonitored
                        AveragePulse     = Math.Round(avgPulse, 1),
                        MovementRate     = Math.Round(movementRate, 3),    // Ex: 0.732 = 73.2% din timp cu mișcare
                        SleepProbability = Math.Round(sleepProbability, 3),
                        DataPoints       = list.Count,          // Câte măsurători au contribuit — folosit la MinDataPoints în CheckAnomalyAsync
                        LastUpdated      = now
                    });
                }

                _profileCache.TryRemove(monitoredId, out _); // Invalidăm cache-ul în memorie — următoarea citire va reîncărca profilul proaspăt din DB
                _logger.LogInformation("Activity profile built for {MonitoredId}: {Hours} hours from {Count} measurements.",
                    monitoredId, byHour.Count(), measurements.Count);
            }
            catch (Exception ex)
            {
                // Nu re-aruncăm — un build de profil picat nu trebuie să oprească restul fluxului (alertare, ingest măsurători)
                _logger.LogError(ex, "Failed to build activity profile for {MonitoredId}.", monitoredId);
            }
            finally
            {
                // finally — garantăm eliberarea lock-ului de build INDIFERENT de rezultat (succes sau excepție),
                // altfel un build care a picat ar bloca permanent reconstruirea profilului acelui pacient
                _buildInProgress.TryRemove(monitoredId, out _);
            }
        }

        // Reconstruiește profilul pentru toate persoanele active (non-arhivate, non-șterse).
        // Apelat zilnic de ActivityProfileRebuildBackgroundService la ora 02:00 UTC.
        public async Task<int> RebuildAllActiveAsync(CancellationToken ct = default)
        {
            int rebuilt = 0;
            try
            {
                // Primul scope: doar pentru a obține lista de ID-uri — îl închidem (bloc using separat) înainte
                // de a începe bucla de build-uri, ca să nu ținem o conexiune DB deschisă inutil pe toată durata buclei
                List<Guid> ids;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
                    ids = await db.Monitoreds
                        .Where(m => !m.IsArchived && m.DeletedAt == null)   // Doar pacienți activi — nici arhivați, nici în perioada de grație a soft-delete
                        .Select(m => m.Id)
                        .ToListAsync(ct);
                }

                // Procesăm secvențial (nu în paralel) — fiecare BuildProfileAsync își creează propriul scope DI
                // și face propriile interogări; rularea secvențială evită saturarea pool-ului de conexiuni DB
                // dacă sunt mulți pacienți, cu prețul unui rebuild total mai lent (acceptabil, rulează la 02:00 UTC)
                foreach (var id in ids)
                {
                    if (ct.IsCancellationRequested) break; // Oprim imediat dacă aplicația se închide în timpul buclei
                    if (!_buildInProgress.TryAdd(id, 0)) continue; // Deja în curs de build pentru acest pacient (ex: declanșat manual) — sărim, nu suprapunem
                    await BuildProfileAsync(id);   // BuildProfileAsync elimină singur intrarea din _buildInProgress în blocul său finally
                    rebuilt++;
                }
            }
            catch (Exception ex)
            {
                // O eroare la nivelul listei de ID-uri (ex: DB indisponibilă) — logăm și returnăm orice s-a reconstruit până acum
                _logger.LogError(ex, "RebuildAllActiveAsync failed");
            }
            return rebuilt; // Returnăm câte profiluri au fost efectiv reconstruite — logat de ActivityProfileRebuildBackgroundService
        }

        // Returnează profilul complet al persoanei (24 ore) din DB
        // Folosit de ActivityProfileController pentru afișare în UI
        public async Task<List<ActivityProfile>> GetProfileAsync(Guid monitoredId)
        {
            // Citește direct din DB (nu din _profileCache) — folosit pentru afișare în UI, unde utilizatorul
            // se așteaptă la datele exacte din DB, nu la o variantă posibil cașată până la 4 ore
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IActivityProfileRepository>();
            return (await repo.GetByMonitoredIdAsync(monitoredId)).ToList();
        }

        // Helper intern: returnează profilul orar din cache sau din DB.
        // Dacă nu există profil, declanșează build fire-and-forget și returnează null.
        private async Task<ActivityProfile?[]?> GetCachedProfileAsync(Guid monitoredId, DateTime now)
        {
            // Verificăm cache-ul (valabil 4 ore) — cale rapidă, fără nicio interogare DB, pentru cazul comun
            // (CheckAnomalyAsync e apelată la fiecare măsurătoare, ~o dată la 30 secunde per pacient activ)
            if (_profileCache.TryGetValue(monitoredId, out var cached) &&
                (now - cached.CachedAt) < CacheDuration)
                return cached.Hours;

            // Cache expirat sau absent (primul apel pentru acest pacient de la pornirea aplicației) → citim din DB
            using var scope = _scopeFactory.CreateScope();
            var repo     = scope.ServiceProvider.GetRequiredService<IActivityProfileRepository>();
            var profiles = (await repo.GetByMonitoredIdAsync(monitoredId)).ToList();

            if (profiles.Count == 0)
            {
                // Niciun profil în DB încă (pacient nou, sau primul rebuild nu a rulat încă) → declanșăm
                // construcția în fundal (fire-and-forget: _ = Task.Run(...), fără await) și răspundem imediat
                // cu null, ca CheckAnomalyAsync să nu blocheze ingestia măsurătorii curente cât durează build-ul
                if (_buildInProgress.TryAdd(monitoredId, 0))
                    _ = Task.Run(() => BuildProfileAsync(monitoredId));
                return null; // Apelantul (CheckAnomalyAsync) tratează null ca "nu putem detecta anomalii încă"
            }

            // Construim un array de 24 poziții (index = ora din zi) — pozițiile pentru ore fără date istorice
            // rămân null (ActivityProfile? — nullable), nu sunt completate cu valori implicite/zero
            var arr = new ActivityProfile?[24];
            foreach (var p in profiles)
                arr[p.HourOfDay] = p;

            _profileCache[monitoredId] = (now, arr); // Salvăm în cache cu timestamp-ul curent — valabil următoarele 4 ore
            return arr;
        }
    }
}
