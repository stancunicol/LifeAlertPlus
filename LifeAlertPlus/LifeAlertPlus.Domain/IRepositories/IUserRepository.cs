using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    // Interfață repository pentru tabela Users (conturi utilizatori — îngrijitori și admini)
    public interface IUserRepository
    {
        Task<User?> GetUserByEmailAsync(string email);                         // Caută după email (autentificare, verificare duplicate la înregistrare)
        Task<User?> GetUserByPhoneNumberAsync(string phoneNumber);             // Caută după numărul de telefon (verificare duplicate)
        Task<User?> GetUserByIdAsync(Guid id);                                 // Caută după ID (cel mai frecvent — din claims JWT)
        Task<User?> GetUserByEmailChangeTokenAsync(string token);              // Caută după token-ul de confirmare email nou (flux schimbare email)
        Task<User?> GetUserByEmailConfirmationTokenAsync(string token);        // Caută după token-ul de confirmare la înregistrare
        Task<User?> GetUserByPasswordResetTokenAsync(string token);            // Caută după token-ul de resetare parolă
        Task<User?> GetUserByEmailChangeCancelTokenAsync(string token);        // Caută după token-ul de anulare schimbare email (securitate — link în emailul vechi)
        Task<IEnumerable<User>> GetAllUsersAsync();                            // Returnează toți utilizatorii (Admin — exclude de obicei adminii înșiși)
        Task<bool> CreateUserAsync(User user);                                 // Inserează un cont nou în DB
        Task<bool> UpdateUserAsync(User user);                                 // Salvează modificările unui utilizator existent
        Task<bool> DeleteUserAsync(Guid id);                                   // Ștergere permanentă (hard-delete, GDPR Art.17)
    }
}
