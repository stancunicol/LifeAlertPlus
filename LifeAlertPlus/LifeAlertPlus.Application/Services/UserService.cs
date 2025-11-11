using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.IRepositories;

namespace LifeAlertPlus.Application.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;

        public UserService(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<Domain.Entities.User?> GetUserByEmailAsync(string email)
        {
            return await _userRepository.GetUserByEmailAsync(email);
        }
    }
}