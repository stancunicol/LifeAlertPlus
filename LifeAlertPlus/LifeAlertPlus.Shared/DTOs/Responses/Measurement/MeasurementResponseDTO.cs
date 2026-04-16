namespace LifeAlertPlus.Shared.DTOs.Responses.Measurement
{
    public class MeasurementResponseDTO
    {
        public string Name { get; set; } = string.Empty;
        public string Activity { get; set; } = string.Empty;
        public bool IsFall { get; set; }
        public Guid IdMonitored { get; set; }
        public double Pulse { get; set; }
        public double Temperature { get; set; }
        public double SpO2 { get; set; }
        public string Coordinates { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}