using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Net.Http.Json;

namespace LifeAlertPlus.Client.Pages.Login
{
    public partial class AuthCallbackPage : ComponentBase
    {
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private HttpClient Http { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        private string _message = "Authenticating...";

        protected override async Task OnInitializedAsync()
        {
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

            if (string.IsNullOrWhiteSpace(code))
            {
                _message = "Authentication failed. Invalid callback.";
                await Task.Delay(2000);
                Navigation.NavigateTo("/login");
                return;
            }

            // Validate returnUrl — only allow local relative paths.
            if (string.IsNullOrWhiteSpace(returnUrl) ||
                !returnUrl.StartsWith('/') ||
                returnUrl.StartsWith("//"))
            {
                returnUrl = "/dashboard";
            }

            try
            {
                var response = await Http.GetFromJsonAsync<TokenResponse>($"api/auth/exchange-token?code={Uri.EscapeDataString(code)}");
                if (response?.Token is not null)
                {
                    await JSRuntime.InvokeVoidAsync("sessionStorage.setItem", "authToken", response.Token);
                    Navigation.NavigateTo(returnUrl);
                }
                else
                {
                    _message = "Authentication failed. Please try again.";
                    await Task.Delay(2000);
                    Navigation.NavigateTo("/login");
                }
            }
            catch
            {
                _message = "Authentication failed. Please try again.";
                await Task.Delay(2000);
                Navigation.NavigateTo("/login");
            }
        }

        private sealed record TokenResponse(string Token);
    }
}
