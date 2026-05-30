using System.Collections.Generic;

namespace LifeAlertPlus.Domain.Entities
{
    public class Monitored
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime? Birthdate { get; set; }
        public string Gender { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public int? UpdateFrequency { get; set; }
        public string DeviceSerialNumber { get; set; } = string.Empty;
        public int? MinHeartRate { get; set; }
        public int? MaxHeartRate { get; set; }
        public double? MinTemperature { get; set; }
        public double? MaxTemperature { get; set; }
        public int? MinSpO2 { get; set; }
        public int? MaxSpO2 { get; set; }
        public int? DataRetentionDays { get; set; }
        // GDPR — how long to keep data after the person is archived (longer than
        // active retention; null = indefinite, must be manually deleted).
        public int? ArchiveRetentionDays { get; set; }
        public bool IsActive { get; set; }
        public bool IsArchived { get; set; }
        public DateTime? ArchivedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }

        public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    }
}
