using Microsoft.AspNetCore.Components;
using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Client.Services;

namespace LifeAlertPlus.Client.Pages.ResetPassword
{
    public partial class ResetPasswordPage : ComponentBase
    {
        [Inject]
        private HttpClient Http { get; set; } = default!;

        [Inject]
        private NavigationManager Navigation { get; set; } = default!;

        [Inject]
        private LanguageService Lang { get; set; } = default!;

        private string T(string key) => Lang.TEnglish(key);

        private string Password { get; set; } = string.Empty;
        private string ConfirmPassword { get; set; } = string.Empty;
        private bool _showPassword = false;
        private bool _showConfirmPassword = false;
        private string Version { get; set; } = string.Empty;

        private void ToggleShowPassword() => _showPassword = !_showPassword;
        private void ToggleShowConfirmPassword() => _showConfirmPassword = !_showConfirmPassword;

        // Password strength (0=empty, 1=weak, 2=fair, 3=good, 4=strong)
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
        private string ErrorMessage { get; set; } = string.Empty;
        private string SuccessMessage { get; set; } = string.Empty;
        private string ResetToken { get; set; } = string.Empty;

        protected override async Task OnInitializedAsync()
        {
            var uri = new Uri(Navigation.Uri);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            ResetToken = query["token"] ?? string.Empty;

            if (string.IsNullOrEmpty(ResetToken))
            {
                ErrorMessage = T("reset.error.invalidLink");
            }

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

        private async Task OnResetPassword()
        {
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            var passwordValidation = ValidatePassword(Password);
            if (!passwordValidation.IsValid)
            {
                ErrorMessage = passwordValidation.ErrorMessage;
                return;
            }

            if (Password != ConfirmPassword)
            {
                ErrorMessage = T("reset.error.passwordMismatch");
                return;
            }

            try
            {
                var request = new UserResetPasswordRequestDTO
                {
                    Token = ResetToken,
                    NewPassword = Password,
                    ConfirmPassword = ConfirmPassword
                };

                var response = await Http.PostAsJsonAsync("api/authentication/reset-password", request);

                if (response.IsSuccessStatusCode)
                {
                    SuccessMessage = T("reset.error.success");
                    await Task.Delay(2000);
                    Navigation.NavigateTo("/login");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    ErrorMessage = error.Contains("expired") 
                        ? T("reset.error.expired") 
                        : T("reset.error.failed");
                }
            }
            catch (Exception)
            {
                ErrorMessage = T("reset.error.generic");
            }
        }

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

            var specialCharacters = "!#$%^&*-_./\\";
            if (!password.Any(c => specialCharacters.Contains(c)))
            {
                return (false, T("password.special"));
            }

            return (true, string.Empty);
        }
    }
}