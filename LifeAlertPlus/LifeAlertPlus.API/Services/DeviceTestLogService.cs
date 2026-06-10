using System.Text.Json;
using System.Text.Json.Serialization;

namespace LifeAlertPlus.API.Services
{
    public class DeviceTestLogEntry
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = string.Empty;

        [JsonPropertyName("serial")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Serial { get; set; }

        [JsonPropertyName("monitoredId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MonitoredId { get; set; }

        [JsonPropertyName("pulse")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Pulse { get; set; }

        [JsonPropertyName("spo2")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? SpO2 { get; set; }

        [JsonPropertyName("temperature")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Temperature { get; set; }

        [JsonPropertyName("coordinates")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Coordinates { get; set; }

        [JsonPropertyName("activity")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Activity { get; set; }

        [JsonPropertyName("isFall")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? IsFall { get; set; }

        [JsonPropertyName("battery")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Battery { get; set; }

        [JsonPropertyName("severity")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Severity { get; set; }
    }

    public class DeviceTestLogService
    {
        private readonly bool _enabled;
        private readonly string _filePath;
        private readonly object _lock = new();
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public DeviceTestLogService(IConfiguration configuration)
        {
            _enabled = bool.TryParse(configuration["DeviceTestLog:Enabled"], out var e) && e;
            var configured = configuration["DeviceTestLog:Path"];
            _filePath = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(AppContext.BaseDirectory, "device_test_log.json")
                : configured;
        }

        public bool IsEnabled => _enabled;

        public void Log(DeviceTestLogEntry entry)
        {
            if (!_enabled) return;
            lock (_lock)
            {
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
                        entries = [];
                    }
                }
                entries.Add(entry);
                File.WriteAllText(_filePath, JsonSerializer.Serialize(entries, JsonOpts));
            }
        }
    }
}
