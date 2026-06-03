using Microsoft.AspNetCore.Components;
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
            FirstDayOfWeek = "monday",

            HeartRateMin = 60,
            HeartRateMax = 100,
            TemperatureMin = 36.0,
            TemperatureMax = 37.5,
            SpO2Min = 95,
            SpO2Max = 100,

            NotifyByEmail = true,
            NotifyByPush = true,
            EnableDailyReport = false,

            UpdateFrequency = 30,

            Language = "ro"
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

            await LoadWebPushStateAsync();
        }

        private void ResetThresholds()
        {
            Settings.HeartRateMin = 60;
            Settings.HeartRateMax = 100;
            Settings.TemperatureMin = 36.0;
            Settings.TemperatureMax = 37.5;
            Settings.SpO2Min = 95;
            Settings.SpO2Max = 100;
        }

        private void ResetAllSettings()
        {
            Settings = new AppSettings
            {
                FirstDayOfWeek = "monday",
                HeartRateMin = 60,
                HeartRateMax = 100,
                TemperatureMin = 36.0,
                TemperatureMax = 37.5,
                SpO2Min = 95,
                SpO2Max = 100,
                NotifyByEmail = true,
                NotifyByPush = true,
                NotifyBySms = false,
                EnableDailyReport = false,
                UpdateFrequency = 30,
                Language = "ro"
            };
        }

        private class AppSettings
        {
            public string FirstDayOfWeek { get; set; } = string.Empty;

            // Alert Thresholds
            public int HeartRateMin { get; set; }
            public int HeartRateMax { get; set; }
            public double TemperatureMin { get; set; }
            public double TemperatureMax { get; set; }
            public int SpO2Min { get; set; }
            public int SpO2Max { get; set; }

            // Notifications
            public bool NotifyByEmail { get; set; } = true;
            public bool NotifyByPush { get; set; } = true;
            public bool NotifyBySms { get; set; } = false;
            public bool EnableDailyReport { get; set; } = false;

            // Update frequency (seconds)
            public int UpdateFrequency { get; set; } = 30;

            // System
            public string Language { get; set; } = string.Empty;
        }

        // ── Web Push browser subscription ─────────────────────────────────────
        private bool _webPushSupported;
        private bool _webPushSubscribed;
        private string _webPushPermission = "default";

        private async Task LoadWebPushStateAsync()
        {
            try
            {
                _webPushSupported  = await JSRuntime.InvokeAsync<bool>("webPushIsSupported");
                _webPushPermission = await JSRuntime.InvokeAsync<string>("webPushGetPermission");
                if (_webPushSupported && _webPushPermission == "granted")
                {
                    _webPushSubscribed = await JSRuntime.InvokeAsync<bool>("eval",
                        "navigator.serviceWorker.ready.then(r=>r.pushManager.getSubscription()).then(s=>!!s)");
                }
            }
            catch { }
        }

        private async Task SubscribeWebPushAsync()
        {
            try
            {
                var apiBase = Navigation.BaseUri.TrimEnd('/');
                var token   = await JSRuntime.InvokeAsync<string?>("sessionStorage.getItem", "authToken") ?? "";
                _webPushSubscribed = await JSRuntime.InvokeAsync<bool>("webPushSubscribe", apiBase, token);
                _webPushPermission = await JSRuntime.InvokeAsync<string>("webPushGetPermission");
                StateHasChanged();
            }
            catch { }
        }

        private async Task UnsubscribeWebPushAsync()
        {
            try
            {
                var apiBase = Navigation.BaseUri.TrimEnd('/');
                var token   = await JSRuntime.InvokeAsync<string?>("sessionStorage.getItem", "authToken") ?? "";
                await JSRuntime.InvokeVoidAsync("webPushUnsubscribe", apiBase, token);
                _webPushSubscribed = false;
                StateHasChanged();
            }
            catch { }
        }
    }
}
