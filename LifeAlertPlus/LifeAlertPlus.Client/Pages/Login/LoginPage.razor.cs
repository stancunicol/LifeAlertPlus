using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.Login
{
    public partial class LoginPage : ComponentBase
    {
        private string Email { get; set; } = string.Empty;
        private string Password { get; set; } = string.Empty;
        private bool _showPassword = false;
        private string Version { get; set; }

        protected override void OnInitialized()
        {
            Version = AppVersion.Version;
        }

        private void OnLogin()
        {

        }
    }
}
