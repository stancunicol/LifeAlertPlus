namespace LifeAlertPlus.Application.IServices
{
    public interface IEmailService
    {
        Task SendRegistrationSuccessEmailAsync(string recipientEmail, string recipientName, string verificationUrl);
        Task SendPasswordResetEmailAsync(string recipientEmail, string recipientName, string resetUrl);
    }
}