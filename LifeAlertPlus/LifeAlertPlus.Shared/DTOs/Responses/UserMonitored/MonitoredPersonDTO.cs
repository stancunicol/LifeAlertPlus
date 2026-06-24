using System;

namespace LifeAlertPlus.Shared.DTOs.Responses.UserMonitored
{
    // Versiune "rezumat" a unui pacient, imbricată în MonitoredUserDTO.MonitoredPeople — folosită
    // de UserMonitoredController (GET /api/usermonitored, vizualizare Admin) pentru a arăta lista
    // de pacienți ai fiecărui utilizator fără a încărca toate detaliile (praguri vitale, afecțiuni etc.).
    // Client: consumat de AdminPage.razor.cs și MonitoredUsersPage.razor.cs.
    public class MonitoredPersonDTO
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string DeviceSerialNumber { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsArchived { get; set; }
        public DateTime? ArchivedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
