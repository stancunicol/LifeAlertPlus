using System.Text.Json;
using System.Text.Json.Serialization;

namespace LifeAlertPlus.API.Services
{
    // Model de intrare în logul de debug al dispozitivelor ESP32.
    // Fiecare înregistrare conține datele primite de la un dispozitiv (sau simulate)
    // împreună cu tipul evenimentului și timestamp-ul.
    // [JsonIgnore(WhenWritingNull)] → câmpurile null sunt omise din JSON pentru compactitate
    public class DeviceTestLogEntry
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty; // Ex: "ingest", "heartbeat", "panic", "simulation"

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = string.Empty; // Momentul evenimentului (ISO 8601)

        [JsonPropertyName("serial")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Serial { get; set; } // Numărul de serie al dispozitivului ESP32

        [JsonPropertyName("monitoredId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MonitoredId { get; set; } // ID-ul persoanei monitorizate (GUID ca string)

        [JsonPropertyName("pulse")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Pulse { get; set; } // Pulsul măsurat (bpm)

        [JsonPropertyName("spo2")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? SpO2 { get; set; } // Saturația oxigenului (%)

        [JsonPropertyName("temperature")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Temperature { get; set; } // Temperatura corporală (°C)

        [JsonPropertyName("coordinates")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Coordinates { get; set; } // GPS: "lat,long" de la Neo-6M

        [JsonPropertyName("activity")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Activity { get; set; } // Activitate detectată de MPU6050: "walking", "still" etc.

        [JsonPropertyName("isFall")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? IsFall { get; set; } // Detectare cădere (accelerometru)

        [JsonPropertyName("battery")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Battery { get; set; } // Nivelul bateriei (0-100%)

        [JsonPropertyName("severity")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Severity { get; set; } // Severitatea alertei: "Normal", "Alert", "Critical"
    }

    // Serviciu de logare JSON pentru debug-ul dispozitivelor ESP32.
    // ACTIVAT NUMAI dacă DeviceTestLog:Enabled = true în appsettings.
    // Scrie înregistrările într-un fișier JSON acumulativ (array de obiecte).
    // Util în dezvoltare pentru a verifica datele primite de la ESP fără a accesa DB-ul.
    public class DeviceTestLogService : IDeviceTestLogService
    {
        private readonly bool _enabled;    // Activat/dezactivat din configurație
        private readonly string _filePath; // Calea fișierului JSON de log
        private readonly object _lock = new(); // Lock pentru thread-safety (scris din mai multe thread-uri simultan)
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true }; // JSON formatat pentru lizibilitate

        public DeviceTestLogService(IConfiguration configuration)
        {
            // Citim starea activare din appsettings: DeviceTestLog:Enabled
            _enabled = bool.TryParse(configuration["DeviceTestLog:Enabled"], out var e) && e;
            // Calea fișierului: din config sau implicit în directorul executabilului
            var configured = configuration["DeviceTestLog:Path"];
            _filePath = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(AppContext.BaseDirectory, "device_test_log.json") // Implicit lângă executabil
                : configured;
        }

        public bool IsEnabled => _enabled; // Proprietate publică pentru verificare externă

        // Adaugă o intrare în fișierul JSON de log.
        // Operația este sincronizată cu lock pentru a evita coruperea fișierului la scrieri simultane.
        public void Log(DeviceTestLogEntry entry)
        {
            if (!_enabled) return; // Noop dacă dezactivat (nu apelăm lock inutil)
            lock (_lock)
            {
                // Citim array-ul existent din fișier (dacă există)
                List<DeviceTestLogEntry> entries = [];
                if (File.Exists(_filePath))
                {
                    try
                    {
                        var text = File.ReadAllText(_filePath);
                        entries = JsonSerializer.Deserialize<List<DeviceTestLogEntry>>(text) ?? [];
                    }
                    catch
                    {
                        entries = []; // Fișier corupt sau invalid → resetăm
                    }
                }
                entries.Add(entry); // Adăugăm noua intrare
                File.WriteAllText(_filePath, JsonSerializer.Serialize(entries, JsonOpts)); // Rescriem tot fișierul
            }
        }
    }
}
