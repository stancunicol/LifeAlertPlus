using System.Threading.Tasks;
using LifeAlertPlus.Application.IServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Twilio;
using Twilio.Rest.Api.V2010.Account;

namespace LifeAlertPlus.Infrastructure.Services
{
    public class TwilioService : ITwilioService
    {
        private readonly string _accountSid;
        private readonly string _authToken;
        private readonly string _fromNumber;
        private readonly ILogger<TwilioService> _logger;

        public TwilioService(IConfiguration configuration, ILogger<TwilioService> logger)
        {
            _logger = logger;
            _accountSid = configuration["Twilio:AccountSid"];
            _authToken = configuration["Twilio:AuthToken"];
            _fromNumber = configuration["Twilio:FromNumber"];

            if (string.IsNullOrWhiteSpace(_accountSid) || string.IsNullOrWhiteSpace(_authToken) || string.IsNullOrWhiteSpace(_fromNumber))
            {
                _logger.LogWarning("Twilio SMS is not fully configured. AccountSid/AuthToken/FromNumber are required.");
            }
        }

        public async Task SendSmsAsync(string to, string message)
        {
            if (string.IsNullOrWhiteSpace(_accountSid) || string.IsNullOrWhiteSpace(_authToken) || string.IsNullOrWhiteSpace(_fromNumber))
            {
                throw new InvalidOperationException("Twilio credentials are missing or incomplete. Please configure Twilio:AccountSid, Twilio:AuthToken, and Twilio:FromNumber.");
            }

            _logger.LogInformation("Sending Twilio SMS to {PhoneNumber} from {FromNumber}.", to, _fromNumber);
            TwilioClient.Init(_accountSid, _authToken);
            await MessageResource.CreateAsync(
                body: message,
                from: new Twilio.Types.PhoneNumber(_fromNumber),
                to: new Twilio.Types.PhoneNumber(to)
            );
        }
    }
}