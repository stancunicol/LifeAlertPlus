using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Shared.DTOs.Responses.User;

namespace LifeAlertPlus.Application.IServices
{
    // Interfață pentru serviciul de autentificare — hash/verificare parole și validare modificări cont
    public interface IAuthenticationService
    {
        bool VerifyPassword(string password, string passwordHash);                                                        // Verifică dacă parola plaintext corespunde hash-ului BCrypt stocat
        string HashPassword(string password);                                                                             // Generează hash BCrypt pentru o parolă nouă
        Task<UserResponseDTO> VerifyPassword(string password);                                                            // Verifică parola față de utilizatorul curent din context (JWT)
        Task<UserResponseDTO> ValidateChangePassword(string? currentPassword, string? newPassword, string? confirmPassword); // Validează cererea de schimbare parolă (verifică parola curentă + confirmare)
        Task<UserResponseDTO> ValidateChangeEmail(UserChangeEmailRequestDTO request);                                     // Validează cererea de schimbare email (verifică parola curentă)
    }
}
