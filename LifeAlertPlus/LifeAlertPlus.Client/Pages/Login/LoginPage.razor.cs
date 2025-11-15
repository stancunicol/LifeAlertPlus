using LifeAlertPlus.Client.Services;
using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Shared.DTOs.Requests.User;

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

        private string Email { get; set; } = string.Empty;
        private string Password { get; set; } = string.Empty;
        private bool _showPassword = false;
        private string Version { get; set; } = string.Empty;
        private string LoginMessage { get; set; } = string.Empty;

        protected override async Task OnInitializedAsync()
        {
            try
            {
                var url = Navigation.BaseUri + "VERSION";
                var v = await Http.GetStringAsync(url);
                Version = (v ?? string.Empty).Trim();

                if (string.IsNullOrEmpty(Version))
                {
                    Version = "unknown";
                }
            }
            catch
            {
                Version = "unknown";
            }
        }

        private async void OnLogin()
        {
            LoginMessage = string.Empty;

            var request = new UserLoginRequestDTO
            {
                Email = Email,
                Password = Password
            };

            var loginResult = await AuthentificationService.LoginAsync(request);

            if (loginResult != null)
            {
                if (loginResult.Success)
                {
                    Navigation.NavigateTo("/");
                }
                else
                {
                    LoginMessage = "Login failed: " + loginResult.Message;
                }
            }
            else
            {
                LoginMessage = "Login failed: No response from server.";
            }
        }
    }
}
