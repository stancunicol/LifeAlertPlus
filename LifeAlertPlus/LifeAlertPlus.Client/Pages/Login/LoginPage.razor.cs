using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Net.Http.Json;
using LifeAlertPlus.Client.Services;

namespace LifeAlertPlus.Client.Pages.Login
{
    public partial class LoginPage : ComponentBase
    {
        [Inject]
        private HttpClient Http { get; set; } = default!;

        [Inject]
        private NavigationManager Navigation { get; set; } = default!;

        [Inject]
        private AuthentificationService AuthentificationService { get; set; } = default!;
        
        [Inject]
        private IJSRuntime JSRuntime { get; set; } = default!;
        
        private string Email { get; set; } = string.Empty;
        private string Password { get; set; } = string.Empty;
        private bool _showPassword = false;
        private string Version { get; set; } = string.Empty;
        private string ErrorMessage { get; set; } = string.Empty;
        private bool ShowForgotPasswordModal { get; set; } = false;
        private string ForgotPasswordEmail { get; set; } = string.Empty;
        private string ForgotPasswordMessage { get; set; } = string.Empty;
        private bool IsForgotPasswordSuccess { get; set; } = false;

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

        private async Task OnLogin()
        {
            ErrorMessage = string.Empty;

            var request = new Shared.DTOs.Requests.User.UserLoginRequestDTO
            {
                Email = Email,
                Password = Password
            };

            var response = await AuthentificationService.LoginAsync(request);
            
            if(response != null)
            {
                if(response.Success == true)
                {
                    // Salvez token-ul în localStorage
                    await JSRuntime.InvokeVoidAsync("localStorage.setItem", "authToken", response.Token);
                    
                    Navigation.NavigateTo("/dashboard");
                    Console.WriteLine("Login successful.");
                }
                else
                {
                    ErrorMessage = response.Message ?? "Login failed.";
                    Console.WriteLine("Login failed: " + ErrorMessage);
                }
            }
            else
            {
                ErrorMessage = "An error occurred during login.";
                Console.WriteLine("Login error: No response from server.");
            }
        }

        private void OpenForgotPasswordModal()
        {
            ShowForgotPasswordModal = true;
            ForgotPasswordEmail = string.Empty;
            ForgotPasswordMessage = string.Empty;
            IsForgotPasswordSuccess = false;
        }

        private void CloseForgotPasswordModal()
        {
            ShowForgotPasswordModal = false;
        }

        private async Task OnSendResetEmail()
        {
            ForgotPasswordMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(ForgotPasswordEmail))
            {
                ForgotPasswordMessage = "Please enter your email address.";
                IsForgotPasswordSuccess = false;
                return;
            }

            if (!ForgotPasswordEmail.Contains("@"))
            {
                ForgotPasswordMessage = "Please enter a valid email address.";
                IsForgotPasswordSuccess = false;
                return;
            }

            try
            {
                var response = await Http.PostAsJsonAsync("api/authentification/forgot-password", new { Email = ForgotPasswordEmail });

                if (response.IsSuccessStatusCode)
                {
                    ForgotPasswordMessage = "A password reset link has been sent to your email.";
                    IsForgotPasswordSuccess = true;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    ForgotPasswordMessage = error.Contains("not found") 
                        ? "No account found with this email address." 
                        : "Failed to send reset email. Please try again.";
                    IsForgotPasswordSuccess = false;
                }
            }
            catch (Exception ex)
            {
                ForgotPasswordMessage = "An error occurred. Please try again later.";
                IsForgotPasswordSuccess = false;
                Console.WriteLine($"Forgot password error: {ex.Message}");
            }
        }

        private void LoginWithGoogle()
        {
            // Adresa completă a API-ului (modifică portul dacă e altul la API)
            var apiBaseUrl = "http://localhost:5176";
            var clientDashboardUrl = "http://localhost:5254/dashboard";
            var googleAuthUrl = $"{apiBaseUrl}/api/auth/google-login?returnUrl={Uri.EscapeDataString(clientDashboardUrl)}";
            Navigation.NavigateTo(googleAuthUrl, forceLoad: true);
        }
    }
}
