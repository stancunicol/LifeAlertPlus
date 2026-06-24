using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Requests.User;

namespace LifeAlertPlus.Application.IServices
{
    // Interfață pentru serviciul de gestionare utilizatori (CRUD cont, autentificare, flux email, OAuth Google)
    public interface IUserService
    {
        Task<User?> GetUserByEmailAsync(string email);                                                                                                          // Caută utilizatorul după adresa de email
        Task<User?> GetUserByPhoneNumberAsync(string phoneNumber);                                                                                              // Caută utilizatorul după numărul de telefon
        Task<User?> GetUserByIdAsync(Guid id);                                                                                                                  // Caută utilizatorul după ID (GUID)
        Task<User?> GetUserByResetTokenAsync(string token);                                                                                                     // Caută utilizatorul după token-ul de resetare parolă
        Task<User?> GetUserByEmailChangeTokenAsync(string token);                                                                                               // Caută utilizatorul după token-ul de confirmare schimbare email
        Task<IEnumerable<User>> GetAllUsersAsync();                                                                                                             // Returnează toți utilizatorii (folosit de Admin)
        Task<bool> CreateUserAsync(UserRegisterRequestDTO user);                                                                                                // Creează un cont nou (hash parolă, rol implicit, email confirmare)
        Task<bool> UpdateUserAsync(User user);                                                                                                                  // Salvează modificările unui utilizator existent în DB
        Task<User?> VerifyEmailAsync(string token);                                                                                                             // Confirmă adresa de email pe baza token-ului din link
        string GenerateEmailVerificationToken();                                                                                                                // Generează un token unic pentru confirmarea emailului (GUID)
        string GeneratePasswordResetToken();                                                                                                                    // Generează un token unic pentru resetarea parolei (GUID)
        string GenerateEmailChangeCancelToken();                                                                                                                // Generează un token unic pentru anularea schimbării emailului
        Task<User?> GetUserByEmailChangeCancelTokenAsync(string token);                                                                                        // Caută utilizatorul după token-ul de anulare schimbare email
        Task<User?> FindOrCreateGoogleUserAsync(string email, string? fullName, string googleId, string? givenName, string? familyName, string? profilePictureUrl); // OAuth Google: găsește contul existent sau creează unul nou (fără parolă)
        Task<bool> DeleteUserAsync(Guid id);                                                                                                                    // Șterge definitiv un utilizator din DB
        Task CancelEmailChangeAsync(User user);                                                                                                                 // Anulează o schimbare de email în curs (revine la emailul vechi)
        Task EmailChangeAsync(User user);                                                                                                                       // Confirmă și aplică schimbarea emailului după verificare
        Task InitiateEmailChangeAsync(User user, string newEmail);                                                                                              // Inițiază fluxul de schimbare email: salvează noul email și trimite link de confirmare
        Task InitiatePasswordResetAsync(User user);                                                                                                             // Inițiază fluxul de resetare parolă: generează token și trimite email
        Task PasswordChangeAsync(User user, string newPassword);                                                                                                // Aplică noua parolă (hash BCrypt) și invalidează token-ul de resetare
    }
}
