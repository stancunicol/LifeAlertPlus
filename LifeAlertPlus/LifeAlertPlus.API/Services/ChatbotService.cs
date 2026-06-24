using System.Net.Http.Json;
using System.Text.Json;
using LifeAlertPlus.Shared.DTOs.Requests.Chat;
using LifeAlertPlus.Shared.DTOs.Responses.Chat;

namespace LifeAlertPlus.API.Services
{
    // Serviciu care trimite mesajele utilizatorului la API-ul Claude (Anthropic) și returnează răspunsul.
    // Folosește modelul Claude Haiku (cel mai rapid și economic) pentru răspunsuri medicale concise.
    // Chatbot-ul este limitat la 4 domenii: funcții aplicație, praguri vitale, condiții medicale,
    // interpretare alerte — orice altceva este declinat politicos.
    public class ChatbotService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ChatbotService> _logger;
        private readonly string _apiKey; // API key Anthropic din appsettings: Anthropic:ApiKey

        public ChatbotService(IHttpClientFactory httpClientFactory, ILogger<ChatbotService> logger, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _logger            = logger;
            _apiKey            = configuration["Anthropic:ApiKey"] ?? string.Empty;
        }

        // Construiește prompt-ul de sistem în limba solicitată (ro/en)
        // Prompt-ul definește personalitatea chatbot-ului și restricțiile de subiect
        private static string BuildSystemPrompt(string lang)
        {
            bool isEn      = string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase);
            string language = isEn ? "English" : "Romanian"; // Instrucțiunea de limbă în prompt

            // Prompt-ul de sistem este fix (nu se schimbă per conversație)
            // Restricțiile clare previn "jailbreak"-ul: chatbot-ul refuză subiecte în afara domeniului
            return $"""
                You are the LifeAlertPlus assistant, a specialized healthcare AI for a patient monitoring application.

                LifeAlertPlus is an IoT health monitoring system for elderly patients using ESP wearable sensors that continuously measure SpO2, heart rate, body temperature, and detect falls.

                You ONLY answer questions about:

                1. APPLICATION FEATURES
                   - Dashboard: real-time vital signs, alert overview, monitored persons list with status
                   - Monitored page: add/edit/remove monitored patients, view their vitals, configure custom thresholds per patient
                   - Notifications page: alert history, mark as read, filter by Critical/Alert type
                   - Settings: notification preferences (email, SMS, push), default vital sign thresholds, language
                   - Profile: personal info, password change, notification preferences
                   - Invitation system: invite doctors to view a patient's data

                2. VITAL SIGN THRESHOLDS
                   SpO2 (blood oxygen saturation):
                   • Normal: ≥ 95%
                   • Alert: 90–94% (hypoxemia — monitor closely)
                   • Critical: < 90% (severe hypoxemia — immediate action required)

                   Heart rate:
                   • Normal: 50–120 bpm
                   • Alert: < 50 bpm (bradycardia) or > 120 bpm (tachycardia)
                   • Critical: < 40 bpm or > 150 bpm (life-threatening arrhythmia)

                   Body temperature:
                   • Normal: 35.5–38.5°C
                   • Alert: < 35.5°C (mild hypothermia) or > 38.5°C (fever)
                   • Critical: < 34.5°C (severe hypothermia) or > 39.5°C (hyperpyrexia)

                   Fall detection:
                   • Any detected fall → immediate Critical alert, bypasses all cooldowns

                   Alert cooldowns:
                   • Critical: re-sent every 2 minutes while condition persists
                   • Alert: 10-minute cooldown between notifications

                3. MEDICAL CONDITIONS (relevant to monitored vital signs)
                   Cardiovascular: hypertension, bradycardia, tachycardia, arrhythmia, heart failure, MI risk
                   Respiratory: COPD, asthma, hypoxia, pneumonia — affect SpO2 readings
                   Neurological: Parkinson's disease, epilepsy — increase fall risk
                   Metabolic: diabetes mellitus (affects circulation), fever, hypothermia

                4. INTERPRETING ALERTS AND RECOMMENDATIONS
                   - Critical alert: check on patient immediately, call emergency services if condition persists > 5 min
                   - Alert-level: monitor closely, assess worsening trend
                   - Trend predictions (in the app): early warning of developing deterioration over 2-min windows

                IMPORTANT RULES:
                - Only answer questions within the above 4 topics
                - If asked about anything else (cooking, news, code, etc.) politely decline and explain what you can help with
                - Be concise and professional; explain medical terms in plain language when relevant
                - Always respond in {language}
                """;
        }

        // Trimite cererea de chat la API-ul Anthropic și returnează răspunsul modelului
        public async Task<ChatResponse> GetResponseAsync(ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                _logger.LogWarning("Anthropic API key is not configured.");
                return new ChatResponse { Success = false, Reply = "Chatbot service is not configured." };
            }

            try
            {
                var client = _httpClientFactory.CreateClient("Anthropic"); // Client HTTP preconfigurat cu BaseUrl Anthropic

                // Construim corpul cererii pentru API-ul Anthropic Messages
                var body = new
                {
                    model      = "claude-haiku-4-5-20251001", // Haiku: cel mai rapid și economic model Claude
                    max_tokens = 1024,                         // Limita de token-uri (evităm răspunsuri excesiv de lungi)
                    system     = BuildSystemPrompt(request.Lang), // Instrucțiunile sistemului (personalitate + restricții)
                    messages   = request.Messages              // Istoricul conversației (role: "user"/"assistant")
                        .Select(m => new { role = m.Role, content = m.Content })
                        .ToList()
                };

                var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
                {
                    Content = JsonContent.Create(body)
                };
                // Header-ele de autentificare Anthropic
                httpRequest.Headers.Add("x-api-key", _apiKey);          // Cheia API
                httpRequest.Headers.Add("anthropic-version", "2023-06-01"); // Versiunea API-ului Anthropic

                var response = await client.SendAsync(httpRequest);
                var json     = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Anthropic API error {Status}: {Body}", response.StatusCode, json);
                    return new ChatResponse { Success = false, Reply = "Service temporarily unavailable." };
                }

                // Parsăm răspunsul: content[0].text conține textul generat de model
                using var doc = JsonDocument.Parse(json);
                var text = doc.RootElement
                    .GetProperty("content")[0] // Primul bloc de conținut (Haiku returnează text simplu)
                    .GetProperty("text")
                    .GetString() ?? string.Empty;

                return new ChatResponse { Reply = text }; // Success=true implicit
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chatbot request failed.");
                return new ChatResponse { Success = false, Reply = "An error occurred." };
            }
        }
    }
}
