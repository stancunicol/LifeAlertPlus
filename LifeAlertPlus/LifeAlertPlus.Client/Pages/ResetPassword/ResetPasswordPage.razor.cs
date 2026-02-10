using Microsoft.AspNetCore.Components;
using System.Net.Http.Json;
using LifeAlertPlus.Shared.DTOs.Requests.User;

namespace LifeAlertPlus.Client.Pages.ResetPassword
{
    public partial class ResetPasswordPage : ComponentBase
    {
        [Inject]
        private HttpClient Http { get; set; } = default!;

        [Inject]
        private NavigationManager Navigation { get; set; } = default!;

        private string Password { get; set; } = string.Empty;
        private string ConfirmPassword { get; set; } = string.Empty;
        private bool _showPassword = false;
        private string Version { get; set; } = string.Empty;
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
                ErrorMessage = "Invalid reset link. Please request a new password reset.";
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
                ErrorMessage = "Passwords do not match.";
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
                    SuccessMessage = "Password reset successful! Redirecting to login...";
                    await Task.Delay(2000);
                    Navigation.NavigateTo("/login");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    ErrorMessage = error.Contains("expired") 
                        ? "Reset link has expired. Please request a new password reset." 
                        : "Failed to reset password. Please try again.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "An error occurred. Please try again later.";
                Console.WriteLine($"Reset password error: {ex.Message}");
            }
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