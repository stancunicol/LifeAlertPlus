using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;

namespace LifeAlertPlus.Client.Components.HeaderAdmin
{
    public partial class HeaderAdminComponent : ComponentBase, IDisposable
    {
        [Inject]
        private NavigationManager Navigation { get; set; } = default!;

        [Parameter]
        public string UserName { get; set; } = "Guest User";

        [Parameter]
        public string? ProfilePictureUrl { get; set; }

        [Parameter]
        public EventCallback OnLogoutClick { get; set; }

        [Inject]
        private HttpClient Http { get; set; } = default!;

        [Inject]
        private ProfilePictureService ProfilePictureService { get; set; } = default!;

        private bool ShowProfileMenu { get; set; } = false;
        private bool ShowMobileMenu { get; set; } = false;
        private string Version { get; set; } = string.Empty;
        protected override Task OnInitializedAsync()
        {
            Version = AppVersion.Version;
            if (ProfilePictureService != null)
            {
                ProfilePictureService.OnChange += HandleProfilePictureChanged;
                if (!string.IsNullOrEmpty(ProfilePictureService.Url))
                    ProfilePictureUrl = ProfilePictureService.Url;
            }
            return Task.CompletedTask;
        }

        private async void HandleProfilePictureChanged(string? url)
        {
            ProfilePictureUrl = url;
            await InvokeAsync(StateHasChanged);
        }

        public void Dispose()
        {
            if (ProfilePictureService != null)
                ProfilePictureService.OnChange -= HandleProfilePictureChanged;
        }

        private bool IsActive(string path)
        {
            var currentPath = new Uri(Navigation.Uri).AbsolutePath;
            return currentPath.Equals(path, StringComparison.OrdinalIgnoreCase);
        }

        private string GetUserInitials()
        {
            if (string.IsNullOrWhiteSpace(UserName))
                return "GU";

            var parts = UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[1][0]}".ToUpper();
            if (parts.Length == 1 && parts[0].Length >= 2)
                return parts[0].Substring(0, 2).ToUpper();
            if (parts.Length == 1 && parts[0].Length == 1)
                return parts[0].ToUpper();
            return "GU";
        }

        private void ToggleProfileMenu()
        {
            ShowProfileMenu = !ShowProfileMenu;
        }

        private void CloseProfileMenu()
        {
            ShowProfileMenu = false;
        }

        private void ToggleMobileMenu()
        {
            ShowMobileMenu = !ShowMobileMenu;
        }

        private void CloseMobileMenu()
        {
            ShowMobileMenu = false;
        }

        private async Task OnLogout()
        {
            ShowProfileMenu = false;
            if (OnLogoutClick.HasDelegate)
            {
                await OnLogoutClick.InvokeAsync();
            }
            else
            {
                Navigation.NavigateTo("/login");
            }
        }
    }
}