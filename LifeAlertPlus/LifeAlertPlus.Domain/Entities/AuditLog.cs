namespace LifeAlertPlus.Domain.Entities
{
    public class AuditLog
    {
        public Guid Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string ActorEmail { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // Account | System | Patient | Security
    }
}
