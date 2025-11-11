using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.Login
{
    public partial class LoginPage : ComponentBase
    {
        private string Email { get; set; } = string.Empty;
        private string Password { get; set; } = string.Empty;
        private bool _showPassword = false;
        private string AppVersion { get; set; }

        private void OnLogin()
        {
            
        }
    }
}
