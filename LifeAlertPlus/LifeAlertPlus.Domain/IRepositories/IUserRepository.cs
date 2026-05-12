using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    public interface IUserRepository
    {
        Task<User?> GetUserByEmailAsync(string email);
        Task<User?> GetUserByPhoneNumberAsync(string phoneNumber);
        Task<User?> GetUserByIdAsync(Guid id);
        Task<User?> GetUserByEmailChangeTokenAsync(string token);
        Task<User?> GetUserByEmailConfirmationTokenAsync(string token);
        Task<User?> GetUserByPasswordResetTokenAsync(string token);
        Task<User?> GetUserByEmailChangeCancelTokenAsync(string token);
        Task<IEnumerable<User>> GetAllUsersAsync();
        Task<bool> CreateUserAsync(User user);
        Task<bool> UpdateUserAsync(User user);
        Task<bool> DeleteUserAsync(Guid id);
    }
}
