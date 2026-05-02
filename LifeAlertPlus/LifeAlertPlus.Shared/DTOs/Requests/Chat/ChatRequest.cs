namespace LifeAlertPlus.Shared.DTOs.Requests.Chat
{
    public class ChatRequest
    {
        public List<ChatMessageDTO> Messages { get; set; } = new();
        public string Lang { get; set; } = "ro";
    }

    public class ChatMessageDTO
    {
        public string Role { get; set; } = "user";
        public string Content { get; set; } = string.Empty;
    }
}
