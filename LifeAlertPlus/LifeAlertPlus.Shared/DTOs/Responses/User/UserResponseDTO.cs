namespace LifeAlertPlus.Shared.DTOs.Responses.User
{
    // Răspuns generic "succes/mesaj" — cel mai reutilizat DTO din proiect. Folosit de aproape toate
    // acțiunile din AuthenticationController (Register, ForgotPassword, ChangeEmail confirm/cancel,
    // ChangePassword etc.) și de AuthenticationService.VerifyPassword/ValidateChangePassword/ValidateChangeEmail
    // pentru rezultatul validărilor interne. Mesajele sunt afișate direct în UI (toast/eroare formular).
    public class UserResponseDTO
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}