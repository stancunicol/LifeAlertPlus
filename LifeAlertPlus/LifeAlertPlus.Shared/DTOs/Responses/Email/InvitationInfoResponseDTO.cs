using System;

namespace LifeAlertPlus.Shared.DTOs.Responses.Email
{
    public class InvitationInfoResponseDTO
    {
        public string DoctorEmail { get; set; } = string.Empty;
        public Guid PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public bool IsAccepted { get; set; }
        public bool IsExpired { get; set; }
    }
}
