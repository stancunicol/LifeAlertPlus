namespace LifeAlertPlus.Application.IServices
{
    public interface IEmailService
    {
        Task SendRegistrationSuccessEmailAsync(string recipientEmail, string recipientName, string loginUrl);
    }
}