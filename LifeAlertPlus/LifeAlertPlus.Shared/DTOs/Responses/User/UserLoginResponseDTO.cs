namespace LifeAlertPlus.Shared.DTOs.Responses.User
{
    // Server: returnat de AuthenticationController.Login — Success=false + Message pe orice eroare
    // (cont inexistent, parolă greșită, email neconfirmat), Token populat doar la succes.
    // Client: deserializat de AuthApiClient.LoginAsync; LoginPage.razor.cs salvează Token-ul în
    // sessionStorage și redirecționează către /admin-dashboard sau /dashboard în funcție de IsAdmin.
    public class UserLoginResponseDTO
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
    }
}
