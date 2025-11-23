namespace LifeAlertPlus.Shared.DTOs.Requests.User
{
    public class UserResetPasswordRequestDTO
    {
        public string Token { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}