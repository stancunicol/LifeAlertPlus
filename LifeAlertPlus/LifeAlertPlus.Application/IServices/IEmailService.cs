namespace LifeAlertPlus.Application.IServices
{
    public interface IEmailService
    {
        Task SendRegistrationSuccessEmailAsync(string recipientEmail, string recipientName, string verificationUrl);
        Task SendPasswordResetEmailAsync(string recipientEmail, string recipientName, string resetUrl);
        Task SendEmailChangeVerificationAsync(string recipientEmail, string recipientName, string verificationUrl, string oldEmail);
        Task SendEmailChangeNotificationAsync(string oldEmail, string recipientName, string newEmail, string cancelUrl);
    }
}