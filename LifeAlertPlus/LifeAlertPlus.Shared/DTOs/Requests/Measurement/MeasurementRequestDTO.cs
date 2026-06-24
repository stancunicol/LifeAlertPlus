namespace LifeAlertPlus.Shared.DTOs.Requests.Measurement
{
    // Server: primit de MeasurementController.AddMeasurement (POST /api/measurement) — folosit pentru
    // inserare manuală/testare a unei măsurători (diferit de ESPDataResponseDTO, care e payload-ul
    // brut trimis direct de firmware prin ESPController.IngestESPData).
    // Client: construit și trimis de MeasurementApiClient.AddMeasurementAsync, apelat din
    // SimulationPage.razor.cs (linia ~214) când se simulează manual o măsurătoare în UI.
    public class MeasurementRequestDTO
    {
        public string Name { get; set; } = string.Empty;
        public string Activity { get; set; } = string.Empty;
        public bool IsFall { get; set; }
        public Guid IdMonitored { get; set; }
        public double Pulse { get; set; }
        public double Temperature { get; set; }
        public double SpO2 { get; set; }
        public string Coordinates { get; set; } = string.Empty;
    }
}