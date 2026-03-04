namespace LifeAlertPlus.Domain.Entities
{
    public class User
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string? ProfilePictureUrl { get; set; }
        public bool IsEmailConfirmed { get; set; } = false;
        public string? PasswordHash { get; set; }
        public string? EmailConfirmationToken { get; set; }
        public DateTime? EmailConfirmationExpires { get; set; }
        public string? PasswordResetToken { get; set; }
        public DateTime? PasswordResetExpires { get; set; }
        public DateTime? LastChangedPasswordAt { get; set; }
        public string? EmailChangeToken { get; set; }
        public DateTime? EmailChangeExpires { get; set; }
        public string? EmailChangeCancelToken { get; set; }
        public string? Provider { get; set; }
        public string? ProviderKey { get; set; }
        public string? PendingEmail { get; set; }
        public string? FirstDayOfTheWeek { get; set; }
        public string Language { get; set; } = "en";
        public string ThemeColor { get; set; } = "pink";
        public string FontSize { get; set; } = "medium";
        public int MinHeartRate { get; set; } = 60;
        public int MaxHeartRate { get; set; } = 100;
        public float MinTemperature { get; set; } = 36.0f;
        public float MaxTemperature { get; set; } = 37.5f;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
