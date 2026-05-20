namespace LifeAlertPlus.Shared.DTOs.Requests.ESP
{
    public class ESPHeartbeatDTO
    {
        public string Serial { get; set; } = string.Empty;
        public int RssiDbm { get; set; }
        public int FreeHeapBytes { get; set; }
        public long UptimeSeconds { get; set; }
        public int QueuedMeasurements { get; set; }
        public float BatteryVoltage { get; set; }
        public int SensorFlags { get; set; }
        public string FirmwareVersion { get; set; } = string.Empty;
    }
}
