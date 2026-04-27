using System.Threading.Tasks;

namespace LifeAlertPlus.Application.IServices
{
    public interface ITwilioService
    {
        Task SendSmsAsync(string to, string message);
    }
}