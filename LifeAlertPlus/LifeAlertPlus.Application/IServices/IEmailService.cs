namespace LifeAlertPlus.Application.IServices
{
    public interface IEmailService
    {
        Task SendRegistrationSuccessEmailAsync(string recipientEmail, string recipientName, string verificationUrl);
        Task SendPasswordResetEmailAsync(string recipientEmail, string recipientName, string resetUrl);
        Task SendEmailChangeVerificationAsync(string recipientEmail, string recipientName, string verificationUrl, string oldEmail);
        Task SendEmailChangeNotificationAsync(string oldEmail, string recipientName, string newEmail, string cancelUrl);
        Task SendReportEmailAsync(string doctorEmail, string patientName, byte[] pdfAttachment);
        Task SendAlertNotificationEmailAsync(string recipientEmail, string recipientName, string patientName, string severity, string details);
    }
}