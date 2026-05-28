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
        private AuthApiClient AuthApiClient { get; set; } = default!;
        
        [Inject]
        private IJSRuntime JSRuntime { get; set; } = default!;

        [Inject]
        private IConfiguration Configuration { get; set; } = default!;

        [Inject]
        private LanguageService Lang { get; set; } = default!;

        private string T(string key) => Lang.TEnglish(key);
        
        private string Email { get; set; } = string.Empty;
        private string Password { get; set; } = string.Empty;
        private string Version { get; set; } = string.Empty;
        private string ErrorMessage { get; set; } = string.Empty;
        private bool ShowForgotPasswordModal { get; set; } = false;
        private string ForgotPasswordEmail { get; set; } = string.Empty;
        private string ForgotPasswordMessage { get; set; } = string.Empty;
        private bool IsForgotPasswordSuccess { get; set; } = false;
        private bool IsLoading { get; set; } = false;

        private string? _returnUrl;

        protected override Task OnInitializedAsync()
        {
            Version = AppVersion.Version;
            _returnUrl = GetQueryParam("returnUrl");

            // Surface Google OAuth / callback failures so the user understands why they
            // landed back on the login page instead of the dashboard. Without this, every
            // OAuth failure looks like a silent redirect.
            var oauthError = GetQueryParam("error");
            if (!string.IsNullOrWhiteSpace(oauthError))
            {
                ErrorMessage = oauthError switch
                {
                    "GoogleAuthFailed"        => T("login.error.googleAuthFailed"),
                    "GoogleAuthNoEmail"       => T("login.error.googleNoEmail"),
                    "GoogleUserCreateFailed"  => T("login.error.googleUserCreate"),
                    "GoogleExchangeFailed"    => T("login.error.googleExchange"),
                    "GoogleEmailConflict"     => T("login.error.googleEmailConflict"),
                    _                          => string.Format(T("login.error.googleGeneric"), oauthError)
                };
            }

            return Task.CompletedTask;
        }

        private async Task OnLogin()
        {
            if (IsLoading) return;
            
            IsLoading = true;
            ErrorMessage = string.Empty;

            try
            {
                var request = new Shared.DTOs.Requests.User.UserLoginRequestDTO
                {
                    Email = Email,
                    Password = Password
                };

                var response = await AuthApiClient.LoginAsync(request);
                
                if(response != null)
                {
                    if(response.Success)
                    {
                        await JSRuntime.InvokeVoidAsync("sessionStorage.setItem", "authToken", response.Token);

                        var safeReturn = NormalizeSafeReturnUrl(_returnUrl);
                        if (!string.IsNullOrWhiteSpace(safeReturn))
                        {
                            Navigation.NavigateTo(safeReturn, forceLoad: true);
                            return;
                        }

                        if(response.IsAdmin)
                        {
                            Navigation.NavigateTo("/admin-dashboard");
                            return;
                        }
						
                        Navigation.NavigateTo("/dashboard");
                    }

                    else
                    {
                        ErrorMessage = MapLoginError(response.Message);
                    }
                }
                else
                {
                    ErrorMessage = T("login.error.generic");
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        private string MapLoginError(string? apiMessage) => apiMessage switch
        {
            "No account found with this email address." => T("login.error.noAccount"),
            "Incorrect password." => T("login.error.wrongPassword"),
            "Please verify your email before logging in." => T("login.error.emailNotConfirmed"),
            _ => apiMessage ?? T("login.error.failed")
        };

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
                ForgotPasswordMessage = T("login.error.enterEmail");
                IsForgotPasswordSuccess = false;
                return;
            }

            if (!ForgotPasswordEmail.Contains("@"))
            {
                ForgotPasswordMessage = T("login.error.validEmail");
                IsForgotPasswordSuccess = false;
                return;
            }

            try
            {
                var response = await Http.PostAsJsonAsync("api/authentication/forgot-password", new { Email = ForgotPasswordEmail });
                var content = await response.Content.ReadAsStringAsync();
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

                ForgotPasswordMessage = message ?? T("login.error.resetFailed");
                IsForgotPasswordSuccess = success == true;
            }
            catch
            {
                ForgotPasswordMessage = T("login.error.resetGeneric");
                IsForgotPasswordSuccess = false;
            }
        }

        private void LoginWithGoogle()
        {
            var apiBaseUrl = (Configuration["ApiBaseUrl"] ?? Navigation.BaseUri).TrimEnd('/');
            var clientBaseUrl = Navigation.BaseUri.TrimEnd('/');

            var safeReturn = NormalizeSafeReturnUrl(_returnUrl);
            var clientReturnUrl = string.IsNullOrWhiteSpace(safeReturn)
                ? $"{clientBaseUrl}/dashboard"
                : $"{clientBaseUrl}{safeReturn}";

            var googleAuthUrl = $"{apiBaseUrl}/api/auth/google-login?returnUrl={Uri.EscapeDataString(clientReturnUrl)}";
            Navigation.NavigateTo(googleAuthUrl, forceLoad: true);
        }

        private static string? NormalizeSafeReturnUrl(string? returnUrl)
        {
            if (string.IsNullOrWhiteSpace(returnUrl))
                return null;

            // Only allow local relative paths.
            if (!returnUrl.StartsWith("/", StringComparison.Ordinal))
                return null;

            if (returnUrl.StartsWith("//", StringComparison.Ordinal))
                return null;

            if (returnUrl.Contains("://", StringComparison.OrdinalIgnoreCase))
                return null;

            return returnUrl;
        }

        private string? GetQueryParam(string name)
        {
            try
            {
                var uri = new Uri(Navigation.Uri);
                var query = uri.Query;
                if (string.IsNullOrWhiteSpace(query)) return null;

                if (query.StartsWith("?")) query = query[1..];
                var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 0) continue;
                    var key = Uri.UnescapeDataString(kv[0]);
                    if (!key.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                    return kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
