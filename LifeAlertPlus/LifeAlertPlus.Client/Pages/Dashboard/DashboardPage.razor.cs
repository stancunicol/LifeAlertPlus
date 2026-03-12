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

    private string UserFullName = "";
    private string ProfilePictureUrl = "";
    private int ActiveAlerts = 3;
    private int StableCount = 5;
    private int TodayMeasurements = 24;

    protected override async Task OnInitializedAsync()
    {
        var currentUri = Navigation.ToAbsoluteUri(Navigation.Uri);
        if (!string.IsNullOrWhiteSpace(currentUri.Fragment))
        {
            var tokenFromFragment = TryGetQueryParameter(currentUri.Fragment.TrimStart('#'), "token");
            if (!string.IsNullOrWhiteSpace(tokenFromFragment))
            {
                await JSRuntime.InvokeVoidAsync("localStorage.setItem", "authToken", tokenFromFragment);
                Navigation.NavigateTo("/dashboard", replace: true);
                return;
            }
        }

        var claims = await TokenParser.GetClaimsAsync();
        if (claims != null)
        {
            UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
            ProfilePictureUrl = claims.ProfilePictureUrl;
        }
        else
        {
            UserFullName = "User";
        }
    }

    private string GetInitials(string name)
    {
        var parts = name.Split(' ');
        if (parts.Length >= 2)
            return $"{parts[0][0]}{parts[1][0]}".ToUpper();
        return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpper();
    }

    private static string? TryGetQueryParameter(string query, string key)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var queryValue = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(queryValue))
        {
            return null;
        }

        var pairs = queryValue.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pair in pairs)
        {
            var tokens = pair.Split('=', 2);
            if (tokens.Length == 0)
            {
                continue;
            }

            var currentKey = Uri.UnescapeDataString(tokens[0]);
            if (!string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (tokens.Length < 2)
            {
                return string.Empty;
            }

            return Uri.UnescapeDataString(tokens[1]);
        }

        return null;
    }
}