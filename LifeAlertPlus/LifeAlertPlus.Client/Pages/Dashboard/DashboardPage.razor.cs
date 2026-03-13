using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using LifeAlertPlus.Client.Services;

namespace LifeAlertPlus.Client.Pages.Dashboard;

public partial class DashboardPage : ComponentBase
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    [Inject]
    private TokenParserService TokenParser { get; set; } = default!;

    [Inject]
    private UserService UserService { get; set; } = default!;

    [Inject]
    private AuthenticationService AuthenticationService { get; set; } = default!;

    protected string UserFullName { get; set; } = "";
    protected string ProfilePictureUrl { get; set; } = "";
    protected int TotalMonitored { get; set; } = 12;
    protected int ActiveAlerts { get; set; } = 3;
    protected int StableCount { get; set; } = 5;
    protected int TodayMeasurements { get; set; } = 24;
    protected int OfflineCount { get; set; } = 0;

    protected IReadOnlyList<MonitoredSample> MonitoredSamples { get; private set; } = Array.Empty<MonitoredSample>();

    protected override async Task OnInitializedAsync()
    {
        MonitoredSamples = new List<MonitoredSample>
        {
            new("Maria Ionescu", 72, "Critical", 48, "90/60", "35.8", "Updated 2 min ago", false),
            new("Andrei Pop", 65, "Warning", 58, "110/70", "37.6", "Updated 5 min ago", true),
            new("Elena Radu", 54, "OK", 72, "118/76", "36.8", "Updated 12 min ago", true),
            new("George Marin", 80, "Warning", 62, "130/85", "38.2", "Updated 1 min ago", false)
        };

        OfflineCount = MonitoredSamples.Count(m => !m.Online);

        // TokenParserService.GetClaimsAsync() already handles reading the token from the
        // URL fragment (Google OAuth redirect), storing it in localStorage and cleaning the URL.
        // Doing it here first and returning early caused OnInitializedAsync to never run
        // a second time (same component instance), so user data was never loaded.
        var claims = await TokenParser.GetClaimsAsync();
        if (claims == null)
        {
            Navigation.NavigateTo("/login");
            return;
        }

        // Start with claims (token) so we always have something to show even if DB fields are empty.
        UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
        ProfilePictureUrl = claims.ProfilePictureUrl ?? string.Empty;

        var userFromApi = await UserService.GetUserByIdAsync(claims.UserId);
        if (userFromApi == null)
        {
            await AuthenticationService.LogoutAsync();
            Navigation.NavigateTo("/login");
            return;
        }

        var apiFullName = $"{userFromApi.FirstName} {userFromApi.LastName}".Trim();
        if (!string.IsNullOrWhiteSpace(apiFullName))
            UserFullName = apiFullName;

        var apiProfile = userFromApi.ProfilePictureUrl ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(apiProfile))
            ProfilePictureUrl = apiProfile;
        if (!string.IsNullOrEmpty(ProfilePictureUrl))
            await JSRuntime.InvokeVoidAsync("localStorage.setItem", "profilePictureUrl", ProfilePictureUrl);
        else
            await JSRuntime.InvokeVoidAsync("localStorage.removeItem", "profilePictureUrl");
    }

    private string GetInitials(string name)
    {
        var parts = name.Split(' ');
        if (parts.Length >= 2)
            return $"{parts[0][0]}{parts[1][0]}".ToUpper();
        return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpper();
    }

    protected string GetCardClass(string status, bool online)
    {
        if (!online) return "offline";
        return status.ToLower() switch
        {
            "critical" => "critical",
            "warning" => "warning",
            _ => "ok"
        };
    }

    protected string GetStatusText(string status, bool online)
    {
        if (!online) return "Offline";
        return status.ToLower() switch
        {
            "critical" => "Critical",
            "warning" => "Warning",
            _ => "OK"
        };
    }

    protected record MonitoredSample(
        string Name,
        int Age,
        string Status,
        int HeartRate,
        string BloodPressure,
        string Temperature,
        string LastUpdate,
        bool Online);
}