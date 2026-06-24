namespace LifeAlertPlus.Shared.DTOs.Responses.DoctorNote
{
    // Notiță medicală lăsată de un medic pentru un pacient. Returnat în 2 fluxuri diferite:
    //   - DoctorNoteController (autentificat JWT — folosit de proprietarul pacientului)
    //   - InvitationsController (anonim, prin token — folosit de medicul invitat, fără cont)
    // Client: afișat în SelectedMonitored.razor.cs și ViewSelectedMonitored.razor.cs (secțiunea de notițe medic).
    public class DoctorNoteDTO
    {
        public Guid Id { get; set; }
        public string DoctorEmail { get; set; } = string.Empty;
        public string DoctorName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
