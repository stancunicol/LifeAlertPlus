using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Requests.User;

namespace LifeAlertPlus.Application.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IAuthenticationService _authenticationService;
        private readonly IRoleRepository _roleRepository;

        public UserService(IUserRepository userRepository, IAuthenticationService authenticationService, IRoleRepository roleRepository)
        {
            _userRepository = userRepository;
            _authenticationService = authenticationService;
            _roleRepository = roleRepository;
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _userRepository.GetUserByEmailAsync(email);
        }

        public async Task<User?> GetUserByPhoneNumberAsync(string phoneNumber)
        {
            return await _userRepository.GetUserByPhoneNumberAsync(phoneNumber);
        }

        public async Task<User?> GetUserByIdAsync(Guid id)
        {
            return await _userRepository.GetUserByIdAsync(id);
        }

        public async Task<IEnumerable<User>> GetAllUsersAsync()
        {
            return await _userRepository.GetAllUsersAsync();
        }

        public string GenerateEmailVerificationToken() => GenerateToken();

        private static string GenerateToken() =>
            Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());

        public async Task<User?> VerifyEmailAsync(string token)
        {
            var user = await _userRepository.GetUserByEmailConfirmationTokenAsync(token);

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
            var userRole = await _roleRepository.GetRoleByNameAsync("User");
            if (userRole == null)
            {
                throw new InvalidOperationException("Default role 'User' is missing. Seed roles before creating users.");
            }

            var emailToken = GenerateEmailVerificationToken();
            
            var newUser = new User
            {
                Id = Guid.NewGuid(),
                RoleId = userRole.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                PasswordHash = _authenticationService.HashPassword(user.Password),
                IsEmailConfirmed = false,
                EmailConfirmationToken = emailToken,
                EmailConfirmationExpires = DateTime.UtcNow.AddHours(24),
                CreatedAt = DateTime.UtcNow,
                Provider = "Local",
                MinHeartRate = 60,
                MaxHeartRate = 100,
                MinTemperature = 36.0,
                MaxTemperature = 37.5,
                MinSpO2 = 95,
                MaxSpO2 = 100,
                Language = "ro",
                FontSize = "medium",
                UpdateFrequency = 30,
                NotifyByEmail = true,
                NotifyByPush = true
            };

            var result = await _userRepository.CreateUserAsync(newUser);
            return result;
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            return await _userRepository.UpdateUserAsync(user);
        }

        public async Task<User?> GetUserByResetTokenAsync(string token)
        {
            return await _userRepository.GetUserByPasswordResetTokenAsync(token);
        }

        public string GeneratePasswordResetToken() => GenerateToken();

        public string GenerateEmailChangeCancelToken() => GenerateToken();

        public async Task<User?> GetUserByEmailChangeCancelTokenAsync(string token)
        {
            return await _userRepository.GetUserByEmailChangeCancelTokenAsync(token);
        }

        public async Task<User?> FindOrCreateGoogleUserAsync(string email, string? fullName, string googleId, string? givenName, string? familyName, string? profilePictureUrl)
        {
            var userRole = await _roleRepository.GetRoleByNameAsync("User")
                ?? throw new InvalidOperationException("Default role 'User' is missing. Seed roles before creating users.");

            var user = await _userRepository.GetUserByEmailAsync(email);
            var resolvedFirstName = !string.IsNullOrWhiteSpace(givenName)
                ? givenName.Trim()
                : fullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Google";
            var resolvedLastName = !string.IsNullOrWhiteSpace(familyName)
                ? familyName.Trim()
                : fullName?.Contains(' ') == true ? string.Join(' ', fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)) : "User";

            if (user != null)
            {
                if (user.Provider != "Google" || user.ProviderKey != googleId ||
                    user.FirstName != resolvedFirstName || user.LastName != resolvedLastName ||
                    user.ProfilePictureUrl != profilePictureUrl ||
                    user.RoleId == Guid.Empty)
                {
                    user.Provider = "Google";
                    user.ProviderKey = googleId;
                    user.FirstName = resolvedFirstName;
                    user.LastName = resolvedLastName;
                    user.ProfilePictureUrl = profilePictureUrl;
                    user.RoleId = userRole.Id;
                    user.UpdatedAt = DateTime.UtcNow;
                    await _userRepository.UpdateUserAsync(user);
                }
                return user;
            }

            var newUser = new User
            {
                Id = Guid.NewGuid(),
                FirstName = resolvedFirstName,
                LastName = resolvedLastName,
                Email = email,
                RoleId = userRole.Id,
                ProfilePictureUrl = profilePictureUrl,
                IsEmailConfirmed = true,
                Provider = "Google",
                ProviderKey = googleId,
                CreatedAt = DateTime.UtcNow,
                MinHeartRate = 60,
                MaxHeartRate = 100,
                MinTemperature = 36.0,
                MaxTemperature = 37.5,
                MinSpO2 = 95,
                MaxSpO2 = 100,
                Language = "ro",
                FontSize = "medium",
                UpdateFrequency = 30,
                NotifyByEmail = true,
                NotifyByPush = true
            };
            var created = await _userRepository.CreateUserAsync(newUser);
            return created ? newUser : null;
        }

        public async Task<bool> DeleteUserAsync(Guid id)
        {
            return await _userRepository.DeleteUserAsync(id);
        }

        public async Task CancelEmailChangeAsync(User user)
        {
            user.EmailChangeCancelToken = null;
            user.EmailChangeExpires = null;
            user.EmailChangeToken = null;
            user.PendingEmail = null;
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.UpdateUserAsync(user);
        }

        public async Task EmailChangeAsync(User user)
        {
            if (string.IsNullOrEmpty(user.PendingEmail))
            {
                return;
            }

            user.Email = user.PendingEmail;
            user.IsEmailConfirmed = true;
            user.PendingEmail = null;
            user.EmailChangeToken = null;
            user.EmailChangeCancelToken = null;
            user.EmailChangeExpires = null;
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.UpdateUserAsync(user);
        }

        public async Task<User?> GetUserByEmailChangeTokenAsync(string token)
        {
            return await _userRepository.GetUserByEmailChangeTokenAsync(token);
        }

        public async Task InitiateEmailChangeAsync(User user, string newEmail)
        {
            var emailChangeToken = GenerateEmailVerificationToken();
            var cancelToken = GenerateEmailChangeCancelToken();

            user.PendingEmail = newEmail;
            user.EmailChangeCancelToken = cancelToken;
            user.EmailChangeToken = emailChangeToken;
            user.EmailChangeExpires = DateTime.UtcNow.AddHours(24);
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.UpdateUserAsync(user);
        }

        public async Task InitiatePasswordResetAsync(User user)
        {
            var resetToken = GeneratePasswordResetToken();

            user.PasswordResetToken = resetToken;
            user.PasswordResetExpires = DateTime.UtcNow.AddHours(1);
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.UpdateUserAsync(user);
        }

        public async Task PasswordChangeAsync(User user, string newPassword)
        {
            user.PasswordHash = _authenticationService.HashPassword(newPassword);
            user.PasswordResetToken = null;
            user.PasswordResetExpires = null;
            user.LastChangedPasswordAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.UpdateUserAsync(user);
        }
    }
}