namespace LifeAlertPlus.Domain.Entities
{
    public class User
    {
        public Guid Id { get; set; }
        public Guid RoleId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
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
        public string? Language { get; set; }
        public string? FontSize { get; set; }
        public int? MinHeartRate { get; set; }
        public int? MaxHeartRate { get; set; }
        public double? MinTemperature { get; set; }
        public double? MaxTemperature { get; set; }
        public int? UpdateFrequency { get; set; }
        public int? DataRetentionDays { get; set; }
        public bool NotifyByEmail { get; set; } = true;
        public bool NotifyByPush { get; set; } = true;
        public bool NotifyBySms { get; set; } = false;
        public bool EnableDailyReport { get; set; } = false;
        public string? PhoneNumber { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }

        public Role Role { get; set; } = default!;
    }
}
