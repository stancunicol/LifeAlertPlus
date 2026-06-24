namespace LifeAlertPlus.Shared.DTOs.Requests.Notification
{
    // Server: primit de NotificationController.SubmitFeedback (POST /api/notification/{id}/feedback) —
    // marchează dacă o alertă a fost reală (true) sau falsă alarmă (false); vezi NotificationFeedbackTests.cs.
    // Client: trimis de NotificationService.cs (linia ~78), apelat din pagina de notificări când
    // utilizatorul confirmă/infirmă o alertă pentru care backend-ul a cerut explicit feedback.
    public class NotificationFeedbackRequestDTO
    {
        public bool WasReal { get; set; }
    }
}
