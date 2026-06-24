namespace LifeAlertPlus.Shared.DTOs.Responses.Chat
{
    // Server: returnat de ChatController.Chat — răspunsul generat de chatbot pentru ultimul mesaj din ChatRequest.
    // Client: deserializat de ChatbotClientService.cs și afișat în ChatbotWidget.razor.cs.
    public class ChatResponse
    {
        public string Reply { get; set; } = string.Empty;
        public bool Success { get; set; } = true;
    }
}
