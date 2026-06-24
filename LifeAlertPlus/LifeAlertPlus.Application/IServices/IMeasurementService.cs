using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Responses.Measurement;

namespace LifeAlertPlus.Application.IServices
{
    // Interfață pentru serviciul de măsurători ESP32 (puls, SpO2, temperatură, GPS, activitate)
    public interface IMeasurementService
    {
        Task AddMeasurementAsync(Measurement measurement);                                                                              // Salvează o măsurătoare nouă primită de la ESP32
        Task<IEnumerable<MeasurementResponseDTO>?> GetMeasurementsByMonitoredIdAsync(Guid idMonitored, int pageNumber, int pageSize); // Returnează măsurătorile paginat pentru o persoană monitorizată
        Task<MeasurementResponseDTO?> GetMeasurementByIdAsync(Guid id);                                                               // Returnează o singură măsurătoare după ID
        Task<int> GetTodayMeasurementsCountAsync();                                                                                    // Numărul total de măsurători înregistrate azi (dashboard admin)
        Task<int> DeleteMeasurementsOlderThanAsync(IEnumerable<Guid> monitoredIds, DateTime cutoffDate);                              // Șterge măsurătorile mai vechi de cutoffDate pentru persoanele date (retenție date)
    }
}