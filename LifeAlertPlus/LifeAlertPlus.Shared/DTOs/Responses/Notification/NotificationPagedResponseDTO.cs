namespace LifeAlertPlus.Shared.DTOs.Responses.Notification
{
    public class NotificationPagedResponseDTO
    {
        public List<NotificationItemDTO> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int CriticalCount { get; set; }
        public int AlertCount { get; set; }
        public int UnreadCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    public class NotificationItemDTO
    {
        public Guid Id { get; set; }
        public string NotificationType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public Guid IdMonitored { get; set; }
        public string MonitoredName { get; set; } = string.Empty;
        public bool IsRead { get; set; }
    }
}
