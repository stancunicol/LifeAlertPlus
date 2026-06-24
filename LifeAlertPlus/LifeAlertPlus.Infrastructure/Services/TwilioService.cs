using LifeAlertPlus.Application.IServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Twilio;
using Twilio.Rest.Api.V2010.Account;

namespace LifeAlertPlus.Infrastructure.Services
{
    // Implementare ITwilioService — trimite SMS-uri de alertă prin API-ul Twilio
    public class TwilioService : ITwilioService
    {
        private readonly string _accountSid;
        private readonly string _authToken;
        private readonly string _fromNumber;
        private readonly ILogger<TwilioService> _logger;

        // Flag care indică dacă există credențiale valide — evită crash la pornire dacă SMS e dezactivat (ex: în Development)
        private readonly bool _isConfigured;

        public TwilioService(IConfiguration configuration, ILogger<TwilioService> logger)
        {
            _logger = logger;
            _accountSid = configuration["Twilio:AccountSid"] ?? string.Empty;
            _authToken  = configuration["Twilio:AuthToken"]  ?? string.Empty;
            _fromNumber = configuration["Twilio:FromNumber"] ?? string.Empty;

            _isConfigured = !string.IsNullOrWhiteSpace(_accountSid)
                         && !string.IsNullOrWhiteSpace(_authToken)
                         && !string.IsNullOrWhiteSpace(_fromNumber);

            if (_isConfigured)
                TwilioClient.Init(_accountSid, _authToken); // Inițializare client static Twilio (o singură dată, la pornirea aplicației)
            else
                _logger.LogError("Twilio is not configured. Set Twilio:AccountSid, Twilio:AuthToken, Twilio:FromNumber to enable SMS notifications.");
        }

        // Trimite SMS prin Twilio — numărul "to" trebuie în format E.164 (ex: +40712345678)
        public async Task SendSmsAsync(string to, string message)
        {
            if (!_isConfigured)
                throw new InvalidOperationException("Twilio credentials are not configured.");

            _logger.LogInformation("Sending Twilio SMS to {PhoneNumber} from {FromNumber}.", to, _fromNumber);
            await MessageResource.CreateAsync(
                body: message,
                from: new Twilio.Types.PhoneNumber(_fromNumber),
                to: new Twilio.Types.PhoneNumber(to)
            );
        }
    }
}