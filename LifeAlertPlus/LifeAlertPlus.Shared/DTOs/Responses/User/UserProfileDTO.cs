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
        public string FontSize { get; set; } = "medium";
        public int MinHeartRate { get; set; }
        public int MaxHeartRate { get; set; }
        public float MinTemperature { get; set; }
        public float MaxTemperature { get; set; }
        public int UpdateFrequency { get; set; } = 30;
        public DateTime? LastChangedPasswordAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
