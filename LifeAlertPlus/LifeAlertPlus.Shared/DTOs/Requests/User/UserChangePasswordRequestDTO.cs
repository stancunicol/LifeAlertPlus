namespace LifeAlertPlus.Shared.DTOs.Requests.User
{
    // Server: primit de AuthenticationController.ChangePassword (POST /api/authentication/change-password) →
    // validat de AuthenticationService.ValidateChangePassword (complexitate parolă nouă, confirmare, parolă diferită de cea curentă).
    // Client: completat în ProfilePage.razor.cs (proprietatea PasswordChange) și trimis prin AuthApiClient.UpdatePasswordAsync.
    public class UserChangePasswordRequestDTO
    {
        public string Email { get; set; } = string.Empty;
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}