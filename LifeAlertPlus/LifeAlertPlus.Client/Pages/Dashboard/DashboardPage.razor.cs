using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.Dashboard;

public partial class DashboardPage : ComponentBase
{
    private string UserFullName = "";
    private string ProfilePictureUrl = "";
    private int ActiveAlerts = 3;
    private int StableCount = 5;
    private int TodayMeasurements = 24;

    protected override async Task OnInitializedAsync()
    {
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
}