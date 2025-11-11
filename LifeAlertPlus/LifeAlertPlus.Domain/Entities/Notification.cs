namespace LifeAlertPlus.Domain.Entities
{
    public class Notification
    {
        public Guid Id { get; set; }
        public Guid IdMonitored { get; set; }
        public Monitored Monitored { get; set; }
        public string NotificationType { get; set; }
        public string Message { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
