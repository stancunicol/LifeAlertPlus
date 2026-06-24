using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    // Interfață repository pentru tabela Measurements (date trimise de ESP32: puls, SpO2, temperatură, GPS, activitate)
    public interface IMeasurementRepository
    {
        Task AddMeasurementAsync(Measurement measurement);                                                                // Inserează o măsurătoare nouă primită de la ESP32
        Task<IEnumerable<Measurement>> GetMeasurementsByMonitoredIdAsync(Guid idMonitored, int pageNumber, int pageSize); // Returnează măsurătorile paginat (ordonate descrescător după CreatedAt)
        Task<Measurement?> GetMeasurementByIdAsync(Guid id);                                                             // Returnează o singură măsurătoare după ID
        Task<int> GetTodayMeasurementsCountAsync();                                                                       // Numărul total de măsurători înregistrate azi (statistică dashboard Admin)
        Task<int> DeleteMeasurementsOlderThanAsync(IEnumerable<Guid> monitoredIds, DateTime cutoffDate);                 // Șterge bulk măsurătorile mai vechi de cutoffDate (politică retenție date)
    }
}