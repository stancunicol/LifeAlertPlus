using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace LifeAlertPlus.Client.Pages.Dashboard;

public partial class DashboardPage : ComponentBase
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private string UserFullName = "";
    private string ProfilePictureUrl = "";
    private int ActiveAlerts = 3;
    private int StableCount = 5;
    private int TodayMeasurements = 24;

    protected override async Task OnInitializedAsync()
    {
        var currentUri = Navigation.ToAbsoluteUri(Navigation.Uri);
        if (!string.IsNullOrWhiteSpace(currentUri.Query))
        {
            var tokenFromQuery = TryGetQueryParameter(currentUri.Query, "token");
            if (!string.IsNullOrWhiteSpace(tokenFromQuery))
            {
                await JSRuntime.InvokeVoidAsync("localStorage.setItem", "authToken", tokenFromQuery);
                Navigation.NavigateTo("/dashboard", replace: true);
                return;
            }
        }

        var token = await JSRuntime.InvokeAsync<string>("localStorage.getItem", new object[] { "authToken" });
        if (!string.IsNullOrEmpty(token))
        {
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var jsonToken = handler.ReadJwtToken(token);
            var firstName = jsonToken?.Claims?.FirstOrDefault(x => x.Type == "firstName")?.Value ?? "";
            var lastName = jsonToken?.Claims?.FirstOrDefault(x => x.Type == "lastName")?.Value ?? "";
            var profilePictureUrl = jsonToken?.Claims?.FirstOrDefault(x => x.Type == "profilePictureUrl")?.Value ?? "";
            UserFullName = $"{firstName} {lastName}".Trim();
            ProfilePictureUrl = profilePictureUrl;
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