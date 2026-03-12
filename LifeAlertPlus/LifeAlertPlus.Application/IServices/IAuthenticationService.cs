using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Shared.DTOs.Responses.User;

namespace LifeAlertPlus.Application.IServices
{
    public interface IAuthenticationService
    {
        bool VerifyPassword(string password, string passwordHash);
        string HashPassword(string password);
        Task<UserResponseDTO> VerifyPassword(string password);
        Task<UserResponseDTO> ValidateChangePassword(string? currentPassword, string? newPassword, string? confirmPassword);
        Task<UserResponseDTO> ValidateChangeEmail(UserChangeEmailRequestDTO request);
    }
}
