using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;

namespace LifeAlertPlus.Client.Pages.Register
{
    public partial class RegisterPage : ComponentBase
    {
        [Inject]
        private HttpClient Http { get; set; } = default!;

        [Inject]
        private NavigationManager Navigation { get; set; } = default!;

        [Inject]
        private AuthentificationService AuthentificationService { get; set; } = default!;

        private string FirstName { get; set; } = string.Empty;
        private string LastName { get; set; } = string.Empty;
        private string Email { get; set; } = string.Empty;
        private string Password { get; set; } = string.Empty;
        private string ConfirmPassword { get; set; } = string.Empty;
        private bool _showPassword = false;
        private string Version { get; set; } = string.Empty;
        private string ErrorMessage { get; set; } = string.Empty;
        private string SuccessMessage { get; set; } = string.Empty;
        private bool ShowModal { get; set; } = false;

        protected override async Task OnInitializedAsync()
        {
            try
            {
                var url = Navigation.BaseUri + "VERSION";
                var v = await Http.GetStringAsync(url);
                Version = (v ?? string.Empty).Trim();

                if (string.IsNullOrEmpty(Version))
                {
                    Version = AppVersion.Version;
                }
            }
            catch
            {
                Version = AppVersion.Version;
            }
        }

        private async Task OnRegister()
        {
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            if(string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName) ||
               string.IsNullOrWhiteSpace(Email) ||
               string.IsNullOrWhiteSpace(Password) || string.IsNullOrWhiteSpace(ConfirmPassword))
            {
                ErrorMessage = "All fields are required.";
                return;
            }
                    
            var passwordValidation = ValidatePassword(Password);
            if (!passwordValidation.IsValid)
            {
                ErrorMessage = passwordValidation.ErrorMessage;
                return;
            }

            if (Password != ConfirmPassword)
            {
                ErrorMessage = "Passwords do not match.";
                return;
            }

            var request = new Shared.DTOs.Requests.User.UserRegisterRequestDTO
            {
                FirstName = FirstName,
                LastName = LastName,
                Email = Email,
                Password = Password
            };

            var response = await AuthentificationService.RegisterAsync(request);

            if (response != null)
            {
                ShowModal = true;
            }
            else
            {
                ErrorMessage = "Registration failed. Please try again.";
            }
        }

        private void CloseModal()
        {
            ShowModal = false;
            Navigation.NavigateTo("/login");
        }

        private (bool IsValid, string ErrorMessage) ValidatePassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return (false, "Password is required.");
            }

            if (password.Length < 8)
            {
                return (false, "Password must be at least 8 characters long.");
            }

            if (!password.Any(char.IsLower))
            {
                return (false, "Password must contain at least one lowercase letter.");
            }

            if (!password.Any(char.IsUpper))
            {
                return (false, "Password must contain at least one uppercase letter.");
            }

            if (!password.Any(char.IsDigit))
            {
                return (false, "Password must contain at least one number.");
            }

            var specialCharacters = "!#$%^&*-_./\\";
            if (!password.Any(c => specialCharacters.Contains(c)))
            {
                return (false, "Password must contain at least one special character (!#$%^&*-_./\\).");
            }

            return (true, string.Empty);
        }
    }
}
