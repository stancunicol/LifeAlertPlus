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

        protected override async System.Threading.Tasks.Task OnInitializedAsync()
        {
            try
            {
                // Fetch VERSION from the client application's wwwroot (same origin)
                // Use Navigation.BaseUri to avoid the HttpClient BaseAddress pointing to the API
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
                // Fallback to assembly attribute
                Version = AppVersion.Version;
            }
        }

        private void OnLogin()
        {

        }
    }
}
