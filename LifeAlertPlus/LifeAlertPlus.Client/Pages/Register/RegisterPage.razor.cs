using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;

namespace LifeAlertPlus.Client.Pages.Register
{
    public partial class RegisterPage : ComponentBase
    {
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
        // Consent modal state
        private bool ShowConsentModal { get; set; } = false;
        private bool ConsentChecked { get; set; } = false;

        // 0=empty, 1=weak, 2=medium, 3=strong, 4=very strong
        private int PasswordStrength => ComputeStrength(Password);

        private static int ComputeStrength(string pw)
        {
            if (string.IsNullOrEmpty(pw)) return 0;
            int score = 0;
            if (pw.Length >= 8)  score++;
            if (pw.Length >= 12) score++;
            if (pw.Any(char.IsUpper) && pw.Any(char.IsLower)) score++;
            if (pw.Any(char.IsDigit)) score++;
            if (pw.Any(ch => !char.IsLetterOrDigit(ch))) score++;
            return Math.Min(score, 4);
        }

        private string PasswordStrengthLabel => PasswordStrength switch
        {
            1 => T("password.strengthWeak"),
            2 => T("password.strengthFair"),
            3 => T("password.strengthGood"),
            4 => T("password.strengthStrong"),
            _ => string.Empty
        };

        private string PasswordStrengthClass => PasswordStrength switch
        {
            1 => "strength-weak",
            2 => "strength-fair",
            3 => "strength-good",
            4 => "strength-strong",
            _ => string.Empty
        };

        protected override Task OnInitializedAsync()
        {
            Version = AppVersion.Version;
            return Task.CompletedTask;
        }

        // Step 1 — validate form fields and open the consent modal.
        private void OnRegister()
        {
            if (IsLoading) return;

            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName) ||
                string.IsNullOrWhiteSpace(Email) ||
                string.IsNullOrWhiteSpace(Password) || string.IsNullOrWhiteSpace(ConfirmPassword))
            {
                ErrorMessage = T("register.error.allRequired");
                return;
            }

            if (!IsValidEmail(Email.Trim()))
            {
                ErrorMessage = T("register.error.invalidEmail");
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

            // All field-level validation passed — show consent modal.
            ConsentChecked = false;
            ShowConsentModal = true;
        }

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email && email.Contains('.') && !email.EndsWith('.');
            }
            catch { return false; }
        }

        // Called when the user dismisses the consent modal without agreeing.
        private void CloseConsentModal()
        {
            ShowConsentModal = false;
            ErrorMessage = T("register.error.consentRequired");
        }

        // Step 2 — user accepted consent, submit registration.
        private async Task ConfirmConsentAndRegister()
        {
            if (!ConsentChecked)
            {
                ErrorMessage = T("register.error.consentRequired");
                ShowConsentModal = false;
                return;
            }

            ShowConsentModal = false;
            IsLoading = true;
            ErrorMessage = string.Empty;

            try
            {
                var request = new Shared.DTOs.Requests.User.UserRegisterRequestDTO
                {
                    FirstName = FirstName,
                    LastName = LastName,
                    Email = Email,
                    PhoneNumber = string.IsNullOrWhiteSpace(PhoneNumber) ? null : PhoneNumber.Trim(),
                    Password = Password,
                    DataProcessingConsent = true
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

        private void HandleKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
                OnRegister();
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
