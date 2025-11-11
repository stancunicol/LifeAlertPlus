namespace LifeAlertPlus.Domain.Entities
{
    public class DailyHistory
    {
        public Guid Id { get; set; }
        public Guid IdMonitored { get; set; }
        public Monitored Monitored { get; set; }
        public DateTime Day { get; set; }
        public float MinPulse { get; set; }
        public float MaxPulse { get; set; }
        public float MinTemperature { get; set; }
        public float MaxTemperature { get; set; }
        public int Falls { get; set; }
        public int Allerts { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
