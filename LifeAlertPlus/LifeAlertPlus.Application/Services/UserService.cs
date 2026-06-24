using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Requests.User;

namespace LifeAlertPlus.Application.Services
{
    // Serviciu principal pentru gestionarea conturilor de utilizatori
    // Acoperă: CRUD cont, autentificare email, flux schimbare email/parolă, OAuth Google
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IAuthenticationService _authenticationService; // Hash/verificare BCrypt
        private readonly IRoleRepository _roleRepository;               // Căutare rol "User" la creare cont

        public UserService(IUserRepository userRepository, IAuthenticationService authenticationService, IRoleRepository roleRepository)
        {
            _userRepository        = userRepository;
            _authenticationService = authenticationService;
            _roleRepository        = roleRepository;
        }

        public async Task<User?> GetUserByEmailAsync(string email) =>
            await _userRepository.GetUserByEmailAsync(email);

        public async Task<User?> GetUserByPhoneNumberAsync(string phoneNumber) =>
            await _userRepository.GetUserByPhoneNumberAsync(phoneNumber);

        public async Task<User?> GetUserByIdAsync(Guid id) =>
            await _userRepository.GetUserByIdAsync(id);

        public async Task<IEnumerable<User>> GetAllUsersAsync() =>
            await _userRepository.GetAllUsersAsync();

        // Generează un token URL-safe (2× GUID base64 concatenate → ~43 caractere)
        // Suficient de lung pentru a fi imposibil de ghicit, scurt pentru URL-uri
        public string GenerateEmailVerificationToken() => GenerateToken();
        private static string GenerateToken() =>
            Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());

        // Confirmă emailul pe baza token-ului primit în link-ul de confirmare
        // Verifică și expirarea (24 ore de la înregistrare)
        public async Task<User?> VerifyEmailAsync(string token)
        {
            var user = await _userRepository.GetUserByEmailConfirmationTokenAsync(token);
            if (user == null || user.EmailConfirmationExpires == null || user.EmailConfirmationExpires < DateTime.UtcNow)
                return null; // Token inexistent sau expirat

            user.IsEmailConfirmed       = true;
            user.EmailConfirmationToken   = null; // Invalidăm token-ul după folosire
            user.EmailConfirmationExpires = null;
            user.UpdatedAt              = DateTime.UtcNow;
            await _userRepository.UpdateUserAsync(user);
            return user;
        }

        // Creează un cont nou cu email+parolă (provider "Local")
        // Setează valorile implicite pentru praguri vitale și preferințe notificări
        public async Task<bool> CreateUserAsync(UserRegisterRequestDTO user)
        {
            var userRole = await _roleRepository.GetRoleByNameAsync("User")
                ?? throw new InvalidOperationException("Default role 'User' is missing. Seed roles before creating users.");

            var emailToken = GenerateEmailVerificationToken();

            var newUser = new User
            {
                Id                       = Guid.NewGuid(),
                RoleId                   = userRole.Id,
                FirstName                = user.FirstName,
                LastName                 = user.LastName,
                Email                    = user.Email,
                PhoneNumber              = user.PhoneNumber,
                PasswordHash             = _authenticationService.HashPassword(user.Password), // Hash BCrypt
                IsEmailConfirmed         = false,              // Necesită confirmare email
                EmailConfirmationToken   = emailToken,
                EmailConfirmationExpires = DateTime.UtcNow.AddHours(24), // Token valabil 24 ore
                CreatedAt                = DateTime.UtcNow,
                Provider                 = "Local",            // Autentificare email+parolă (nu OAuth)
                // Praguri vitale implicite (vor fi ajustate per afecțiune de ConditionThresholdAdjuster)
                MinHeartRate             = 60,
                MaxHeartRate             = 100,
                MinTemperature           = 36.0,
                MaxTemperature           = 37.5,
                MinSpO2                  = 95,
                MaxSpO2                  = 100,
                Language                 = "ro",       // Limbă implicită română
                UpdateFrequency          = 30,         // Frecvența actualizărilor ESP (secunde)
                NotifyByEmail            = true,
                NotifyByPush             = true,
                DataProcessingConsentAt  = user.DataProcessingConsent ? DateTime.UtcNow : null // GDPR
            };

            return await _userRepository.CreateUserAsync(newUser);
        }

        public async Task<bool> UpdateUserAsync(User user) =>
            await _userRepository.UpdateUserAsync(user);

        public async Task<User?> GetUserByResetTokenAsync(string token) =>
            await _userRepository.GetUserByPasswordResetTokenAsync(token);

        public string GeneratePasswordResetToken()    => GenerateToken();
        public string GenerateEmailChangeCancelToken() => GenerateToken();

        public async Task<User?> GetUserByEmailChangeCancelTokenAsync(string token) =>
            await _userRepository.GetUserByEmailChangeCancelTokenAsync(token);

        // OAuth Google: găsește contul existent sau creează unul nou
        // SECURITATE: dacă emailul există cu alt provider → aruncă GoogleEmailConflictException
        //             (previne preluarea contului de către oricine controlează acel email pe Google)
        public async Task<User?> FindOrCreateGoogleUserAsync(string email, string? fullName, string googleId, string? givenName, string? familyName, string? profilePictureUrl)
        {
            var userRole = await _roleRepository.GetRoleByNameAsync("User")
                ?? throw new InvalidOperationException("Default role 'User' is missing. Seed roles before creating users.");

            var user = await _userRepository.GetUserByEmailAsync(email);

            // Rezolvăm prenumele și numele din datele Google (givenName/familyName au prioritate față de fullName)
            var resolvedFirstName = !string.IsNullOrWhiteSpace(givenName)
                ? givenName.Trim()
                : fullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Google";
            var resolvedLastName = !string.IsNullOrWhiteSpace(familyName)
                ? familyName.Trim()
                : fullName?.Contains(' ') == true
                    ? string.Join(' ', fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1))
                    : "User";

            if (user != null)
            {
                // Emailul există deja cu altă metodă de autentificare → respingem (securitate)
                if (!string.Equals(user.Provider, "Google", StringComparison.OrdinalIgnoreCase))
                    throw new GoogleEmailConflictException(email);

                // Același email + Google → actualizăm datele de profil dacă s-au schimbat
                if (user.ProviderKey != googleId ||
                    user.FirstName != resolvedFirstName || user.LastName != resolvedLastName ||
                    user.ProfilePictureUrl != profilePictureUrl || user.RoleId == Guid.Empty)
                {
                    user.ProviderKey        = googleId;
                    user.FirstName          = resolvedFirstName;
                    user.LastName           = resolvedLastName;
                    user.ProfilePictureUrl  = profilePictureUrl;
                    if (user.RoleId == Guid.Empty) user.RoleId = userRole.Id;
                    user.UpdatedAt          = DateTime.UtcNow;
                    await _userRepository.UpdateUserAsync(user);
                }
                return user;
            }

            // Cont Google nou — emailul e confirmat automat (Google îl verifică)
            var newUser = new User
            {
                Id                = Guid.NewGuid(),
                FirstName         = resolvedFirstName,
                LastName          = resolvedLastName,
                Email             = email,
                RoleId            = userRole.Id,
                ProfilePictureUrl = profilePictureUrl,
                IsEmailConfirmed  = true,           // Google garantează verificarea emailului
                Provider          = "Google",
                ProviderKey       = googleId,        // ID-ul unic al contului Google
                CreatedAt         = DateTime.UtcNow,
                MinHeartRate      = 60, MaxHeartRate = 100,
                MinTemperature    = 36.0, MaxTemperature = 37.5,
                MinSpO2           = 95, MaxSpO2 = 100,
                Language          = "ro",
                UpdateFrequency   = 30,
                NotifyByEmail     = true,
                NotifyByPush      = true
            };
            var created = await _userRepository.CreateUserAsync(newUser);
            return created ? newUser : null;
        }

        public async Task<bool> DeleteUserAsync(Guid id) =>
            await _userRepository.DeleteUserAsync(id);

        // Anulează o schimbare de email în curs — curăță toate câmpurile temporare
        public async Task CancelEmailChangeAsync(User user)
        {
            user.EmailChangeCancelToken = null;
            user.EmailChangeExpires     = null;
            user.EmailChangeToken       = null;
            user.PendingEmail           = null;
            user.UpdatedAt              = DateTime.UtcNow;
            await _userRepository.UpdateUserAsync(user);
        }

        // Confirmă și aplică schimbarea emailului după ce utilizatorul a dat click pe link
        public async Task EmailChangeAsync(User user)
        {
            if (string.IsNullOrEmpty(user.PendingEmail)) return; // Nicio schimbare în așteptare

            user.Email                = user.PendingEmail; // Aplicăm noul email
            user.IsEmailConfirmed     = true;
            user.PendingEmail         = null; // Curățăm câmpurile temporare
            user.EmailChangeToken     = null;
            user.EmailChangeCancelToken = null;
            user.EmailChangeExpires   = null;
            user.UpdatedAt            = DateTime.UtcNow;
            await _userRepository.UpdateUserAsync(user);
        }

        public async Task<User?> GetUserByEmailChangeTokenAsync(string token) =>
            await _userRepository.GetUserByEmailChangeTokenAsync(token);

        // Inițiază fluxul de schimbare email: salvează noul email + generează 2 token-uri
        //   - emailChangeToken: confirmare la noua adresă (utilizatorul apasă "Confirmă")
        //   - cancelToken: anulare de la vechea adresă (securitate — dacă altcineva a inițiat)
        public async Task InitiateEmailChangeAsync(User user, string newEmail)
        {
            var emailChangeToken = GenerateEmailVerificationToken();
            var cancelToken      = GenerateEmailChangeCancelToken();

            user.PendingEmail           = newEmail;
            user.EmailChangeCancelToken = cancelToken;
            user.EmailChangeToken       = emailChangeToken;
            user.EmailChangeExpires     = DateTime.UtcNow.AddHours(24); // Token valabil 24 ore
            user.UpdatedAt              = DateTime.UtcNow;
            await _userRepository.UpdateUserAsync(user);
        }

        // Inițiază resetarea parolei: generează token și îl salvează cu expirare 1 oră
        public async Task InitiatePasswordResetAsync(User user)
        {
            var resetToken = GeneratePasswordResetToken();

            user.PasswordResetToken   = resetToken;
            user.PasswordResetExpires = DateTime.UtcNow.AddHours(1); // Token valabil 1 oră
            user.UpdatedAt            = DateTime.UtcNow;
            await _userRepository.UpdateUserAsync(user);
        }

        // Aplică noua parolă: hash BCrypt, invalidează token-ul de resetare, salvează momentul schimbării
        // LastChangedPasswordAt este inclus în JWT → token-urile emise înainte de această dată devin invalide
        public async Task PasswordChangeAsync(User user, string newPassword)
        {
            user.PasswordHash         = _authenticationService.HashPassword(newPassword);
            user.PasswordResetToken   = null; // Invalidăm token-ul după folosire
            user.PasswordResetExpires = null;
            user.LastChangedPasswordAt = DateTime.UtcNow;
            user.UpdatedAt            = DateTime.UtcNow;
            await _userRepository.UpdateUserAsync(user);
        }
    }
}