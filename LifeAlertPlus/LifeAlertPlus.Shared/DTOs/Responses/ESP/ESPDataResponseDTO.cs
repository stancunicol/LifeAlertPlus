namespace LifeAlertPlus.Shared.DTOs.Responses.ESP
{
    public class ESPDataResponseDTO
    {
        public string Serial { get; set; } = string.Empty;
        public long Date { get; set; }
        public bool IsAvailable { get; set; } = true;
        public bool IsFall { get; set; } = false;
        public string? ErrorMessage { get; set; }
        public List<int> Mpu6050 { get; set; } = new List<int>();
        public List<int> Gyro { get; set; } = new List<int>();
        public List<int>? Max30100 { get; set; }
        public int? Bpm { get; set; }
        public int? Spo2 { get; set; }
        public string? Neo6m { get; set; }
        public double? Temperature { get; set; }
        public double? Battery { get; set; }

        // Firmware-computed activity label for the last measurement window
        // ("moving" / "stationary"). Null when MPU was unavailable or sample
        // count too low to classify confidently.
        public string? Activity { get; set; }

        // Device diagnostics from the latest heartbeat packet (null = no heartbeat yet).
        public int? RssiDbm       { get; set; }
        public int? FreeHeapBytes { get; set; }
        public int? UptimeSeconds { get; set; }
        public int? HeartbeatAge  { get; set; }
    }
}