namespace LifeAlertPlus.Shared.DTOs.Responses.User
{
    // Client: tipul folosit de AuthApiClient.RegisterAsync la deserializarea răspunsului de la
    // POST /api/authentication/register. NOTĂ: server-ul (AuthenticationController.Register) returnează
    // de fapt o instanță UserResponseDTO — funcționează corect doar pentru că cele 2 clase au exact
    // aceeași formă JSON (Success, Message), deci deserializarea e structurală, nu legată de tipul C# exact.
    public class UserRegisterResponseDTO
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
