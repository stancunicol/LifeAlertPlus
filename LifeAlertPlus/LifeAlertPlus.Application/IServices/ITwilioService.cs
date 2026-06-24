using System.Threading.Tasks;

namespace LifeAlertPlus.Application.IServices
{
    // Interfață pentru serviciul de trimitere SMS prin Twilio (alerte critice)
    public interface ITwilioService
    {
        Task SendSmsAsync(string to, string message); // Trimite un SMS la numărul 'to' (format E.164: +40...) cu mesajul alertei
    }
}