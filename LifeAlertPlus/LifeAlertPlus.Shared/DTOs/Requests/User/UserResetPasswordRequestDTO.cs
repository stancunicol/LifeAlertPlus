namespace LifeAlertPlus.Shared.DTOs.Requests.User
{
    // Server: primit de AuthenticationController.ResetPassword (POST /api/authentication/reset-password) —
    // Token-ul vine din link-ul trimis pe email (valabil 1 oră, vezi UserService.InitiatePasswordResetAsync).
    // Client: construit în ResetPasswordPage.razor.cs (linia ~120) la submit-ul formularului de resetare.
    public class UserResetPasswordRequestDTO
    {
        public string Token { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}