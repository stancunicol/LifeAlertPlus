using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Client.Services;

namespace LifeAlertPlus.Client.Pages.History;

public partial class HistoryPage : ComponentBase
{
        [Inject]
        private TokenParserService TokenParserService { get; set; } = default!;

        private string UserFullName = "";
        private string ProfilePictureUrl = "";
        private string SelectedPerson = "All";
        private string SelectedMeasurementType = "All";
        private string SelectedTimePeriod = "Week";

        protected override async Task OnInitializedAsync()
        {
            var claims = await TokenParserService.GetClaimsAsync();
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

        private string GetRowClass(string status)
        {
            return status.ToLower() switch
            {
                "abnormal" => "row-abnormal",
                "warning" => "row-warning",
                "normal" => "row-normal",
                _ => ""
            };
        }

        private string GetTypeIcon(string type)
        {
            return type switch
            {
                "HeartRate" => "❤️",
                "BloodPressure" => "🩸",
                "Temperature" => "🌡️",
                _ => "📊"
            };
        }
}