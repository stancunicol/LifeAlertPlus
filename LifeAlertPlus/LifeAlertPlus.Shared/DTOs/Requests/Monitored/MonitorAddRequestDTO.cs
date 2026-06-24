namespace LifeAlertPlus.Shared.DTOs.Requests.Monitored
{
    // Server: primit de MonitoredController.AddMonitoredPerson (POST /api/monitored) — combină datele
    // noului pacient (MonitorCreateRequestDTO) cu emailul utilizatorului curent, ca să creeze și legătura
    // UserMonitored (proprietarul inițial al pacientului) în același request.
    // Client: construit în MonitoredPage.razor.cs (linia ~543), trimis prin MonitoredApiClient.AddMonitoredPersonAsync,
    // la submit-ul formularului de adăugare a unei persoane monitorizate noi.
    public class MonitorAddRequestDTO
    {
        public MonitorCreateRequestDTO MonitoredPerson { get; set; } = new MonitorCreateRequestDTO();
        public string CurrentUserEmail { get; set; } = string.Empty;
    }
}