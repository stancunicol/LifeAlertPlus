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
        private AuthenticationService AuthenticationService { get; set; } = default!;
        
        [Inject]
        private IJSRuntime JSRuntime { get; set; } = default!;

        [Inject]
        private IConfiguration Configuration { get; set; } = default!;
        
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

            var response = await AuthenticationService.LoginAsync(request);
            
            if(response != null)
            {
                if(response.Success)
                {
                    if(response.IsAdmin)
                    {
                        await JSRuntime.InvokeVoidAsync("sessionStorage.setItem", "authToken", response.Token);
						
                        Navigation.NavigateTo("/admin-dashboard");
                        return;
                    }
					
                    await JSRuntime.InvokeVoidAsync("sessionStorage.setItem", "authToken", response.Token);
					
                    Navigation.NavigateTo("/dashboard");
                }
                else
                {
                    ErrorMessage = response.Message ?? "Login failed.";
                }
            }
            else
            {
                ErrorMessage = "An error occurred during login.";
            }
        }

        private async Task HandleKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
            {
                await OnLogin();
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
                var response = await Http.PostAsJsonAsync("api/authentication/forgot-password", new { Email = ForgotPasswordEmail });
                var content = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[ForgotPassword API raw response]: {content}");
                string? message = null;
                bool? success = false;
                try
                {
                    var json = System.Text.Json.JsonDocument.Parse(content);
                    if (json.RootElement.TryGetProperty("message", out var msgProp))
                        message = msgProp.GetString();
                    if (json.RootElement.TryGetProperty("success", out var succProp))
                        success = succProp.GetBoolean();
                }
                catch { message = content; }

                ForgotPasswordMessage = message ?? "Failed to send reset email. Please try again.";
                IsForgotPasswordSuccess = success == true;
            }
            catch
            {
                ForgotPasswordMessage = "An error occurred. Please try again later.";
                IsForgotPasswordSuccess = false;
            }
        }

        private void LoginWithGoogle()
        {
            var apiBaseUrl = (Configuration["ApiBaseUrl"] ?? "http://localhost:5176").TrimEnd('/');
            var clientBaseUrl = Navigation.BaseUri.TrimEnd('/');
            var clientDashboardUrl = $"{clientBaseUrl}/dashboard";
            var googleAuthUrl = $"{apiBaseUrl}/api/auth/google-login?returnUrl={Uri.EscapeDataString(clientDashboardUrl)}";
            Navigation.NavigateTo(googleAuthUrl, forceLoad: true);
        }
    }
}
