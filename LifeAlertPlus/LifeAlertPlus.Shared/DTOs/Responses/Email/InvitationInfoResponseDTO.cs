using System;

namespace LifeAlertPlus.Shared.DTOs.Responses.Email
{
    // Server: returnat de InvitationsController (GET /api/invitations/info?token=...) — validează
    // existența + expirarea token-ului ÎNAINTE de a da acces la datele pacientului.
    // Client: deserializat în InviteAcceptPage.razor.cs (linia ~141) la încărcarea paginii de invitație —
    // pagina e accesibilă anonim (fără cont), securitatea se bazează exclusiv pe acest token din URL.
    public class InvitationInfoResponseDTO
    {
        public string DoctorEmail { get; set; } = string.Empty;
        public string CaregiverEmail { get; set; } = string.Empty;
        public Guid PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public bool IsAccepted { get; set; }
        public bool IsExpired { get; set; }
    }
}
