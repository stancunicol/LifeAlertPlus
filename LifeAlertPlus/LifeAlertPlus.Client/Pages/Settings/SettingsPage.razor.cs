using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Client.Services;
using Microsoft.JSInterop;
using System.IdentityModel.Tokens.Jwt;

namespace LifeAlertPlus.Client.Pages.Settings
{
    public partial class SettingsPage : ComponentBase
    {
        [Inject]
        private UserService UserService { get; set; } = default!;

        [Inject]
        private IJSRuntime JSRuntime { get; set; } = default!;

        [Inject]
        private HttpClient Http { get; set; } = default!;

        [Inject]
        private NavigationManager Navigation { get; set; } = default!;

        private AppSettings Settings { get; set; } = new AppSettings
        {
            Theme = "pink",
            AccentColor = "#E8A5C8",
            FontSize = "medium",
            EnableAnimations = true,
            FirstDayOfWeek = "monday",

            BPSystolicMin = 90,
            BPSystolicMax = 140,
            BPDiastolicMin = 60,
            BPDiastolicMax = 90,
            HeartRateMin = 60,
            HeartRateMax = 100,
            GlucoseMin = 70,
            GlucoseMax = 140,
            TemperatureMin = 36.0,
            TemperatureMax = 37.5,

            NotificationSound = true,
            NotificationVibration = true,
            DesktopNotifications = true,
            CheckInterval = "60",

            AutoSave = true,
            AutoBackup = true,
            HistoryRetention = "365",

            Language = "ro",
            DateFormat = "dd/MM/yyyy",
            TimeFormat = "24h"
        };

        private bool ShowSaveConfirmation { get; set; } = false;
        private string UserFullName = "";
        private string ProfilePictureUrl = "";
        private string Version = "";
        private Guid UserId;

        private async Task SaveSettings()
        {
            var settings = new UserUpdateRequestDTO
            {
                FirstDayOfTheWeek = Settings.FirstDayOfWeek,
                Language = Settings.Language,
                ThemeColor = Settings.Theme
            };

            var request = await UserService.UpdateUserAsync(UserId, settings);

            if(request == false)
            {
                Console.WriteLine("Failed to save settings.");
                return;
            }

            ShowSaveConfirmation = true;
            StateHasChanged();

            Task.Delay(3000).ContinueWith(_ =>
            {
                ShowSaveConfirmation = false;
                InvokeAsync(StateHasChanged);
            });
        }

        protected override async Task OnInitializedAsync()
        {
            try
            {
                var url = Navigation.BaseUri + "VERSION";
                var v = await Http.GetStringAsync(url);
                Version = (v ?? string.Empty).Trim();

                if (string.IsNullOrEmpty(Version))
                {
                    Version = AppVersion.Version;
                }
            }
            catch
            {
                Version = AppVersion.Version;
            }

            var token = await JSRuntime.InvokeAsync<string>("localStorage.getItem", new object[] { "authToken" });
            if (!string.IsNullOrEmpty(token))
            {
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jsonToken = handler.ReadJwtToken(token);
                var firstName = jsonToken?.Claims?.FirstOrDefault(x => x.Type == "firstName")?.Value ?? "";
                var userIdClaim = jsonToken?.Claims?.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Sub);
                var lastName = jsonToken?.Claims?.FirstOrDefault(x => x.Type == "lastName")?.Value ?? "";
                var profilePictureUrl = jsonToken?.Claims?.FirstOrDefault(x => x.Type == "profilePictureUrl")?.Value ?? "";
                UserFullName = $"{firstName} {lastName}".Trim();
                ProfilePictureUrl = profilePictureUrl;

                if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out Guid userId))
                {
                    UserId = userId;
                }
            }
            else
            {
                UserFullName = "User";
            }
        }

        private void ResetThresholds()
        {
            Settings.BPSystolicMin = 90;
            Settings.BPSystolicMax = 140;
            Settings.BPDiastolicMin = 60;
            Settings.BPDiastolicMax = 90;
            Settings.HeartRateMin = 60;
            Settings.HeartRateMax = 100;
            Settings.GlucoseMin = 70;
            Settings.GlucoseMax = 140;
            Settings.TemperatureMin = 36.0;
            Settings.TemperatureMax = 37.5;
        }

        private void ResetAllSettings()
        {
            Settings = new AppSettings
            {
                Theme = "pink",
                AccentColor = "#E8A5C8",
                FontSize = "medium",
                EnableAnimations = true,
                FirstDayOfWeek = "monday",
                BPSystolicMin = 90,
                BPSystolicMax = 140,
                BPDiastolicMin = 60,
                BPDiastolicMax = 90,
                HeartRateMin = 60,
                HeartRateMax = 100,
                GlucoseMin = 70,
                GlucoseMax = 140,
                TemperatureMin = 36.0,
                TemperatureMax = 37.5,
                NotificationSound = true,
                NotificationVibration = true,
                DesktopNotifications = true,
                CheckInterval = "60",
                AutoSave = true,
                AutoBackup = true,
                HistoryRetention = "365",
                Language = "ro",
                DateFormat = "dd/MM/yyyy",
                TimeFormat = "24h"
            };
        }

        private void ExportData()
        {
            Console.WriteLine("Exporting data...");
        }

        private void ImportData()
        {
            Console.WriteLine("Importing data...");
        }

        private void ClearCache()
        {
            Console.WriteLine("Clearing cache...");
        }

        private class AppSettings
        {
            // Appearance
            public string Theme { get; set; } = string.Empty;
            public string AccentColor { get; set; } = string.Empty;
            public string FontSize { get; set; } = string.Empty;
            public bool EnableAnimations { get; set; }
            public string FirstDayOfWeek { get; set; } = string.Empty;

            // Alert Thresholds
            public int BPSystolicMin { get; set; }
            public int BPSystolicMax { get; set; }
            public int BPDiastolicMin { get; set; }
            public int BPDiastolicMax { get; set; }
            public int HeartRateMin { get; set; }
            public int HeartRateMax { get; set; }
            public int GlucoseMin { get; set; }
            public int GlucoseMax { get; set; }
            public double TemperatureMin { get; set; }
            public double TemperatureMax { get; set; }

            // Notifications
            public bool NotificationSound { get; set; }
            public bool NotificationVibration { get; set; }
            public bool DesktopNotifications { get; set; }
            public string CheckInterval { get; set; } = string.Empty;

            // Data Management
            public bool AutoSave { get; set; }
            public bool AutoBackup { get; set; }
            public string HistoryRetention { get; set; } = string.Empty;

            // System
            public string Language { get; set; } = string.Empty;
            public string DateFormat { get; set; } = string.Empty;
            public string TimeFormat { get; set; } = string.Empty;
        }
    }
}
