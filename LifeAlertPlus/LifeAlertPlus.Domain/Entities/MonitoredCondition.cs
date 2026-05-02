namespace LifeAlertPlus.Domain.Entities
{
    public class MonitoredCondition
    {
        public Guid Id { get; set; }
        public Guid IdMonitored { get; set; }
        public string ConditionKey { get; set; } = string.Empty;
        public DateTime AddedAt { get; set; }

        public Monitored Monitored { get; set; } = null!;
    }
}
