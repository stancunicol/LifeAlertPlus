namespace LifeAlertPlus.Shared.DTOs.Requests.ESP
{
    // Payload-ul trimis de firmware-ul ESP32 (heartbeat_send() din main.cpp) la fiecare 5 minute.
    // Server: primit de ESPController.Heartbeat (POST /api/ESP/heartbeat, autentificat prin header X-Device-Key) —
    // folosit pentru diagnosticare dispozitiv (vezi ESPControllerTests.cs: Heartbeat_Returns200_AndStoresHeartbeat_WhenValid).
    // Nu există un caller direct din Client — datele sunt produse exclusiv de dispozitivul fizic.
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
