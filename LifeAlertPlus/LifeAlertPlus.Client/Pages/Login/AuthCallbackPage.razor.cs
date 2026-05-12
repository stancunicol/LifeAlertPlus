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
            var query = uri.Query;
            string? code = null;

            if (!string.IsNullOrWhiteSpace(query))
            {
                foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 2 && kv[0].Equals("code", StringComparison.OrdinalIgnoreCase))
                    {
                        code = Uri.UnescapeDataString(kv[1]);
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                _message = "Authentication failed. Invalid callback.";
                await Task.Delay(2000);
                Navigation.NavigateTo("/login");
                return;
            }

            try
            {
                var response = await Http.GetFromJsonAsync<TokenResponse>($"api/auth/exchange-token?code={Uri.EscapeDataString(code)}");
                if (response?.Token is not null)
                {
                    await JSRuntime.InvokeVoidAsync("sessionStorage.setItem", "authToken", response.Token);
                    Navigation.NavigateTo("/dashboard");
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
