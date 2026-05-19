using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Shared.DTOs.Requests.Chat;

namespace LifeAlertPlus.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class ChatController : BaseApiController
    {
        private readonly ChatbotService _chatbotService;

        public ChatController(ChatbotService chatbotService)
        {
            _chatbotService = chatbotService;
        }

        [HttpPost]
        public async Task<IActionResult> Chat([FromBody] ChatRequest request)
        {
            if (request.Messages.Count == 0)
                return BadRequest(new { Message = "No messages provided." });

            // Max 20 messages to avoid excessive token usage
            if (request.Messages.Count > 20)
                request.Messages = request.Messages.Skip(request.Messages.Count - 20).ToList();

            var response = await _chatbotService.GetResponseAsync(request);
            if (!response.Success)
                return StatusCode(503, new { response.Reply });

            return Ok(response);
        }
    }
}
