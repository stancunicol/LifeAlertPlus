namespace LifeAlertPlus.Shared.DTOs.Requests.User
{
    // Server: primit de AuthenticationController.Login (POST /api/authentication/login) — verifică
    // parola (BCrypt), confirmarea emailului, apoi emite JWT prin JwtService.GenerateToken.
    // Client: construit în LoginPage.razor.cs la submit, trimis prin AuthApiClient.LoginAsync.
    public class UserLoginRequestDTO
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
