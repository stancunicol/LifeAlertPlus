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

        public async Task<User?> GetUserByIdAsync(Guid id)
        {
            return await _userRepository.GetUserByIdAsync(id);
        }

        public async Task<IEnumerable<User>> GetAllUsersAsync()
        {
            return await _userRepository.GetAllUsersAsync();
        }

        public string GenerateEmailVerificationToken()
        {
            return Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        }

        public async Task<User?> VerifyEmailAsync(string token)
        {
            var users = await _userRepository.GetAllUsersAsync();
            var user = users.FirstOrDefault(u => u.EmailConfirmationToken == token);

            if (user == null || user.EmailConfirmationExpires == null || user.EmailConfirmationExpires < DateTime.UtcNow)
            {
                return null;
            }

            user.IsEmailConfirmed = true;
            user.EmailConfirmationToken = null;
            user.EmailConfirmationExpires = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _userRepository.UpdateUserAsync(user);
            return user;
        }

        public async Task<bool> CreateUserAsync(UserRegisterRequestDTO user)
        {
            var emailToken = GenerateEmailVerificationToken();
            
            var newUser = new User
            {
                Id = Guid.NewGuid(),
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                PasswordHash = _authentificationService.HashPassword(user.Password),
                IsEmailConfirmed = false,
                EmailConfirmationToken = emailToken,
                EmailConfirmationExpires = DateTime.UtcNow.AddHours(24),
                CreatedAt = DateTime.UtcNow,
                Provider = "Local"
            };

            await _userRepository.CreateUserAsync(newUser);
            return true;
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            return await _userRepository.UpdateUserAsync(user);
        }

        public async Task<User?> GetUserByResetTokenAsync(string token)
        {
            var users = await _userRepository.GetAllUsersAsync();
            return users.FirstOrDefault(u => u.PasswordResetToken == token);
        }

        public string GeneratePasswordResetToken()
        {
            return Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        }

        public string GenerateEmailChangeCancelToken()
        {
            return Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        }

        public async Task<User?> GetUserByEmailChangeCancelTokenAsync(string token)
        {
            var users = await _userRepository.GetAllUsersAsync();
            return users.FirstOrDefault(u => u.EmailChangeCancelToken == token);
        }
        /// <summary>
        /// Caută sau creează un utilizator pe baza datelor Google (email, nume, googleId)
        /// </summary>
        public async Task<User?> FindOrCreateGoogleUserAsync(string email, string? name, string googleId)
        {
            var user = await _userRepository.GetUserByEmailAsync(email);
            if (user != null)
            {
                // Actualizează provider info dacă e nevoie
                if (user.Provider != "Google" || user.ProviderKey != googleId)
                {
                    user.Provider = "Google";
                    user.ProviderKey = googleId;
                    await _userRepository.UpdateUserAsync(user);
                }
                return user;
            }

            // Creează user nou
            var firstName = name?.Split(' ').FirstOrDefault() ?? "Google";
            var lastName = name?.Contains(' ') == true ? string.Join(' ', name.Split(' ').Skip(1)) : "User";
            var newUser = new User
            {
                Id = Guid.NewGuid(),
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                IsEmailConfirmed = true,
                Provider = "Google",
                ProviderKey = googleId,
                CreatedAt = DateTime.UtcNow
            };
            var created = await _userRepository.CreateUserAsync(newUser);
            return created ? newUser : null;
        }
    }
}