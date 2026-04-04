using Microsoft.AspNetCore.Components;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Client.Services;
using Microsoft.JSInterop;

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

        [Inject]
        private TokenParserService TokenParser { get; set; } = default!;

        private AppSettings Settings { get; set; } = new AppSettings
        {
            Theme = "pink",
            AccentColor = "#A5D6A7",
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
            UpdateFrequency = 30,

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
                ThemeColor = Settings.Theme,
                FontSize = Settings.FontSize,
                MinHeartRate = Settings.HeartRateMin,
                MaxHeartRate = Settings.HeartRateMax,
                MinTemperature = (float)Settings.TemperatureMin,
                MaxTemperature = (float)Settings.TemperatureMax,
                UpdateFrequency = Settings.UpdateFrequency
            };

            var request = await UserService.UpdateUserAsync(UserId, settings);

            if(request == false)
            {
                return;
            }

            ShowSaveConfirmation = true;
            StateHasChanged();

            await Task.Delay(3000);
            ShowSaveConfirmation = false;
            StateHasChanged();
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

            var claims = await TokenParser.GetClaimsAsync();
            if (claims == null)
            {
                Navigation.NavigateTo("/login");
                return;
            }

            UserFullName = $"{claims.FirstName} {claims.LastName}".Trim();
            ProfilePictureUrl = claims.ProfilePictureUrl;
            UserId = claims.UserId;

            var userFromApi = await UserService.GetUserByIdAsync(UserId);
            if (userFromApi == null)
            {
                // User no longer exists in DB (e.g. after DB reset) — clear all client state
                await JSRuntime.InvokeVoidAsync("sessionStorage.removeItem", "authToken");
                await JSRuntime.InvokeVoidAsync("sessionStorage.removeItem", "profilePictureUrl");
                Navigation.NavigateTo("/login");
                return;
            }

            if (!string.IsNullOrEmpty(userFromApi.FirstDayOfTheWeek))
                Settings.FirstDayOfWeek = userFromApi.FirstDayOfTheWeek;
            if (!string.IsNullOrEmpty(userFromApi.Language))
                Settings.Language = userFromApi.Language;
            if (!string.IsNullOrEmpty(userFromApi.ThemeColor))
                Settings.Theme = userFromApi.ThemeColor;
            if (!string.IsNullOrEmpty(userFromApi.FontSize))
                Settings.FontSize = userFromApi.FontSize;
            Settings.HeartRateMin = userFromApi.MinHeartRate;
            Settings.HeartRateMax = userFromApi.MaxHeartRate;
            Settings.TemperatureMin = userFromApi.MinTemperature;
            Settings.TemperatureMax = userFromApi.MaxTemperature;
            if (userFromApi.UpdateFrequency > 0)
                Settings.UpdateFrequency = userFromApi.UpdateFrequency;

            if (!string.IsNullOrEmpty(userFromApi.ProfilePictureUrl))
            {
                ProfilePictureUrl = userFromApi.ProfilePictureUrl;
                await JSRuntime.InvokeVoidAsync("sessionStorage.setItem", "profilePictureUrl", userFromApi.ProfilePictureUrl);
            }
            else
            {
                ProfilePictureUrl = string.Empty;
                await JSRuntime.InvokeVoidAsync("sessionStorage.removeItem", "profilePictureUrl");
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
                AccentColor = "#A5D6A7",
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
                UpdateFrequency = 30,
                Language = "ro",
                DateFormat = "dd/MM/yyyy",
                TimeFormat = "24h"
            };
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

            // Update frequency (seconds)
            public int UpdateFrequency { get; set; } = 30;

            // System
            public string Language { get; set; } = string.Empty;
            public string DateFormat { get; set; } = string.Empty;
            public string TimeFormat { get; set; } = string.Empty;
        }
    }
}
