namespace LifeAlertPlus.Domain.Entities
{
    public class DoctorNote
    {
        public Guid Id { get; set; }
        public Guid IdMonitored { get; set; }
        public Guid IdDoctor { get; set; }       // User.Id of the doctor
        public string DoctorEmail { get; set; } = string.Empty;
        public string DoctorName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public Monitored Monitored { get; set; } = null!;
    }
}
