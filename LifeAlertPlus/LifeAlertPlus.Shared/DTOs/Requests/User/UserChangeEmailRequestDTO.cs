namespace LifeAlertPlus.Shared.DTOs.Requests.User
{
    // Server: primit de AuthenticationController.ChangeEmail (POST /api/authentication/change-email) →
    // validat de AuthenticationService.ValidateChangeEmail, apoi UserService.InitiateEmailChangeAsync
    // trimite 2 token-uri (confirmare pe noul email + anulare pe cel vechi, din motive de securitate).
    // Client: completat în ProfilePage.razor.cs (proprietatea EmailChange) și trimis prin AuthApiClient.UpdateEmailAsync.
    public class UserChangeEmailRequestDTO
    {
        public string CurrentEmail { get; set; } = string.Empty;
        public string NewEmail { get; set; } = string.Empty;
        public string ConfirmEmail { get; set; } = string.Empty;
        public string CurrentPassword { get; set; } = string.Empty;
    }
}