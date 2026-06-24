using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;

namespace LifeAlertPlus.Client.Components.HeaderAdmin
{
    // Code-behind pentru antetul (header) zonei de administrare — meniu de profil, meniu mobil,
    // afișarea numelui/pozei adminului și butonul de logout
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

        [Inject]
        private UserStateService UserStateService { get; set; } = default!;

        [Inject]
        private LanguageService Lang { get; set; } = default!;

        private string T(string key) => Lang.TEnglish(key);

        private bool ShowProfileMenu { get; set; } = false;
        private bool ShowMobileMenu { get; set; } = false;
        private string Version { get; set; } = string.Empty;

        // Preferă numele primit ca parametru; dacă nu există, cade pe numele din starea globală a utilizatorului
        private string DisplayedName =>
            !string.IsNullOrWhiteSpace(UserName) ? UserName
            : !string.IsNullOrWhiteSpace(UserStateService.DisplayName) ? UserStateService.DisplayName
            : string.Empty;

        protected override Task OnInitializedAsync()
        {
            Version = AppVersion.Version;
            // Se abonează la schimbările pozei de profil (ex: după upload) pentru a actualiza header-ul live
            if (ProfilePictureService != null)
            {
                ProfilePictureService.OnChange += HandleProfilePictureChanged;
                if (!string.IsNullOrEmpty(ProfilePictureService.Url))
                    ProfilePictureUrl = ProfilePictureService.Url;
            }
            UserStateService.OnChange += HandleUserNameChanged;
            return Task.CompletedTask;
        }

        protected override void OnParametersSet()
        {
            // Sincronizează numele primit prin parametru în starea globală, ca alte componente să-l poată folosi
            if (!string.IsNullOrWhiteSpace(UserName))
                UserStateService.SetDisplayName(UserName);
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
        }

        // Determină dacă o cale de navigare corespunde paginii curente, pentru a evidenția link-ul activ din meniu
        private bool IsActive(string path)
        {
            var currentPath = new Uri(Navigation.Uri).AbsolutePath;
            return currentPath.Equals(path, StringComparison.OrdinalIgnoreCase);
        }

        // Construiește inițialele afișate în avatar (ex: "John Doe" -> "JD"), cu fallback la "GU" (Guest User)
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

        // La delogare: folosește handler-ul custom dat de părinte, dacă există; altfel navighează direct la /login
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