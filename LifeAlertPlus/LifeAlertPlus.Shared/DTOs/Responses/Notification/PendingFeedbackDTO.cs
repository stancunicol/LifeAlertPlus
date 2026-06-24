namespace LifeAlertPlus.Shared.DTOs.Responses.Notification
{
    // Server: returnat de NotificationController.GetPendingFeedback (GET) — listează alertele
    // pentru care s-a cerut explicit feedback (FeedbackRequestedAt setat) și la care utilizatorul
    // nu a răspuns încă (vezi NotificationFeedbackTests.cs și NotificationFeedbackRequestDTO).
    // Client: afișat ca prompt/modal care întreabă "a fost reală alerta X?" — răspunsul se trimite
    // înapoi prin NotificationFeedbackRequestDTO.
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
