namespace LifeAlertPlus.Shared.DTOs.Responses.User
{
    public class UserProfileDTO
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? ProfilePictureUrl { get; set; }
        public bool IsEmailConfirmed { get; set; }
        public string? Provider { get; set; }
        public string? FirstDayOfTheWeek { get; set; }
        public string Language { get; set; } = "en";
        public string ThemeColor { get; set; } = "pink";
        public int MinHeartRate { get; set; }
        public int MaxHeartRate { get; set; }
        public float MinTemperature { get; set; }
        public float MaxTemperature { get; set; }
        public int MinSpO2 { get; set; }
        public int MaxSpO2 { get; set; }
        public int UpdateFrequency { get; set; } = 30;
        public bool NotifyByEmail { get; set; } = true;
        public bool NotifyByPush { get; set; } = true;
        public bool NotifyBySms { get; set; } = false;
        public bool EnableDailyReport { get; set; } = false;
        public DateTime? LastChangedPasswordAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
