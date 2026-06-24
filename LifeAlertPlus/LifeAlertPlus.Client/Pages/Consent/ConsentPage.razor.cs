using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;
using System.Net.Http.Json;

namespace LifeAlertPlus.Client.Pages.Consent
{
    // Code-behind pentru pagina de Consimțământ GDPR — afișată la primul login dacă utilizatorul
    // nu a acceptat încă procesarea datelor; altfel utilizatorul nu poate continua spre dashboard
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
            // Extrage parametrul returnUrl din query string, ca să știe unde să redirecționeze după acceptare
            var uri = new Uri(Navigation.Uri);
            foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals("returnUrl", StringComparison.OrdinalIgnoreCase))
                {
                    var url = Uri.UnescapeDataString(kv[1]);
                    // Acceptă doar URL-uri relative locale (previne open-redirect către "//evil.com")
                    if (url.StartsWith('/') && !url.StartsWith("//"))
                        ReturnUrl = url;
                }
            }

            // Dacă utilizatorul a acceptat deja consimțământul (ex: a navigat direct pe pagină), trece direct
            var claims = await TokenParser.GetClaimsAsync();
            if (claims != null && !claims.NeedsConsent)
                Navigation.NavigateTo(ReturnUrl);
        }

        // Trimite acceptul de consimțământ la server și continuă navigarea către pagina inițial cerută
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

                // Token-ul JWT curent încă are claim-ul NeedsConsent=true — server-ul emite un token
                // nou doar la următoarea autentificare/reînnoire. Pentru simplitate navigăm direct;
                // claim-ul vechi va dispărea automat la următoarea reînnoire a token-ului.
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

        // Utilizatorul refuză consimțământul — nu poate folosi aplicația fără el, deci este delogat
        private async Task DeclineAndLogout()
        {
            await AuthApiClient.LogoutAsync();
            Navigation.NavigateTo("/login?error=ConsentDeclined");
        }
    }
}
