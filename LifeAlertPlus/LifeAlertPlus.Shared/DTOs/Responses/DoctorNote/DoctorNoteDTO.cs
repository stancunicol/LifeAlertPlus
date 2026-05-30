namespace LifeAlertPlus.Shared.DTOs.Responses.DoctorNote
{
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
