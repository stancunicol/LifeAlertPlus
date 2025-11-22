using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Requests.User;

namespace LifeAlertPlus.Application.IServices
{
    public interface IUserService
    {
        Task<User?> GetUserByEmailAsync(string email);
        Task<bool> CreateUserAsync(UserRegisterRequestDTO user);
        Task<bool> UpdateUserAsync(User user);
    }
}
