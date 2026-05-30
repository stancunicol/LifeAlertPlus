namespace LifeAlertPlus.Shared.DTOs.Responses.Notification
{
    public class PendingFeedbackDTO
    {
        public Guid Id { get; set; }
        public Guid IdMonitored { get; set; }
        public string NotificationType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string MonitoredName { get; set; } = string.Empty;
    }
}
