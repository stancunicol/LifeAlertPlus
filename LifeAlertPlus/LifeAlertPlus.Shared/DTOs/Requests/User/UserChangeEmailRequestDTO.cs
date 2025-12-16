namespace LifeAlertPlus.Shared.DTOs.Requests.User
{
    public class UserChangeEmailRequestDTO
    {
        public string CurrentEmail { get; set; } = string.Empty;
        public string NewEmail { get; set; } = string.Empty;
        public string ConfirmEmail { get; set; } = string.Empty;
        public string CurrentPassword { get; set; } = string.Empty;
    }
}