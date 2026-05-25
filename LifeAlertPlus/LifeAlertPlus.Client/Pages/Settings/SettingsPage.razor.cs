using Microsoft.AspNetCore.Components;
using System.Globalization;
using LifeAlertPlus.Shared.DTOs.Requests.User;
using LifeAlertPlus.Client.Services;
using Microsoft.JSInterop;

namespace LifeAlertPlus.Client.Pages.Settings
{
    public partial class SettingsPage : ComponentBase
    {
        [Inject]
        private UserApiClient UserApiClient { get; set; } = default!;

        [Inject]
        private IJSRuntime JSRuntime { get; set; } = default!;

        [Inject]
        private HttpClient Http { get; set; } = default!;

        [Inject]
        private NavigationManager Navigation { get; set; } = default!;

        [Inject]
        private TokenParserService TokenParser { get; set; } = default!;

        [Inject]
        private LanguageService Lang { get; set; } = default!;

        private AppSettings Settings { get; set; } = new AppSettings
        {
            AccentColor = "#A5D6A7",
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
            SpO2Min = 95,
            SpO2Max = 100,

            NotificationSound = true,
            NotificationVibration = true,
            DesktopNotifications = true,
            CheckInterval = "60",
            NotifyByEmail = true,
            NotifyByPush = true,
            EnableDailyReport = false,

            AutoSave = true,
            AutoBackup = true,
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

        private string T(string key) => Lang.T(key);

        private async Task SaveSettings()
        {
            // Sync language to LanguageService before saving
            Lang.SetLanguage(Settings.Language);
            await JSRuntime.InvokeVoidAsync("setLanguage", Settings.Language);

            var settings = new UserUpdateRequestDTO
            {
                FirstDayOfTheWeek = Settings.FirstDayOfWeek,
                Language = Settings.Language,
                MinHeartRate = Settings.HeartRateMin,
                MaxHeartRate = Settings.HeartRateMax,
                MinTemperature = (float)Settings.TemperatureMin,
                MaxTemperature = (float)Settings.TemperatureMax,
                MinSpO2 = Settings.SpO2Min,
                MaxSpO2 = Settings.SpO2Max,
                UpdateFrequency = Settings.UpdateFrequency,
                NotifyByEmail = Settings.NotifyByEmail,
                NotifyByPush = Settings.NotifyByPush,
                NotifyBySms = Settings.NotifyBySms,
                EnableDailyReport = Settings.EnableDailyReport
            };

            var request = await UserApiClient.UpdateUserAsync(UserId, settings);

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

            var userFromApi = await UserApiClient.GetUserByIdAsync(UserId);
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
            {
                Settings.Language = userFromApi.Language;
                Lang.SetLanguage(userFromApi.Language);
                await JSRuntime.InvokeVoidAsync("setLanguage", userFromApi.Language);
            }
            if (userFromApi.MinHeartRate > 0) Settings.HeartRateMin = userFromApi.MinHeartRate;
            if (userFromApi.MaxHeartRate > 0) Settings.HeartRateMax = userFromApi.MaxHeartRate;
            if (userFromApi.MinTemperature > 0) Settings.TemperatureMin = userFromApi.MinTemperature;
            if (userFromApi.MaxTemperature > 0) Settings.TemperatureMax = userFromApi.MaxTemperature;
            if (userFromApi.MinSpO2 > 0) Settings.SpO2Min = userFromApi.MinSpO2;
            if (userFromApi.MaxSpO2 > 0) Settings.SpO2Max = userFromApi.MaxSpO2;
            if (userFromApi.UpdateFrequency > 0)
                Settings.UpdateFrequency = userFromApi.UpdateFrequency;
            Settings.NotifyByEmail = userFromApi.NotifyByEmail;
            Settings.NotifyByPush = userFromApi.NotifyByPush;
            Settings.NotifyBySms = userFromApi.NotifyBySms;
            Settings.EnableDailyReport = userFromApi.EnableDailyReport;

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
            Settings.SpO2Min = 95;
            Settings.SpO2Max = 100;
        }

        private void ResetAllSettings()
        {
            Settings = new AppSettings
            {
                AccentColor = "#A5D6A7",
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
                SpO2Min = 95,
                SpO2Max = 100,
                NotificationSound = true,
                NotificationVibration = true,
                DesktopNotifications = true,
                CheckInterval = "60",
                NotifyByEmail = true,
                NotifyByPush = true,
                NotifyBySms = false,
                EnableDailyReport = false,
                AutoSave = true,
                AutoBackup = true,
                UpdateFrequency = 30,
                Language = "ro",
                DateFormat = "dd/MM/yyyy",
                TimeFormat = "24h"
            };
        }

        private class AppSettings
        {
            // Appearance
            public string AccentColor { get; set; } = string.Empty;
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
            public int SpO2Min { get; set; }
            public int SpO2Max { get; set; }

            // Notifications
            public bool NotificationSound { get; set; }
            public bool NotificationVibration { get; set; }
            public bool DesktopNotifications { get; set; }
            public string CheckInterval { get; set; } = string.Empty;
            public bool NotifyByEmail { get; set; } = true;
            public bool NotifyByPush { get; set; } = true;
            public bool NotifyBySms { get; set; } = false;
            public bool EnableDailyReport { get; set; } = false;

            // Data Management
            public bool AutoSave { get; set; }
            public bool AutoBackup { get; set; }

            // Update frequency (seconds)
            public int UpdateFrequency { get; set; } = 30;

            // System
            public string Language { get; set; } = string.Empty;
            public string DateFormat { get; set; } = string.Empty;
            public string TimeFormat { get; set; } = string.Empty;
        }
    }
}
