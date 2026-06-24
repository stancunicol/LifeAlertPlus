using System;

namespace LifeAlertPlus.Shared.DTOs.Requests.Email
{
    // Server: primit de EmailController.SendDoctorInvitation (POST /api/email/send-doctor-invitation) →
    // EmailService.SendDoctorInvitationEmailAsync trimite link-ul cu token (hash SHA-256 stocat în Invitation).
    // Client: construit în SelectedMonitored.razor.cs (linia ~2306) când îngrijitorul invită un medic
    // să vadă datele pacientului curent — declanșat din UI-ul de partajare/acces medical.
    public class SendDoctorInvitationRequestDTO
    {
        public string DoctorEmail { get; set; } = string.Empty;
        public Guid PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
    }
}