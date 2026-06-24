using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Net.Http.Json;

namespace LifeAlertPlus.Client.Pages.Login
{
    // Code-behind pentru pagina de callback OAuth (Google) — primește codul de autorizare din query string,
    // îl schimbă pe un JWT propriu prin API și redirecționează utilizatorul spre consimțământ sau dashboard
    public partial class AuthCallbackPage : ComponentBase
    {
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private HttpClient Http { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        private string _message = "Authenticating...";

        protected override async Task OnInitializedAsync()
        {
            // Extrage "code" și "returnUrl" din query string-ul URL-ului de callback
            var uri = new Uri(Navigation.Uri);
            string? code      = null;
            string? returnUrl = null;

            foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2) continue;

                var key = kv[0];
                var val = Uri.UnescapeDataString(kv[1]);

                if (key.Equals("code",      StringComparison.OrdinalIgnoreCase)) code      = val;
                if (key.Equals("returnUrl", StringComparison.OrdinalIgnoreCase)) returnUrl = val;
            }

            // Dacă server-ul a transmis un parametru `error` în URL-ul de callback, îl propagă spre /login
            // ca utilizatorul să vadă un mesaj concret în loc de o redirecționare silențioasă
            string? serverError = null;
            foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals("error", StringComparison.OrdinalIgnoreCase))
                    serverError = Uri.UnescapeDataString(kv[1]);
            }
            if (!string.IsNullOrWhiteSpace(serverError))
            {
                _message = $"Authentication failed: {serverError}";
                Navigation.NavigateTo($"/login?error={Uri.EscapeDataString(serverError)}");
                return;
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                _message = "Authentication failed: missing authorization code.";
                Navigation.NavigateTo("/login?error=GoogleAuthFailed");
                return;
            }

            // Validează returnUrl — acceptă doar căi locale relative (previne open-redirect)
            if (string.IsNullOrWhiteSpace(returnUrl) ||
                !returnUrl.StartsWith('/') ||
                returnUrl.StartsWith("//"))
            {
                returnUrl = "/dashboard";
            }

            try
            {
                // Schimbă codul de autorizare Google pe un JWT propriu, emis de API
                var response = await Http.GetFromJsonAsync<TokenResponse>($"api/auth/exchange-token?code={Uri.EscapeDataString(code)}");
                if (response?.Token is not null)
                {
                    await JSRuntime.InvokeVoidAsync("sessionStorage.setItem", "authToken", response.Token);

                    // GDPR: utilizatorii care se autentifică prima dată cu Google nu au dat încă consimțământul explicit
                    var needsConsent = false;
                    try
                    {
                        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                        var jwt = handler.ReadJwtToken(response.Token);
                        var val = jwt.Claims.FirstOrDefault(c => c.Type == "needsConsent")?.Value;
                        needsConsent = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                    }
                    catch { /* best-effort; dacă parsarea JWT-ului eșuează, se continuă spre returnUrl */ }

                    Navigation.NavigateTo(needsConsent
                        ? $"/consent?returnUrl={Uri.EscapeDataString(returnUrl)}"
                        : returnUrl);
                }
                else
                {
                    _message = "Authentication failed: the server did not return a token.";
                    Navigation.NavigateTo("/login?error=GoogleExchangeFailed");
                }
            }
            catch (HttpRequestException ex)
            {
                _message = $"Authentication failed: {ex.Message}";
                Navigation.NavigateTo("/login?error=GoogleExchangeFailed");
            }
            catch (Exception ex)
            {
                _message = $"Authentication failed: {ex.Message}";
                Navigation.NavigateTo("/login?error=GoogleExchangeFailed");
            }
        }

        private sealed record TokenResponse(string Token);
    }
}
