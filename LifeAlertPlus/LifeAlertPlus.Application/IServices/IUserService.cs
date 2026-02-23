using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Requests.User;

namespace LifeAlertPlus.Application.IServices
{
    public interface IUserService
    {
        Task<User?> GetUserByEmailAsync(string email);
        Task<User?> GetUserByIdAsync(Guid id);
        Task<User?> GetUserByResetTokenAsync(string token);
        Task<IEnumerable<User>> GetAllUsersAsync();
        Task<bool> CreateUserAsync(UserRegisterRequestDTO user);
        Task<bool> UpdateUserAsync(User user);
        Task<User?> VerifyEmailAsync(string token);
        string GenerateEmailVerificationToken();
        string GeneratePasswordResetToken();
        string GenerateEmailChangeCancelToken();
        Task<User?> GetUserByEmailChangeCancelTokenAsync(string token);
        Task<User?> FindOrCreateGoogleUserAsync(string email, string? fullName, string googleId, string? givenName, string? familyName, string? profilePictureUrl);
        Task<bool> DeleteUserAsync(Guid id);
    }
}
