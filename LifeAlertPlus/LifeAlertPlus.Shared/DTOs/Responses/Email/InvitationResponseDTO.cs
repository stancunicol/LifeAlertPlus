using System;

namespace LifeAlertPlus.Shared.DTOs.Responses.Email
{
    // Server: returnat de EmailController.SendDoctorInvitation după trimiterea emailului de invitație —
    // confirmă datele invitației create (token-ul brut e expus aici doar în răspunsul API, în DB se
    // stochează hash-ul SHA-256, nu token-ul în clar).
    // NOTĂ: la momentul acestui comentariu, nu am găsit un caller explicit din Client care să consume
    // acest răspuns dincolo de verificarea succesului — UI-ul tratează apelul ca fire-and-forget.
    public class InvitationResponseDTO
    {
        public string DoctorEmail { get; set; } = string.Empty;
        public Guid PatientId { get; set; }
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public bool IsAccepted { get; set; }
    }
}