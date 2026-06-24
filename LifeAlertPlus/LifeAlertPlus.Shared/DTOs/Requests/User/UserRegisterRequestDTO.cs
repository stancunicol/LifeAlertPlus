namespace LifeAlertPlus.Shared.DTOs.Requests.User
{
    // Server: primit de AuthenticationController.Register (POST /api/authentication/register) →
    // UserService.CreateUserAsync creează contul (provider "Local", email neconfirmat, praguri vitale implicite).
    // DataProcessingConsent e cerința GDPR — fără el, contul nu se creează.
    // Client: construit în RegisterPage.razor.cs la submit, trimis prin AuthApiClient.RegisterAsync.
    public class UserRegisterRequestDTO
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public bool DataProcessingConsent { get; set; }
    }
}
