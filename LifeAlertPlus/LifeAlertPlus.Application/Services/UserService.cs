using System.ComponentModel;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Requests.User;

namespace LifeAlertPlus.Application.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IAuthentificationService _authentificationService;

        public UserService(IUserRepository userRepository, IAuthentificationService authentificationService)
        {
            _userRepository = userRepository;
            _authentificationService = authentificationService;
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _userRepository.GetUserByEmailAsync(email);
        }

        public async Task<bool> CreateUserAsync(UserRegisterRequestDTO user)
        {
            var newUser = new User
            {
                Id = Guid.NewGuid(),
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Telephone = user.Telephone,
                PasswordHash = _authentificationService.HashPassword(user.Password),
                IsEmailConfirmed = false,
                CreatedAt = DateTime.UtcNow
            };

            await _userRepository.CreateUserAsync(newUser);
            return true;
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            return await _userRepository.UpdateUserAsync(user);
        }
    }
}