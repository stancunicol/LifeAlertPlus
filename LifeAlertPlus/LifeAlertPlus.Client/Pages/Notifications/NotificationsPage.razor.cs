using Microsoft.AspNetCore.Components;

namespace LifeAlertPlus.Client.Pages.Notifications;

public partial class NotificationsPage : ComponentBase
{
    private string UserFullName = "";
        protected override async Task OnInitializedAsync()
        {
            var token = await JSRuntime.InvokeAsync<string>("localStorage.getItem", new object[] { "authToken" });
            if (!string.IsNullOrEmpty(token))
            {
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jsonToken = handler.ReadJwtToken(token);
                var firstName = jsonToken?.Claims?.FirstOrDefault(x => x.Type == "firstName")?.Value ?? "";
                var lastName = jsonToken?.Claims?.FirstOrDefault(x => x.Type == "lastName")?.Value ?? "";
                UserFullName = $"{firstName} {lastName}".Trim();
            }
            else
            {
                UserFullName = "User";
            }
        }
    private string ActiveFilter = "All";

    private List<Notification> AllNotifications = new()
    {
        new Notification
        {
            Id = 1,
            Type = "Critical",
            Title = "Critical Heart Rate Alert",
            Description = "Maria Ionescu's heart rate has exceeded 140 bpm. Immediate attention required.",
            PersonName = "Maria Ionescu",
            Time = "5 minutes ago",
            IsRead = false
        },
        new Notification
        {
            Id = 2,
            Type = "Critical",
            Title = "High Blood Pressure Detected",
            Description = "Maria Ionescu's blood pressure reading of 160/95 mmHg is dangerously high.",
            PersonName = "Maria Ionescu",
            Time = "15 minutes ago",
            IsRead = false
        },
        new Notification
        {
            Id = 3,
            Type = "Warning",
            Title = "Elevated Heart Rate",
            Description = "Ion Popa's heart rate of 95 bpm is slightly above normal range.",
            PersonName = "Ion Popa",
            Time = "1 hour ago",
            IsRead = false
        },
        new Notification
        {
            Id = 4,
            Type = "Warning",
            Title = "Blood Pressure Monitoring",
            Description = "Ion Popa's blood pressure at 138/88 mmHg needs close monitoring.",
            PersonName = "Ion Popa",
            Time = "2 hours ago",
            IsRead = false
        },
        new Notification
        {
            Id = 5,
            Type = "Info",
            Title = "Daily Health Check Completed",
            Description = "Elena Popescu has successfully completed her daily health measurements.",
            PersonName = "Elena Popescu",
            Time = "3 hours ago",
            IsRead = true
        },
        new Notification
        {
            Id = 6,
            Type = "Critical",
            Title = "Missed Scheduled Measurement",
            Description = "Maria Ionescu has missed her scheduled 2:00 PM measurement check.",
            PersonName = "Maria Ionescu",
            Time = "4 hours ago",
            IsRead = true
        },
        new Notification
        {
            Id = 7,
            Type = "Info",
            Title = "Measurement Reminder",
            Description = "Vasile Dumitrescu is due for his evening health check in 30 minutes.",
            PersonName = "Vasile Dumitrescu",
            Time = "5 hours ago",
            IsRead = true
        },
        new Notification
        {
            Id = 8,
            Type = "Warning",
            Title = "Irregular Sleep Pattern",
            Description = "Gheorghe Stan's sleep duration has been below 6 hours for 3 consecutive nights.",
            PersonName = "Gheorghe Stan",
            Time = "6 hours ago",
            IsRead = true
        },
        new Notification
        {
            Id = 9,
            Type = "Info",
            Title = "Weekly Summary Available",
            Description = "Your weekly health summary report for Ana Marin is now ready to view.",
            PersonName = "Ana Marin",
            Time = "1 day ago",
            IsRead = true
        },
        new Notification
        {
            Id = 10,
            Type = "Critical",
            Title = "Emergency Contact Alert",
            Description = "Mihai Petre has triggered an emergency alert button. Please check immediately.",
            PersonName = "Mihai Petre",
            Time = "2 days ago",
            IsRead = true
        },
        new Notification
        {
            Id = 11,
            Type = "Info",
            Title = "Device Battery Low",
            Description = "Elena Popescu's monitoring device battery is below 20%. Please charge soon.",
            PersonName = "Elena Popescu",
            Time = "2 days ago",
            IsRead = true
        },
        new Notification
        {
            Id = 12,
            Type = "Warning",
            Title = "Temperature Fluctuation",
            Description = "Ana Marin's temperature has varied by more than 1°C in the past 4 hours.",
            PersonName = "Ana Marin",
            Time = "3 days ago",
            IsRead = true
        }
    };

    private List<Notification> FilteredNotifications
    {
        get
        {
            return ActiveFilter switch
            {
                "All" => AllNotifications,
                "Unread" => AllNotifications.Where(n => !n.IsRead).ToList(),
                _ => AllNotifications.Where(n => n.Type.Equals(ActiveFilter, StringComparison.OrdinalIgnoreCase)).ToList()
            };
        }
    }

    private int CriticalCount => AllNotifications.Count(n => n.Type == "Critical");
    private int WarningCount => AllNotifications.Count(n => n.Type == "Warning");
    private int InfoCount => AllNotifications.Count(n => n.Type == "Info");
    private int UnreadCount => AllNotifications.Count(n => !n.IsRead);

    private string GetNotificationIcon(string type)
    {
        return type switch
        {
            "Critical" => "🚨",
            "Warning" => "⚠️",
            "Info" => "ℹ️",
            _ => "🔔"
        };
    }

    private void MarkAsRead(int id)
    {
        var notification = AllNotifications.FirstOrDefault(n => n.Id == id);
        if (notification != null)
        {
            notification.IsRead = true;
            StateHasChanged();
        }
    }

    private void MarkAllAsRead()
    {
        foreach (var notification in AllNotifications)
        {
            notification.IsRead = true;
        }
        StateHasChanged();
    }

    private void DeleteNotification(int id)
    {
        var notification = AllNotifications.FirstOrDefault(n => n.Id == id);
        if (notification != null)
        {
            AllNotifications.Remove(notification);
            StateHasChanged();
        }
    }

    private string GetEmptyMessage()
    {
        return ActiveFilter switch
        {
            "Unread" => "All caught up! No unread notifications.",
            "Critical" => "No critical alerts at the moment.",
            "Warning" => "No warnings to display.",
            "Info" => "No information messages.",
            _ => "No notifications available."
        };
    }

    public class Notification
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public bool IsRead { get; set; }
    }
}