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

        /// <summary>
        /// Caută sau creează un utilizator pe baza datelor Google (email, nume, googleId)
        /// </summary>
        Task<User?> FindOrCreateGoogleUserAsync(string email, string? name, string googleId);
    }
}
