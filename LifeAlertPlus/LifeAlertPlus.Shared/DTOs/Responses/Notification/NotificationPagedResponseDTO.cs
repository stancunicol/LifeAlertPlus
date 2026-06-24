namespace LifeAlertPlus.Shared.DTOs.Responses.Notification
{
    // Server: returnat de NotificationController (GET, paginat) — include atât pagina curentă de
    // notificări cât și contoare agregate (CriticalCount/AlertCount/UnreadCount) folosite pentru
    // badge-uri/indicatoare în UI, calculate pe TOATE notificările, nu doar pe pagina curentă.
    // Client: consumat de NotificationsPage.razor.cs și de NotificationService.cs (polling periodic
    // pentru badge-ul de notificări necitite din header).
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
