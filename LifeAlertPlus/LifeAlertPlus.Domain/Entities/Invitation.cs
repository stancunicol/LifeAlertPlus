using System;

namespace LifeAlertPlus.Domain.Entities
{
    public class Invitation
    {
        public Guid Id { get; set; }
        public string DoctorEmail { get; set; } = string.Empty;
        public Guid PatientId { get; set; }
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public bool IsAccepted { get; set; } = false;
        public DateTime CreatedAt { get; set; }
    }
}