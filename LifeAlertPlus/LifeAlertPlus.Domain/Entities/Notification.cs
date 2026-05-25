using System.ComponentModel.DataAnnotations.Schema;

namespace LifeAlertPlus.Domain.Entities
{
    public class Notification
    {
        public Guid Id { get; set; }

        [ForeignKey(nameof(Monitored))]
        public Guid IdMonitored { get; set; }

        [ForeignKey(nameof(User))]
        public Guid? IdUser { get; set; }

        public string NotificationType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }

        public bool IsRead { get; set; } = false;

        // False-alarm feedback loop: set when the alert episode resolves to Normal.
        // WasReal is filled in once the user answers the popup on next app visit.
        public DateTime? FeedbackRequestedAt { get; set; }
        public DateTime? FeedbackRespondedAt { get; set; }
        public bool? WasReal { get; set; }

        public Monitored Monitored { get; set; } = null!;
        public User? User { get; set; }
    }
}
