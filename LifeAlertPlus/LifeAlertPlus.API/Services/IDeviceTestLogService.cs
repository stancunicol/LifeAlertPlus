namespace LifeAlertPlus.API.Services
{
    // Interfața pentru serviciul de logging al dispozitivelor ESP în fișier JSON local.
    // Folosit exclusiv în mediul de testare/debugging cu dispozitive fizice reale.
    // Se activează prin configurarea "DeviceTestLog:Enabled": true în appsettings.
    public interface IDeviceTestLogService
    {
        // Scrie o intrare de log (ingested data, heartbeat, alertă) în fișierul JSON de diagnostic
        void Log(DeviceTestLogEntry entry);
    }
}
