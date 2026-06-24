namespace LifeAlertPlus.API.Services
{
    // Interfața publică a serviciului de monitorizare a alertelor.
    // Definește contractul pe care îl folosesc controllerii ESP și Measurement pentru a interacționa
    // cu logica de detectare a alertelor fără a crea o dependență directă față de implementare.
    public interface IAlertMonitorService
    {
        // Verifică dacă dispozitivul cu seria dată are voie să trimită date acum (rate limiting)
        bool IsIngestAllowed(string serial);

        // Procesează o măsurătoare nouă: detectează tendințe, evaluează severitatea, trimite alerte
        Task ProcessMeasurementAsync(Guid monitoredId, double pulse, double temperature, double spo2,
            bool isFall, string activity = "", string coordinates = "");

        // Declanșează o alertă de panică imediată (buton fizic pe brățară sau fall detectat critic)
        Task TriggerPanicAlertAsync(Guid monitoredId, string? coordinates = null);

        // Verifică nivelul bateriei și notifică dacă e sub pragul critic (ex. < 20%)
        Task CheckBatteryAsync(Guid monitoredId, string serial, double battery);
    }
}
