namespace LifeAlertPlus.Shared.DTOs.Responses.User
{
    public class UserLoginResponseDTO
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
    }
}
