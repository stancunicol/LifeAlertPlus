namespace LifeAlertPlus.Shared.DTOs.Requests.User
{
    public class UserUpdateEmailResponseDTO
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool RequiresLogout { get; set; } = false;
    }
}