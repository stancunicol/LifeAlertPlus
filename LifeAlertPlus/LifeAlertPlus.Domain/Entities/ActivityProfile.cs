namespace LifeAlertPlus.Domain.Entities
{
    public class ActivityProfile
    {
        public Guid IdMonitored { get; set; }
        public int HourOfDay { get; set; }       // 0-23
        public double AveragePulse { get; set; }
        public double MovementRate { get; set; }     // 0.0-1.0, ratio of measurements with movement
        public double SleepProbability { get; set; } // 0.0-1.0, ratio of measurements indicating sleep
        public int DataPoints { get; set; }
        public DateTime LastUpdated { get; set; }

        public Monitored Monitored { get; set; } = null!;
    }
}
