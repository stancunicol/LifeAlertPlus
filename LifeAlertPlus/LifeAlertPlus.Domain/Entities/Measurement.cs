using System;

namespace LifeAlertPlus.Domain.Entities
{
    public class Measurement
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid IdMonitored { get; set; }
        public Monitored Monitored { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool FallDetection { get; set; }
        public float Pulse { get; set; }
        public float Temperature { get; set; }
        public float Longitude { get; set; }
        public float Latitude { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
