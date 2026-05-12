using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Requests.User;

namespace LifeAlertPlus.Application.IServices
{
    public interface IUserService
    {
        Task<User?> GetUserByEmailAsync(string email);
        Task<User?> GetUserByPhoneNumberAsync(string phoneNumber);
        Task<User?> GetUserByIdAsync(Guid id);
        Task<User?> GetUserByResetTokenAsync(string token);
        Task<User?> GetUserByEmailChangeTokenAsync(string token);
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
        Task CancelEmailChangeAsync(User user);
        Task EmailChangeAsync(User user);
        Task InitiateEmailChangeAsync(User user, string newEmail);
        Task InitiatePasswordResetAsync(User user);
        Task PasswordChangeAsync(User user, string newPassword);
    }
}
