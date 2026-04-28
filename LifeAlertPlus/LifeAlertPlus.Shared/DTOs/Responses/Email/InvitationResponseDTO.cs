using System;

namespace LifeAlertPlus.Shared.DTOs.Responses.Email
{
    public class InvitationResponseDTO
    {
        public string DoctorEmail { get; set; } = string.Empty;
        public Guid PatientId { get; set; }
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public bool IsAccepted { get; set; }
    }
}