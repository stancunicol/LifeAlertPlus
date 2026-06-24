using System;
using System.Collections.Generic;

namespace LifeAlertPlus.Shared.DTOs.Responses.UserMonitored
{
    // Server: returnat de UserMonitoredController (GET, vizualizare Admin) — un utilizator (îngrijitor)
    // împreună cu lista pacienților pe care îi monitorizează (MonitoredPeople), reconstruind relația
    // many-to-many User↔Monitored într-o formă convenabilă pentru afișare ierarhică.
    // Client: consumat de AdminPage.razor.cs și MonitoredUsersPage.razor.cs (panourile de administrare).
    public class MonitoredUserDTO
    {
        public Guid UserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = "User";
        public bool IsActive { get; set; }
        public string Provider { get; set; } = "Local";
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public IReadOnlyList<MonitoredPersonDTO> MonitoredPeople { get; set; } = Array.Empty<MonitoredPersonDTO>();
    }
}
