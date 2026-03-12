namespace LifeAlertPlus.Domain.Entities
{
    public class Measurement
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Guid IdMonitored { get; set; }
        public bool FallDetection { get; set; }
        public double Pulse { get; set; }
        public double Temperature { get; set; }
        public double Longitude { get; set; }
        public double Latitude { get; set; }
        public DateTime CreatedAt { get; set; }
        
        public Monitored Monitored { get; set; } = null!;
    }
}
