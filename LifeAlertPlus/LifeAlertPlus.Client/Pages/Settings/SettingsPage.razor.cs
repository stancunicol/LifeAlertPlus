using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.Settings
{
    public partial class SettingsPage : ComponentBase
    {
        private AppSettings Settings { get; set; } = new AppSettings
        {
            Theme = "light",
            AccentColor = "#E8A5C8",
            FontSize = "medium",
            EnableAnimations = true,

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

        private void SaveSettings()
        {
            ShowSaveConfirmation = true;
            StateHasChanged();

            Task.Delay(3000).ContinueWith(_ =>
            {
                ShowSaveConfirmation = false;
                InvokeAsync(StateHasChanged);
            });
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
                Theme = "light",
                AccentColor = "#E8A5C8",
                FontSize = "medium",
                EnableAnimations = true,
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
