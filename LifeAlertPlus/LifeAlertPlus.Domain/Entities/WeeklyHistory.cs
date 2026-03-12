namespace LifeAlertPlus.Domain.Entities
{
    public class WeeklyHistory
    {
        public Guid Id { get; set; }
        public Guid IdMonitored { get; set; }
        public float AveragePulse { get; set; }
        public float AverageTemperature { get; set; }
        public int TotalFalls { get; set; }
        public int TotalAlerts { get; set; }
        public int LeaveAddressCounter { get; set; }
        public DateTime CreatedAt { get; set; }
        
        public Monitored Monitored { get; set; } = null!;
    }
}