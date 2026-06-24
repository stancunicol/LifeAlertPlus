namespace LifeAlertPlus.Shared.DTOs.Requests.Chat
{
    // Cerere către chatbot-ul AI (asistent conversațional, separat de microserviciul de predicție vitale).
    // Server: primită de ChatController.Chat (POST /api/chat) → ChatbotService (probabil apel către Anthropic, vezi appsettings "Anthropic").
    // Client: construită de ChatbotClientService.cs și trimisă din ChatbotWidget.razor.cs (widgetul flotant de chat).
    public class ChatRequest
    {
        public List<ChatMessageDTO> Messages { get; set; } = new();   // Istoricul conversației — trimis complet la fiecare cerere (chatbot-ul nu ține sesiune pe server)
        public string Lang { get; set; } = "ro";                       // Limba răspunsului generat
    }

    // Un mesaj individual din conversație (rol "user" sau "assistant")
    public class ChatMessageDTO
    {
        public string Role { get; set; } = "user";
        public string Content { get; set; } = string.Empty;
    }
}
