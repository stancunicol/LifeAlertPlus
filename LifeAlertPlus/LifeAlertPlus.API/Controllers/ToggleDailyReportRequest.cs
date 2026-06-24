namespace LifeAlertPlus.API.Controllers
{
    // DTO simplu pentru cererea PATCH /api/user/{id}/daily-report.
    // Trimis din frontend ca JSON { "enabled": true/false } pentru a activa/dezactiva raportul zilnic.
    public class ToggleDailyReportRequest
    {
        // true = activează raportul zilnic prin email; false = dezactivează
        public bool Enabled { get; set; }
    }
}
