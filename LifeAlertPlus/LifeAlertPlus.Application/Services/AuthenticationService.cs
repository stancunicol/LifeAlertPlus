using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Shared.DTOs.Responses.User;

namespace LifeAlertPlus.Application.Services
{
    public class AuthenticationService : IAuthenticationService
    {
        public bool VerifyPassword(string password, string passwordHash)
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }

        public string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        public Task<UserResponseDTO> VerifyPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return Task.FromResult(new UserResponseDTO { Success = false, Message = "Password is required." });

            if (password.Length < 8)
                return Task.FromResult(new UserResponseDTO { Success = false, Message = "Password must be at least 8 characters long." });

            if (password.All(char.IsLower) || password.All(char.IsUpper) || !password.Any(char.IsDigit) || !password.Any(ch => !char.IsLetterOrDigit(ch)))
                return Task.FromResult(new UserResponseDTO { Success = false, Message = "Password must contain uppercase, lowercase, digit, and special character." });

            return Task.FromResult(new UserResponseDTO { Success = true, Message = "Password is valid." });
        }

        public async Task<UserResponseDTO> ValidateChangePassword(string? currentPassword, string? newPassword, string? confirmPassword)
        {
            if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword) || string.IsNullOrEmpty(confirmPassword))
            {
                return new UserResponseDTO { Success = false, Message = "Current password and new password are required." };
            }

            if (newPassword != confirmPassword)
            {
                return new UserResponseDTO { Success = false, Message = "New password and confirmation do not match." };
            }

            var passwordValidation = await VerifyPassword(newPassword);
            if (!passwordValidation.Success)
            {
                return passwordValidation;
            }

            if (currentPassword == newPassword)
            {
                return new UserResponseDTO { Success = false, Message = "New password cannot be the same as the current password." };
            }

            return new UserResponseDTO { Success = true, Message = "Password change is valid." };
        }

        public Task<UserResponseDTO> ValidateChangeEmail(UserChangeEmailRequestDTO request)
        {
            if (string.IsNullOrEmpty(request.CurrentEmail) || string.IsNullOrEmpty(request.NewEmail) || string.IsNullOrEmpty(request.ConfirmEmail)
                || request.CurrentEmail == request.NewEmail || request.NewEmail != request.ConfirmEmail || string.IsNullOrEmpty(request.CurrentPassword))
            {
                return Task.FromResult(new UserResponseDTO { Success = false, Message = "Current email and new email are required." });
            }

            if (!request.NewEmail.Contains('@') || !request.NewEmail.Contains('.'))
                return Task.FromResult(new UserResponseDTO { Success = false, Message = "Invalid email format." });

            return Task.FromResult(new UserResponseDTO { Success = true, Message = "Email change is valid." });
        }
    }
}
