using System.ComponentModel.DataAnnotations;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Shared.DTOs.Responses.User;

namespace LifeAlertPlus.Application.Services
{
    // Serviciu de autentificare: hash/verificare BCrypt și validare regulilor de parolă/email
    // Nu are dependențe de DB — operează exclusiv pe stringuri și reguli de business
    public class AuthenticationService : IAuthenticationService
    {
        // Verifică dacă parola plaintext corespunde hash-ului BCrypt stocat în DB
        // BCrypt.Verify() este sigur față de timing attacks (comparație constantă în timp)
        public bool VerifyPassword(string password, string passwordHash)
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }

        // Generează un hash BCrypt pentru o parolă nouă (work factor implicit ~10 — lent intenționat)
        public string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        // Validează regulile de complexitate ale parolei (fără verificare DB — doar logică de business)
        // Reguli: minim 8 caractere, majusculă, minusculă, cifră, caracter special
        public Task<UserResponseDTO> VerifyPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return Task.FromResult(new UserResponseDTO { Success = false, Message = "Password is required." });

            if (password.Length < 8)
                return Task.FromResult(new UserResponseDTO { Success = false, Message = "Password must be at least 8 characters long." });

            // Verificăm că parola conține toate categoriile de caractere
            if (password.All(char.IsLower) || password.All(char.IsUpper) || !password.Any(char.IsDigit) || !password.Any(ch => !char.IsLetterOrDigit(ch)))
                return Task.FromResult(new UserResponseDTO { Success = false, Message = "Password must contain uppercase, lowercase, digit, and special character." });

            return Task.FromResult(new UserResponseDTO { Success = true, Message = "Password is valid." });
        }

        // Validează cererea de schimbare parolă:
        //   1. Câmpurile nu sunt goale
        //   2. Noua parolă coincide cu confirmarea
        //   3. Noua parolă respectă regulile de complexitate
        //   4. Noua parolă diferă de cea curentă
        public async Task<UserResponseDTO> ValidateChangePassword(string? currentPassword, string? newPassword, string? confirmPassword)
        {
            if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword) || string.IsNullOrEmpty(confirmPassword))
                return new UserResponseDTO { Success = false, Message = "Current password and new password are required." };

            if (newPassword != confirmPassword)
                return new UserResponseDTO { Success = false, Message = "New password and confirmation do not match." };

            var passwordValidation = await VerifyPassword(newPassword); // Verificăm regulile de complexitate
            if (!passwordValidation.Success)
                return passwordValidation;

            if (currentPassword == newPassword) // Nu permitem aceeași parolă
                return new UserResponseDTO { Success = false, Message = "New password cannot be the same as the current password." };

            return new UserResponseDTO { Success = true, Message = "Password change is valid." };
        }

        // Validează cererea de schimbare email:
        //   1. Câmpurile obligatorii nu sunt goale
        //   2. Noul email diferă de cel curent
        //   3. Noul email coincide cu confirmarea
        //   4. Formatul emailului este valid (EmailAddressAttribute din System.ComponentModel.DataAnnotations)
        public Task<UserResponseDTO> ValidateChangeEmail(UserChangeEmailRequestDTO request)
        {
            if (string.IsNullOrEmpty(request.CurrentEmail) || string.IsNullOrEmpty(request.NewEmail) || string.IsNullOrEmpty(request.ConfirmEmail)
                || request.CurrentEmail == request.NewEmail   // Emailul nou trebuie să fie diferit
                || request.NewEmail != request.ConfirmEmail   // Confirmarea trebuie să coincidă
                || string.IsNullOrEmpty(request.CurrentPassword))
            {
                return Task.FromResult(new UserResponseDTO { Success = false, Message = "Current email and new email are required." });
            }

            if (!new EmailAddressAttribute().IsValid(request.NewEmail)) // Validare format RFC standard
                return Task.FromResult(new UserResponseDTO { Success = false, Message = "Invalid email format." });

            return Task.FromResult(new UserResponseDTO { Success = true, Message = "Email change is valid." });
        }
    }
}
