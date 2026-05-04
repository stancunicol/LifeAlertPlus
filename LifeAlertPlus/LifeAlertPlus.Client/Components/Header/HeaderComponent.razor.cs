using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;

namespace LifeAlertPlus.Client.Components.Header
{
    public partial class HeaderComponent : ComponentBase, IDisposable
    {
        [Inject]
        private NavigationManager Navigation { get; set; } = default!;

        [Parameter]
        public string UserName { get; set; } = string.Empty;

        [Parameter]
        public string? ProfilePictureUrl { get; set; }

        [Parameter]
        public EventCallback OnLogoutClick { get; set; }

        [Inject]
        private HttpClient Http { get; set; } = default!;

        [Inject]
        private ProfilePictureService ProfilePictureService { get; set; } = default!;

        [Inject]
        private UserStateService UserStateService { get; set; } = default!;

        [Inject]
        private LanguageService Lang { get; set; } = default!;

        private bool ShowProfileMenu { get; set; } = false;
        private bool ShowMobileMenu { get; set; } = false;
        private string Version { get; set; } = string.Empty;
        private string T(string key) => Lang.T(key);

        // Shows the parameter name if non-empty, otherwise falls back to the cached value
        // from a previous page load so the header never flickers to empty during navigation.
        private string DisplayedName =>
            !string.IsNullOrWhiteSpace(UserName) ? UserName
            : !string.IsNullOrWhiteSpace(UserStateService.DisplayName) ? UserStateService.DisplayName
            : string.Empty;

        protected override Task OnInitializedAsync()
        {
            Version = AppVersion.Version;
            if (ProfilePictureService != null)
            {
                ProfilePictureService.OnChange += HandleProfilePictureChanged;
                if (!string.IsNullOrEmpty(ProfilePictureService.Url))
                    ProfilePictureUrl = ProfilePictureService.Url;
            }
            UserStateService.OnChange += HandleUserNameChanged;
            Lang.OnLanguageChanged += HandleLanguageChanged;
            return Task.CompletedTask;
        }

        protected override void OnParametersSet()
        {
            // Cache the name whenever the page passes a valid one.
            if (!string.IsNullOrWhiteSpace(UserName))
                UserStateService.SetDisplayName(UserName);
        }

        private async void HandleLanguageChanged()
        {
            await InvokeAsync(StateHasChanged);
        }

        private async void HandleProfilePictureChanged(string? url)
        {
            ProfilePictureUrl = url;
            await InvokeAsync(StateHasChanged);
        }

        private async void HandleUserNameChanged(string? name)
        {
            await InvokeAsync(StateHasChanged);
        }

        public void Dispose()
        {
            if (ProfilePictureService != null)
                ProfilePictureService.OnChange -= HandleProfilePictureChanged;
            UserStateService.OnChange -= HandleUserNameChanged;
            Lang.OnLanguageChanged -= HandleLanguageChanged;
        }

        private bool IsActive(string path)
        {
            var currentPath = new Uri(Navigation.Uri).AbsolutePath;
            return currentPath.Equals(path, StringComparison.OrdinalIgnoreCase);
        }

        private string GetUserInitials()
        {
            var name = DisplayedName;
            if (string.IsNullOrWhiteSpace(name))
                return "GU";

            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
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