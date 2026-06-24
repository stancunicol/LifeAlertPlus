namespace LifeAlertPlus.Shared.DTOs.Responses.ESP
{
    // DTO cu rol dublu (numele "Response" e puțin înșelător) — folosit ca:
    //   1) REQUEST: payload-ul brut trimis de firmware (build_json() din main.cpp) către
    //      ESPController.IngestESPData (POST /api/ESP/ingest) și ESPController.Simulate.
    //   2) RESPONSE: ultima măsurătoare "în direct" a unui dispozitiv, cache-uită în memorie
    //      (SimulationManager) și citită de Client prin MonitoredApiClient.GetEspDataAsync —
    //      consumat aproape în toate paginile cu date live (Dashboard, MonitoredPage,
    //      MonitoredUsersPage, SelectedMonitored, ViewSelectedMonitored) prin polling periodic.
    // Generat și pentru simulare/testare de ESPDataGenerator (Helpers), fără hardware real.
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