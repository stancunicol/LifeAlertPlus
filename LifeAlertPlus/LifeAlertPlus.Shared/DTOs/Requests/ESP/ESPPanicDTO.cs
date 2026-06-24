namespace LifeAlertPlus.Shared.DTOs.Requests.ESP
{
    // Payload-ul trimis de firmware-ul ESP32 (panic_send() din main.cpp) când Butonul 1 e apăsat.
    // Server: primit de ESPController.PanicAlert (POST /api/ESP/panic, autentificat prin X-Device-Key) →
    // declanșează imediat o alertă critică (AlertMonitorService.TriggerPanicAlertAsync), indiferent
    // de ciclul normal de evaluare a măsurătorilor. Produs exclusiv de dispozitiv, fără caller din Client.
    public class ESPPanicDTO
    {
        public string Serial { get; set; } = string.Empty;
        public string? Coordinates { get; set; }
    }
}
