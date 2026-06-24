using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Shared.DTOs.Requests.Chat;

namespace LifeAlertPlus.API.Controllers
{
    // Controller pentru asistentul AI integrat în LifeAlertPlus.
    // Expune un singur endpoint POST care acceptă un istoric de mesaje și returnează răspunsul AI.
    // Modelul folosit este Claude Haiku (rapid și economic) cu un system prompt medical specializat.
    [ApiController]
    [Authorize] // Necesită autentificare — chatbot-ul nu e public
    [Route("api/[controller]")]
    public class ChatController : BaseApiController
    {
        private readonly ChatbotService _chatbotService; // Serviciul care comunică cu API-ul Anthropic

        public ChatController(ChatbotService chatbotService)
        {
            _chatbotService = chatbotService;
        }

        // POST /api/chat — Trimite un mesaj asistentului AI și primește răspuns
        // Body: { messages: [{role: "user"/"assistant", content: "..."}], lang: "ro"/"en" }
        [HttpPost]
        public async Task<IActionResult> Chat([FromBody] ChatRequest request)
        {
            // Fără mesaje nu putem trimite nimic la API
            if (request.Messages.Count == 0)
                return BadRequest(new { Message = "No messages provided." });

            // Limităm la maxim 20 mesaje pentru a evita costuri excesive de tokens API
            // Păstrăm doar cele mai recente 20 (contextul conversației)
            if (request.Messages.Count > 20)
                request.Messages = request.Messages.Skip(request.Messages.Count - 20).ToList();

            var response = await _chatbotService.GetResponseAsync(request); // Apel către Claude API

            // Dacă API-ul AI nu a răspuns (cheie lipsă, rată depășită, eroare de rețea)
            if (!response.Success)
                return StatusCode(503, new { response.Reply }); // 503 Service Unavailable

            return Ok(response); // { reply: "...", success: true }
        }
    }
}
