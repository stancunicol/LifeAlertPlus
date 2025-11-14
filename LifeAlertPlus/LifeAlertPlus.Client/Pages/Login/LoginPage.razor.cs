using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.Login
{
    public partial class LoginPage : ComponentBase
    {
        [Microsoft.AspNetCore.Components.Inject]
        private System.Net.Http.HttpClient Http { get; set; }

        [Microsoft.AspNetCore.Components.Inject]
        private Microsoft.AspNetCore.Components.NavigationManager Navigation { get; set; } = default!;
        private string Email { get; set; } = string.Empty;
        private string Password { get; set; } = string.Empty;
        private bool _showPassword = false;
        private string Version { get; set; } = string.Empty;

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

        private void OnLogin()
        {

        }
    }
}
