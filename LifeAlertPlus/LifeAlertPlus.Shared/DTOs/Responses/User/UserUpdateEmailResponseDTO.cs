namespace LifeAlertPlus.Shared.DTOs.Responses.User
{
    // Server: returnat de AuthenticationController.ChangeEmail (linia ~321) — RequiresLogout=true
    // semnalează Client-ului că trebuie să delogheze utilizatorul, pentru că schimbarea emailului
    // invalidează implicit sesiunea curentă (până la confirmarea pe noua adresă).
    // Client: deserializat de AuthApiClient.UpdateEmailAsync, folosit din ProfilePage.razor.cs.
    public class UserUpdateEmailResponseDTO
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool RequiresLogout { get; set; } = false;
    }
}