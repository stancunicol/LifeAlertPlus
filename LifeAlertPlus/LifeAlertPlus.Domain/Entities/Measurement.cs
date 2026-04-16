namespace LifeAlertPlus.Domain.Entities
{
    public class Measurement
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Activity { get; set; } = string.Empty;
        public bool IsFall { get; set; }
        public Guid IdMonitored { get; set; }
        public double Pulse { get; set; }
        public double Temperature { get; set; }
        public double SpO2 { get; set; }
        public string Coordinates { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        
        public Monitored Monitored { get; set; } = null!;
    }
}
