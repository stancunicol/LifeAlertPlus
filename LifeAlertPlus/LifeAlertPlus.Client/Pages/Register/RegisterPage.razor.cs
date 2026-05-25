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
        private AuthApiClient AuthApiClient { get; set; } = default!;

        [Inject]
        private LanguageService Lang { get; set; } = default!;

        private string T(string key) => Lang.TEnglish(key);

        private string FirstName { get; set; } = string.Empty;
        private string LastName { get; set; } = string.Empty;
        private string Email { get; set; } = string.Empty;
        private string PhoneNumber { get; set; } = string.Empty;
        private string Password { get; set; } = string.Empty;
        private string ConfirmPassword { get; set; } = string.Empty;
        private string Version { get; set; } = string.Empty;
        private string ErrorMessage { get; set; } = string.Empty;
        private string SuccessMessage { get; set; } = string.Empty;
        private bool ShowModal { get; set; } = false;
        private bool IsLoading { get; set; } = false;

        protected override Task OnInitializedAsync()
        {
            Version = AppVersion.Version;
            return Task.CompletedTask;
        }

        private async Task OnRegister()
        {
            if (IsLoading) return;
            
            IsLoading = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            try
            {
                if(string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName) ||
                   string.IsNullOrWhiteSpace(Email) ||
                   string.IsNullOrWhiteSpace(Password) || string.IsNullOrWhiteSpace(ConfirmPassword))
                {
                    ErrorMessage = T("register.error.allRequired");
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
                    ErrorMessage = T("register.error.passwordMismatch");
                    return;
                }

                var request = new Shared.DTOs.Requests.User.UserRegisterRequestDTO
                {
                    FirstName = FirstName,
                    LastName = LastName,
                    Email = Email,
                    PhoneNumber = string.IsNullOrWhiteSpace(PhoneNumber) ? null : PhoneNumber.Trim(),
                    Password = Password
                };

                var response = await AuthApiClient.RegisterAsync(request);

                if (response != null && response.Success)
                {
                    ShowModal = true;
                }
                else
                {
                    ErrorMessage = MapApiError(response?.Message);
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void CloseModal()
        {
            ShowModal = false;
            Navigation.NavigateTo("/login");
        }

        private async Task HandleKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
            {
                await OnRegister();
            }
        }

        private string MapApiError(string? apiMessage) => apiMessage switch
        {
            "An account with this email address already exists." => T("register.error.emailInUse"),
            "This phone number is already associated with another account." => T("register.error.phoneInUse"),
            _ => apiMessage ?? T("register.error.failed")
        };

        private (bool IsValid, string ErrorMessage) ValidatePassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return (false, T("password.required"));
            }

            if (password.Length < 8)
            {
                return (false, T("password.minLength"));
            }

            if (!password.Any(char.IsLower))
            {
                return (false, T("password.lowercase"));
            }

            if (!password.Any(char.IsUpper))
            {
                return (false, T("password.uppercase"));
            }

            if (!password.Any(char.IsDigit))
            {
                return (false, T("password.number"));
            }

            if(!password.Any(ch => !char.IsLetterOrDigit(ch)))
            {
                return (false, T("password.special"));
            }

            return (true, string.Empty);
        }
    }
}
