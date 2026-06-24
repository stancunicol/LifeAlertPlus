using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;

namespace LifeAlertPlus.Client.Components.Header
{
    // Code-behind pentru antetul (header) zonei publice/utilizator — meniu de profil, meniu mobil,
    // contor de notificări necitite și afișarea numelui/pozei utilizatorului autentificat
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

        [Inject]
        private NotificationService NotificationService { get; set; } = default!;

        private bool ShowProfileMenu { get; set; } = false;
        private bool ShowMobileMenu { get; set; } = false;
        private string Version { get; set; } = string.Empty;
        private int _unreadCount = 0;
        private string T(string key) => Lang.T(key);

        // Afișează numele primit ca parametru dacă nu e gol; altfel cade pe valoarea memorată
        // dintr-o încărcare de pagină anterioară, ca header-ul să nu "clipească" la gol în timpul navigării.
        private string DisplayedName =>
            !string.IsNullOrWhiteSpace(UserName) ? UserName
            : !string.IsNullOrWhiteSpace(UserStateService.DisplayName) ? UserStateService.DisplayName
            : string.Empty;

        protected override async Task OnInitializedAsync()
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
            Lang.OnLanguageChanged += HandleLanguageChanged;
            Navigation.LocationChanged += HandleLocationChanged;
            NotificationService.OnUnreadCountChanged += HandleUnreadCountChanged;
            await RefreshUnreadCountAsync();
        }

        private async void HandleUnreadCountChanged()
        {
            await RefreshUnreadCountAsync();
        }

        // La fiecare navigare reîmprospătează contorul de notificări necitite (ex: pot apărea notificări noi)
        private async void HandleLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e)
        {
            await RefreshUnreadCountAsync();
        }

        // Cere de la API doar prima notificare (pageSize: 1) — scopul real este să citească UnreadCount din răspuns
        private async Task RefreshUnreadCountAsync()
        {
            try
            {
                var result = await NotificationService.GetPagedAsync(page: 1, pageSize: 1);
                _unreadCount = result?.UnreadCount ?? 0;
                await InvokeAsync(StateHasChanged);
            }
            catch { }
        }

        protected override void OnParametersSet()
        {
            // Memorează numele de fiecare dată când pagina-părinte transmite o valoare validă
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
            Navigation.LocationChanged -= HandleLocationChanged;
            NotificationService.OnUnreadCountChanged -= HandleUnreadCountChanged;
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