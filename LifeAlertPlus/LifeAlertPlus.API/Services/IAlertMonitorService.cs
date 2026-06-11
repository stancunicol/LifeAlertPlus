namespace LifeAlertPlus.API.Services
{
    public interface IAlertMonitorService
    {
        bool IsIngestAllowed(string serial);
        Task ProcessMeasurementAsync(Guid monitoredId, double pulse, double temperature, double spo2,
            bool isFall, string activity = "", string coordinates = "");
        Task TriggerPanicAlertAsync(Guid monitoredId, string? coordinates = null);
        Task CheckBatteryAsync(Guid monitoredId, string serial, double battery);
    }
}
