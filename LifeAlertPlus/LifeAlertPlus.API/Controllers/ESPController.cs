using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using LifeAlertPlus.Shared.DTOs.Requests.ESP;
using Microsoft.AspNetCore.Authorization;
using LifeAlertPlus.Application.IServices;
using System.Security.Cryptography;
using System.Text;

namespace LifeAlertPlus.API.Controllers
{
    // Controller pentru dispozitivele ESP32 (IoT wearable) — punctul unde firmware-ul (main.cpp) și
    // clientul Blazor se întâlnesc cu backend-ul .NET.
    //
    // DOUĂ MODELE DE AUTENTIFICARE DIFERITE COEXISTĂ ÎN ACEST CONTROLLER:
    //   1. JWT (Bearer token) — pentru endpoint-urile apelate de utilizatori prin Blazor (ex: GetESPData).
    //      Acestea NU au [AllowAnonymous], deci [Authorize] de la nivel de clasă se aplică implicit.
    //   2. Header X-Device-Key (HMAC-SHA256) — pentru endpoint-urile apelate DIRECT de firmware-ul ESP32
    //      (ingest, panic, wifi-config, heartbeat). Dispozitivul nu poate gestiona login/parolă/JWT,
    //      deci aceste rute sunt [AllowAnonymous] din perspectiva ASP.NET, dar validate manual prin
    //      ValidateDeviceKey() — vezi mai jos mecanismul exact.
    //
    // FLUXUL DE DATE: ESP32 trimite măsurători → IngestESPData le persistă în DB + le pune în cache-ul
    // în memorie (SimulationManager) → AlertMonitorService evaluează asincron dacă declanșează o alertă →
    // Clientul Blazor citește periodic cache-ul prin GetESPData (date "live", nu istoricul din DB).
    [ApiController]
    [Authorize] // Implicit: toate endpoint-urile necesită JWT (excepțiile sunt marcate cu [AllowAnonymous])
    [Route("api/[controller]")] // URL de bază: /api/esp
    public class ESPController(
        IConfiguration configuration,                         // Citește Urls:EspDeviceSecret pentru validarea HMAC
        ILogger<ESPController> logger,
        Services.ISimulationManager simulationManager,        // Cache în memorie (NU DB) — ultimul pachet + heartbeat per serial, folosit pentru "date live" și pentru modul simulare/testare
        Services.IAlertMonitorService alertMonitorService,    // Evaluează măsurătorile pentru alertare (praguri, reguli medicale) + rate limiting + verificare baterie
        IMonitoredService monitoredService,                   // Găsește persoana monitorizată după serialul dispozitivului
        IUserMonitoredService userMonitoredService,            // Verifică drepturile de acces (care utilizator vede ce dispozitiv)
        IMeasurementService measurementService,                // Persistă măsurătorile reale în baza de date
        IWifiNetworkService wifiNetworkService,                 // Citește rețelele WiFi salvate, trimise dispozitivului la pornire
        Services.IDeviceTestLogService deviceTestLogService) : BaseApiController   // Jurnal vizibil în panoul Admin, pentru depanare hardware
    {
        // GET /api/esp/data/{serial} — Returnează ultimele date primite de la un dispozitiv ESP
        // Apelat de clientul Blazor (polling periodic, ex. DashboardPage/MonitoredPage) pentru a afișa
        // datele vitale "în direct". IMPORTANT: NU citește din baza de date — întoarce mereu ultimul
        // pachet din cache-ul în memorie al SimulationManager-ului. Istoricul real (pentru grafice,
        // export PDF) vine din MeasurementController, nu de aici.
        [HttpGet("data/{serial}")]
        public async Task<IActionResult> GetESPData(string serial)
        {
            // Acest endpoint cere JWT (nu e [AllowAnonymous]) — apelantul e un utilizator din Blazor, nu dispozitivul
            var callerId = GetCallerId();
            if (callerId == null)
                return Unauthorized();

            // Găsim persoana monitorizată asociată acestui serial de dispozitiv — serialul e cheia
            // care leagă "ce dispozitiv fizic" de "ce pacient din DB" (vezi Monitored.DeviceSerialNumber)
            var monitored = await monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(serial);
            if (monitored == null)
                return NotFound(new { Message = "Device not found." });

            // Verificare de autorizare: utilizatorii non-admin pot vedea doar datele persoanelor pe care
            // le monitorizează efectiv (legătură UserMonitored) — altfel oricine ar putea citi datele
            // vitale ale oricărui pacient doar ghicind/cunoscând seria dispozitivului
            if (!IsAdminRole())
            {
                var owned = await userMonitoredService.GetMonitoredPeopleByUserIdAsync(callerId.Value);
                if (!owned.Any(m => m.Id == monitored.Id))
                    return Forbid(); // 403 Forbidden — nu ai acces la acest dispozitiv
            }

            // Obținem datele din cache-ul în memorie (populat de IngestESPData/Simulate la fiecare pachet primit).
            // Cache-ul e per-instanță de aplicație, nu e partajat între eventuale replici/scalare orizontală.
            var data = simulationManager.GetData(serial);
            if (data != null)
            {
                data.IsAvailable = true; // Dispozitivul e activ — am găsit un pachet în cache
                data.ErrorMessage = null;
            }
            else
            {
                // Nu există nimic în cache — fie dispozitivul nu a trimis NICIODATĂ date (de la ultimul restart
                // al serverului, cache-ul fiind volatil), fie serialul e corect dar dispozitivul e off
                data = CreateUnavailableResponse(serial, "No data yet — waiting for device.");
            }

            // Attach latest heartbeat diagnostics (battery, RSSI, uptime) if available.
            // Adăugăm diagnosticele tehnice ale dispozitivului (baterie, semnal WiFi, uptime) — heartbeat-ul
            // e trimis separat de datele vitale (la 5 minute, vezi Heartbeat() mai jos), deci e citit
            // dintr-un slot diferit al cache-ului (SimulationManager.GetHeartbeat, nu GetData)
            var hb = simulationManager.GetHeartbeat(serial.Trim());
            if (hb.HasValue)
            {
                data.RssiDbm        = hb.Value.Data.RssiDbm; // Puterea semnalului WiFi (dBm) — valori tipice: -50 (foarte bun) până la -90 (foarte slab)
                data.FreeHeapBytes  = hb.Value.Data.FreeHeapBytes; // Memorie liberă pe ESP32 — utilă pentru a detecta memory leak-uri în firmware
                data.UptimeSeconds  = (int)Math.Min(hb.Value.Data.UptimeSeconds, int.MaxValue); // Timp de funcționare de la ultima pornire — Math.Min previne overflow dacă valoarea brută (long/uint64) e mai mare decât int.MaxValue
                data.HeartbeatAge   = (int)(DateTime.UtcNow - hb.Value.ReceivedAt).TotalSeconds; // Cât timp a trecut de la ultimul heartbeat — folosit de UI ca să decidă dacă dispozitivul e considerat "offline" (heartbeat prea vechi)
            }

            return Ok(data);
        }

        // Validează că cererea vine de la un dispozitiv ESP legitim folosind HMAC-SHA256, NU JWT —
        // dispozitivul nu are cont/parolă, deci nu poate obține un Bearer token clasic.
        // Mecanism (identic cu cel calculat de firmware la boot, vezi main.cpp app_main()):
        //   cheie_dispozitiv = HMAC-SHA256(secret=Urls:EspDeviceSecret, mesaj=serial_dispozitiv)
        // Fiecare dispozitiv obține o cheie DIFERITĂ (derivată din serialul lui unic, gravat în eFuse la
        // fabricație) — compromiterea unui dispozitiv nu expune cheia altor dispozitive, pentru că
        // secretul rămâne necunoscut atacatorului (doar cheia derivată e vizibilă, nu e inversabilă).
        private bool ValidateDeviceKey(string serial)
        {
            var secret = configuration["Urls:EspDeviceSecret"]; // Secretul partajat (configurat identic în appsettings API și în Kconfig firmware)
            if (string.IsNullOrWhiteSpace(secret)) return false; // Secret neconfigurat → respingem tot (fail-safe, nu fail-open)
            var provided = Request.Headers["X-Device-Key"].ToString(); // Cheia trimisă de ESP în fiecare cerere
            if (string.IsNullOrWhiteSpace(provided)) return false;
            // Recalculăm HMAC-SHA256 al serialului primit, folosind secretul nostru — dacă cererea e
            // legitimă, rezultatul trebuie să fie identic cu ce a calculat firmware-ul la boot
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var expected = Convert.ToHexString(
                hmac.ComputeHash(Encoding.UTF8.GetBytes(serial))
            ).ToLowerInvariant(); // Hex lowercase — trebuie să corespundă exact formatului produs de firmware (snprintf %02x)
            // Comparație în timp constant (FixedTimeEquals) — previne timing attacks: o comparație
            // normală (==) ar opri la primul byte diferit, iar timpul de execuție ar "scurge" informație
            // despre câți bytes sunt corecți; FixedTimeEquals compară mereu toți byte-ii, indiferent de rezultat
            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            var providedBytes = Encoding.UTF8.GetBytes(provided);
            return expectedBytes.Length == providedBytes.Length
                && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
        }

        // POST /api/esp/ingest — Primește date vitale de la un dispozitiv ESP32
        // Apelat de firmware-ul ESP la fiecare interval de măsurare (implicit 30s, configurabil per
        // pacient prin Monitored.UpdateFrequency — vezi GetWifiConfig mai jos, care trimite intervalul).
        // Acesta e endpoint-ul cu cel mai mare volum de trafic din toată aplicația (un apel per dispozitiv
        // per interval, non-stop, cât timp dispozitivul e pornit).
        [HttpPost("ingest")]
        [AllowAnonymous] // ESP-ul nu are JWT — autentificat prin X-Device-Key HMAC (ValidateDeviceKey)
        public async Task<IActionResult> IngestESPData([FromBody] ESPDataResponseDTO payload)
        {
            if (string.IsNullOrWhiteSpace(payload?.Serial))
                return BadRequest(new { Message = "Serial is required." });

            payload.Serial = payload.Serial.Trim();

            // Verificăm că cererea vine de la un ESP autentic (nu de la un atacator care trimite date false)
            if (!ValidateDeviceKey(payload.Serial))
                return Unauthorized(new { Message = "Invalid device key." });

            // Rate limit: max 4 ingest calls per 60 s per serial to prevent data flooding.
            // Protecție împotriva firmware-ului buggy (bucla de trimitere blocată într-un ciclu rapid)
            // sau a unui atacator care a obținut cheia unui dispozitiv și încearcă să inunde baza de date
            if (!alertMonitorService.IsIngestAllowed(payload.Serial))
            {
                logger.LogWarning("Ingest rate limit exceeded for serial {Serial}", payload.Serial);
                return StatusCode(429, new { Message = "Rate limit exceeded. Please slow down." });
            }

            // ESP trimite uptime-ul propriu în milisecunde (nu un Unix timestamp), deci orice valoare
            // sub 1 miliard e clar uptime, nu un timestamp real. Înlocuim cu ora serverului ca
            // IsEspDataFresh() din client să funcționeze corect (compară cu DateTimeOffset.UtcNow).
            if (payload.Date < 1_000_000_000L) payload.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            payload.IsAvailable = true;
            payload.ErrorMessage = null;

            // Stocăm datele în cache-ul în memorie pentru acces rapid din Blazor (citit de GetESPData) —
            // ATENȚIE: asta se face ÎNAINTE de a verifica dacă serialul e legat la un pacient, deci
            // chiar și un dispozitiv "orfan" (nealocat) apare în cache, util la depanare/provisioning
            simulationManager.SetData(payload);

            // Găsim persoana monitorizată asociată acestui serial
            var monitored = await monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(payload.Serial);
            if (monitored == null)
            {
                // Dispozitiv necunoscut (nelinkat la nicio persoană) — acceptăm datele (răspuns 200, ca
                // firmware-ul să nu reîncerce inutil) dar NU le persistăm în DB — nu există un IdMonitored
                // valid pentru coloana obligatorie Measurement.IdMonitored
                logger.LogDebug("ESP data ingested from {Serial} (no monitored linked — measurement not persisted)", payload.Serial);
                return Ok();
            }

            // Nu procesăm date pentru persoane arhivate sau marcate ca șterse — monitorizarea e considerată
            // oprită, deci nu mai are sens să acumulăm măsurători (ar fi șterse de RetentionCleanup în plus)
            if (monitored.IsArchived || monitored.DeletedAt != null)
            {
                logger.LogInformation("ESP data from {Serial} ignored — monitored {MonitoredId} is archived or pending deletion", payload.Serial, monitored.Id);
                return Ok(new { Message = "Monitored person is archived or pending deletion. Data not persisted." });
            }

            // Normalize: firmware may send only Max30100 without Bpm/Spo2 fields
            // Unele versiuni de firmware (sau modul de simulare) trimit puls/SpO2 ca array brut
            // Max30100=[puls, spo2] în loc de câmpuri separate Bpm/Spo2 — normalizăm la un singur format,
            // cu Bpm/Spo2 explicite având prioritate (??) dacă ambele sunt prezente
            int pulse = payload.Bpm
                ?? (payload.Max30100?.Count >= 1 ? payload.Max30100[0] : 0);
            int spo2 = payload.Spo2
                ?? (payload.Max30100?.Count >= 2 ? payload.Max30100[1] : 0);
            double temperature = payload.Temperature ?? 0;

            // Backfill Bpm/Spo2 so the in-memory cache stays consistent
            // Completăm câmpurile lipsă pe OBIECTUL DEJA STOCAT în cache (SetData de mai sus a salvat
            // o referință la `payload`) — ??= scrie doar dacă era null, deci nu suprascrie valori reale
            payload.Bpm  ??= pulse;
            payload.Spo2 ??= spo2;
            string coordinates = payload.Neo6m ?? string.Empty; // Coordonate GPS brute de la modulul Neo-6M (format "lat,lon")
            bool isFall = payload.IsFall; // Flag de cădere — setat de fall_task din firmware (state machine free-fall/impact/stillness)
            // Firmware classifies movement over the last ~5s window from the same MPU
            // stream the fall detector uses (50 Hz). Persist the label so the behavioral
            // profile can compute movement rate / sleep probability per hour over 7 days.
            // "moving" / "stationary" — vezi g_movement din main.cpp; salvată ca să alimenteze
            // ActivityProfileService.BuildProfileAsync (IsMoving() citește exact acest câmp)
            string activity = string.IsNullOrWhiteSpace(payload.Activity)
                ? string.Empty
                : payload.Activity.Trim(); // Ex: "stationary", "walking", "running"

            // Construim entitatea de măsurătoare și o salvăm permanent în baza de date —
            // aceasta e sursa de adevăr pentru istoric, grafice, export PDF (diferit de cache-ul volatil de mai sus)
            var measurement = new Domain.Entities.Measurement
            {
                Id = Guid.NewGuid(),
                Name = "ESP Device", // Sursă: dispozitiv real (nu simulare — vezi Simulate() mai jos, care folosește alt Name)
                Activity = activity,
                IsFall = isFall,
                IdMonitored = monitored.Id,
                Pulse = pulse,
                SpO2 = spo2,
                Temperature = temperature,
                Coordinates = coordinates,
                CreatedAt = DateTime.UtcNow   // Timpul serverului la salvare, NU payload.Date (acela e doar pentru afișare/diagnosticare în cache)
            };
            await measurementService.AddMeasurementAsync(measurement);

            // Logăm pachetul primit în jurnalul de teste pentru dispozitive (vizibil în admin) — util la
            // depanare hardware în teren, independent de Measurement (acela nu se salvează pt. dispozitive nealocate)
            deviceTestLogService.Log(new Services.DeviceTestLogEntry
            {
                Type        = isFall ? "fall" : "measurement",   // Tipul "fall" e evidențiat separat în UI-ul de admin
                Timestamp   = DateTime.UtcNow.ToString("O"), // Format ISO 8601 ("round-trip") — parsabil neambiguu indiferent de cultura/regiunea serverului
                Serial      = payload.Serial,
                Pulse       = pulse,
                SpO2        = spo2,
                Temperature = temperature,
                Coordinates = string.IsNullOrWhiteSpace(coordinates) ? null : coordinates,   // null explicit (nu string gol) — mai clar în log-uri JSON
                Activity    = string.IsNullOrWhiteSpace(activity) ? null : activity,
                IsFall      = isFall ? true : null,    // null când nu e cădere (nu "false") — accentuează vizual doar evenimentele reale de cădere în log
                Battery     = payload.Battery
            });

            // Procesăm măsurătoarea în background pentru evaluarea alertelor (praguri, reguli medicale,
            // anomalii comportamentale, notificări push/email/SMS) — operație potențial costisitoare
            // (interogări DB, eventual apel către microserviciul AI). Fire-and-forget: NU blocăm răspunsul
            // HTTP către ESP32 cât durează evaluarea — firmware-ul are un timeout HTTP de 15s (vezi main.cpp)
            // și nu trebuie să aștepte logica de alertare ca să considere ingestul reușit.
            _ = Task.Run(async () =>
            {
                try
                {
                    await alertMonitorService.ProcessMeasurementAsync(
                        monitored.Id, pulse, temperature, spo2, isFall: isFall,
                        activity: activity,
                        coordinates: coordinates);
                }
                catch (Exception ex)
                {
                    // Excepția e prinsă AICI, nu se propagă — fără acest try/catch, o eroare în task-ul
                    // de fundal ar deveni o "unobserved exception", pierdută silențios
                    logger.LogError(ex, "ProcessMeasurementAsync failed for serial {Serial} (monitored {MonitoredId})",
                        payload.Serial, monitored.Id);
                }
            });

            if (isFall)
                logger.LogWarning("ESP {Serial} reported FALL — triggering critical alert flow", payload.Serial);
            else
                logger.LogDebug("ESP data ingested from {Serial}: pulse={Pulse} temp={Temp}", payload.Serial, pulse, temperature);

            // Battery low check — fires a push notification when below threshold.
            // Verificăm bateria dacă ESP-ul a trimis nivelul (nu toate firmware-urile/variantele hardware
            // au senzor de baterie — payload.Battery e nullable). La fel, fire-and-forget (`_ =`, fără await) —
            // verificarea bateriei nu trebuie să întârzie răspunsul HTTP
            if (payload.Battery.HasValue)
                _ = alertMonitorService.CheckBatteryAsync(monitored.Id, payload.Serial, payload.Battery.Value);

            return Ok();
        }

        // POST /api/esp/panic — Butonul fizic de panică de pe dispozitivul ESP (Buton 1, GPIO3 în firmware)
        // Apelat când utilizatorul apasă butonul dedicat de urgență de pe brățară — spre diferență de
        // IngestESPData (rulează la fiecare interval normal), acesta e declanșat DOAR la eveniment,
        // ocazional, dar trebuie procesat cu prioritate maximă (de aceea AWAIT-ăm direct alerta, nu fire-and-forget)
        [HttpPost("panic")]
        [AllowAnonymous] // ESP-ul nu are JWT — autentificat prin X-Device-Key, la fel ca IngestESPData
        public async Task<IActionResult> PanicAlert([FromBody] ESPPanicDTO payload)
        {
            if (string.IsNullOrWhiteSpace(payload?.Serial))
                return BadRequest(new { Message = "Serial is required." });

            // Verificăm autenticitatea dispozitivului — aceeași validare HMAC ca la ingest
            if (!ValidateDeviceKey(payload.Serial.Trim()))
                return Unauthorized(new { Message = "Invalid device key." });

            var monitored = await monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(payload.Serial.Trim());
            if (monitored == null)
                return NotFound(new { Message = "Device not found." });   // Diferit de IngestESPData: aici un dispozitiv necunoscut e o eroare reală (404), nu un caz tolerat (200)

            // Nu trimitem alerte pentru persoane arhivate sau șterse — chiar dacă butonul fizic a fost apăsat,
            // monitorizarea acelei persoane a fost oprită intenționat
            if (monitored.IsArchived || monitored.DeletedAt != null)
            {
                logger.LogInformation("Panic alert from {Serial} ignored — monitored {MonitoredId} is archived or pending deletion", payload.Serial, monitored.Id);
                return Ok(new { Message = "Monitored person is archived or pending deletion. Panic alert not triggered." });
            }

            // Declanșăm alerta de panică SINCRON (await, nu fire-and-forget) — trimite notificări
            // push + email + SMS pentru toți utilizatorii care monitorizează această persoană.
            // payload.Coordinates poate fi null dacă GPS-ul nu avea fix la momentul apăsării (vezi main.cpp panic_send)
            await alertMonitorService.TriggerPanicAlertAsync(monitored.Id, payload.Coordinates);
            logger.LogWarning("Panic alert triggered by device {Serial}", payload.Serial);

            // Logăm evenimentul de panică în jurnalul de teste (separat de log-ul de măsurători normale)
            deviceTestLogService.Log(new Services.DeviceTestLogEntry
            {
                Type        = "panic",
                Timestamp   = DateTime.UtcNow.ToString("O"),
                Serial      = payload.Serial,
                Coordinates = string.IsNullOrWhiteSpace(payload.Coordinates) ? null : payload.Coordinates
            });

            return Ok();
        }

        // GET /api/esp/wifi-config/{serial} — Returnează configurația WiFi pentru dispozitiv
        // ESP-ul apelează acest endpoint la pornire (wifi_setup() din main.cpp) pentru a afla la ce
        // rețele WiFi să se conecteze și la ce interval să trimită date — practic, configurația
        // remote a dispozitivului, gestionată din UI-ul web, nu hardcodată în firmware.
        [HttpGet("wifi-config/{serial}")]
        [AllowAnonymous] // ESP-ul nu are JWT — validat prin X-Device-Key
        public async Task<IActionResult> GetWifiConfig(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial))
                return BadRequest(new { Message = "Serial is required." });

            var trimmedSerial = serial.Trim();

            if (!ValidateDeviceKey(trimmedSerial))
                return Unauthorized(new { Message = "Invalid device key." });

            // Citim rețelele WiFi salvate pentru acest dispozitiv din DB
            var networks = await wifiNetworkService.GetByDeviceSerialAsync(trimmedSerial);
            // Returnăm doar SSID și parolă (fără ID-uri interne sau alte date din entitate) — spre diferență
            // de WifiController (folosit de utilizatori prin Blazor), care exclude parola din răspuns
            // (WifiNetworkResponseDTO), AICI parola TREBUIE trimisă în clar, pentru că dispozitivul fizic
            // are nevoie de ea ca să se conecteze efectiv la rețea. NOTĂ: parola e stocată ca text simplu
            // în DB (fără criptare — vezi WifiNetworkRepository), deci nu există un pas de decriptare aici.
            var payload = networks
                .Select(n => new { ssid = n.Ssid, password = n.Password })
                .ToList();

            // Obținem frecvența de actualizare configurată pentru această persoană — permite ajustarea
            // per-pacient a cât de des trimite ESP32 măsurători (ex: 15s pentru cazuri critice, 60s pentru cazuri stabile)
            var monitored = await monitoredService.GetMonitoredPersonByDeviceSerialNumberAsync(trimmedSerial);
            const int defaultFrequencySeconds = 30; // Implicit: trimite date la fiecare 30 secunde, dacă pacientul nu are valoare proprie
            var updateFrequencySeconds = (monitored?.UpdateFrequency ?? 0) > 0
                ? monitored!.UpdateFrequency!.Value
                : defaultFrequencySeconds;

            // Returnăm rețelele WiFi și intervalul de trimitere date — convertit în MILISECUNDE pentru ESP
            // (firmware-ul lucrează cu vTaskDelay(pdMS_TO_TICKS(ms)), nu cu secunde)
            return Ok(new { networks = payload, updateIntervalMs = updateFrequencySeconds * 1000 });
        }

        // POST /api/esp/heartbeat — Semnal periodic al dispozitivului (diagnostice tehnice)
        // ESP-ul trimite heartbeat mai rar decât datele vitale (la fiecare HEARTBEAT_MS = 5 minute,
        // vezi main.cpp) — separat de ingest, pentru că diagnosticele hardware (baterie, RSSI) nu au
        // nevoie de aceeași frecvență ca semnele vitale. Conține și QueuedMeasurements — câte măsurători
        // sunt în coada offline a dispozitivului (vezi coada NVS din firmware), util pentru a detecta
        // dispozitive care au fost mult timp fără conexiune.
        [HttpPost("heartbeat")]
        [AllowAnonymous] // Dispozitivul nu are JWT — validat prin X-Device-Key, ca toate endpoint-urile ESP
        public IActionResult Heartbeat([FromBody] ESPHeartbeatDTO payload)
        {
            if (string.IsNullOrWhiteSpace(payload?.Serial))
                return BadRequest(new { Message = "Serial is required." });

            if (!ValidateDeviceKey(payload.Serial.Trim()))
                return Unauthorized(new { Message = "Invalid device key." });

            // Stocăm heartbeat-ul în memoria SimulationManager-ului cu timestamp-ul primirii (ReceivedAt) —
            // GetESPData calculează mai târziu HeartbeatAge din acest timestamp, ca să arate UI-ului
            // "cât de proaspăt" e ultimul heartbeat (nu doar conținutul lui brut)
            simulationManager.SetHeartbeat(payload.Serial.Trim(), payload);
            logger.LogDebug("Heartbeat from {Serial}: RSSI={Rssi} heap={Heap} uptime={Uptime}s queue={Queue}",
                payload.Serial, payload.RssiDbm, payload.FreeHeapBytes, payload.UptimeSeconds, payload.QueuedMeasurements);
            return Ok();   // Nu e nevoie de logică suplimentară — e doar diagnosticare, nu declanșează alerte
        }

        // DELETE /api/esp/simulate/{serial} — Șterge datele simulate din memorie (doar Admin)
        // Folosit din panoul de administrare pentru a reseta starea unui dispozitiv simulat fără să
        // aștepte ca un pachet real "offline" să-l suprascrie
        [HttpDelete("simulate/{serial}")]
        [Authorize(Roles = "Admin")] // Doar administratorii pot șterge date simulate — spre diferență de toate endpoint-urile de mai sus, AICI se cere JWT cu rol Admin, nu X-Device-Key
        public IActionResult ClearSimulatedData(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial))
                return BadRequest(new { Message = "Serial is required." });

            simulationManager.ClearData(serial.Trim()); // Eliminăm din dicționarul în memorie — același cache folosit de IngestESPData/GetESPData
            logger.LogInformation("Simulated data cleared for serial {Serial}", serial);
            return Ok(new { Message = "Simulated data cleared." });
        }

        // POST /api/esp/simulate — Injectează manual date simulate (doar Admin)
        // Util pentru testare UI fără dispozitiv fizic — scrie în EXACT același cache (SimulationManager)
        // ca IngestESPData, deci din perspectiva GetESPData/Client, datele simulate sunt indistinguibile
        // de cele reale. NU persistă în DB (nu apelează measurementService.AddMeasurementAsync ca IngestESPData) —
        // e doar pentru cache-ul "live", nu pentru a popula istoricul.
        [HttpPost("simulate")]
        [Authorize(Roles = "Admin")]   // Necesită JWT + rol Admin (nu X-Device-Key) — un admin autentificat din UI declanșează simularea, nu un dispozitiv
        public IActionResult Simulate([FromBody] ESPDataResponseDTO payload)
        {
            if (string.IsNullOrWhiteSpace(payload.Serial))
                return BadRequest(new { Message = "Serial is required." });

            payload.Serial = payload.Serial.Trim();
            if (payload.Date == 0) payload.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); // Timestamp curent dacă lipsește
            payload.IsAvailable = true;
            payload.ErrorMessage = null;

            // Stocăm datele simulate — vor fi returnate de GET /data/{serial}, la fel ca pentru un dispozitiv real
            simulationManager.SetData(payload);
            logger.LogInformation("Simulated ESP data stored for serial {Serial}", payload.Serial);

            return Ok(payload); // Returnăm datele stocate pentru confirmare (util la depanare din UI-ul de simulare)
        }

        // Helper privat: construiește un răspuns standard pentru dispozitive indisponibile/necunoscute —
        // reutilizat de GetESPData, ca formatul răspunsului să fie identic indiferent de motivul indisponibilității
        private static ESPDataResponseDTO CreateUnavailableResponse(string serial, string message) =>
            new()
            {
                Serial       = serial,
                Date         = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), // Timestamp curent — nu există un timestamp real de la dispozitiv
                IsAvailable  = false, // Marcăm dispozitivul ca offline — UI-ul Blazor citește acest flag ca să arate "Offline" în loc de valori vitale
                ErrorMessage = message, // Mesaj afișat direct în UI (diferă în funcție de contextul apelant — vezi CreateUnavailableResponse callers)
                Mpu6050      = [], // Liste goale (nu null) pentru compatibilitate cu serializer-ul JSON / deserializarea din Client (evită NullReferenceException pe .Count)
                Gyro         = [],
            };
    }
}
