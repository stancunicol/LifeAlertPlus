namespace LifeAlertPlus.Domain.Entities
{
    public class User
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
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
        public string Provider { get; set; }
        public string? ProviderKey { get; set; }
        public string? PendingEmail { get; set; }
        public string? FirstDayOfTheWeek { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
