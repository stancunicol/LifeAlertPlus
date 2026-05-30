using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;
using System.Net.Http.Json;

namespace LifeAlertPlus.Client.Pages.Consent
{
    public partial class ConsentPage : ComponentBase
    {
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private HttpClient Http { get; set; } = default!;
        [Inject] private TokenParserService TokenParser { get; set; } = default!;
        [Inject] private AuthApiClient AuthApiClient { get; set; } = default!;
        [Inject] private LanguageService Lang { get; set; } = default!;

        private string T(string key) => Lang.T(key);

        private bool ConsentChecked { get; set; } = false;
        private bool IsLoading { get; set; } = false;
        private string ErrorMessage { get; set; } = string.Empty;
        private string ReturnUrl { get; set; } = "/dashboard";

        protected override async Task OnInitializedAsync()
        {
            // Parse returnUrl from query string.
            var uri = new Uri(Navigation.Uri);
            foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals("returnUrl", StringComparison.OrdinalIgnoreCase))
                {
                    var url = Uri.UnescapeDataString(kv[1]);
                    if (url.StartsWith('/') && !url.StartsWith("//"))
                        ReturnUrl = url;
                }
            }

            // If user already consented (e.g. navigated here directly), skip.
            var claims = await TokenParser.GetClaimsAsync();
            if (claims != null && !claims.NeedsConsent)
                Navigation.NavigateTo(ReturnUrl);
        }

        private async Task AcceptConsent()
        {
            if (!ConsentChecked) return;
            IsLoading = true;
            ErrorMessage = string.Empty;
            try
            {
                var claims = await TokenParser.GetClaimsAsync();
                if (claims == null)
                {
                    Navigation.NavigateTo("/login");
                    return;
                }

                var resp = await Http.PostAsync($"api/user/{claims.UserId}/consent", null);
                if (!resp.IsSuccessStatusCode)
                {
                    ErrorMessage = T("consent.saveFailed");
                    return;
                }

                // Re-issue token so needsConsent disappears from subsequent calls.
                // Simplest: re-login via the existing session (token is still valid).
                // The server returns a new JWT only when we call a token-issuing endpoint.
                // For now navigate directly — the claim disappears on next token renewal.
                Navigation.NavigateTo(ReturnUrl);
            }
            catch
            {
                ErrorMessage = T("consent.saveFailed");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task DeclineAndLogout()
        {
            await AuthApiClient.LogoutAsync();
            Navigation.NavigateTo("/login?error=ConsentDeclined");
        }
    }
}
