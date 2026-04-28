using System;

namespace LifeAlertPlus.Shared.DTOs.Requests.Email
{
    public class SendDoctorInvitationRequestDTO
    {
        public string DoctorEmail { get; set; } = string.Empty;
        public Guid PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
    }
}