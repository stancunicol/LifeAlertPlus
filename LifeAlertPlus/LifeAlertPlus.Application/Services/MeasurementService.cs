using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Shared.DTOs.Responses.Measurement;

namespace LifeAlertPlus.Application.Services
{
    // Serviciu pentru măsurătorile trimise de dispozitivul ESP32 (puls, SpO2, temperatură, GPS, activitate, cădere)
    // Mapează entitățile Domain la DTO-uri de răspuns pentru a nu expune modelul intern în API
    public class MeasurementService : IMeasurementService
    {
        private readonly IMeasurementRepository _measurementRepository; // Acces la tabela Measurements din DB

        public MeasurementService(IMeasurementRepository measurementRepository)
        {
            _measurementRepository = measurementRepository;
        }

        // Salvează o măsurătoare nouă primită de la ESP32 (apelat din ESPController)
        public async Task AddMeasurementAsync(Measurement measurement)
        {
            await _measurementRepository.AddMeasurementAsync(measurement);
        }

        // Returnează măsurătorile paginat pentru o persoană monitorizată (afișate în UI → grafice, istoric)
        public async Task<IEnumerable<MeasurementResponseDTO>?> GetMeasurementsByMonitoredIdAsync(Guid idMonitored, int pageNumber, int pageSize)
        {
            var measurements = await _measurementRepository.GetMeasurementsByMonitoredIdAsync(idMonitored, pageNumber, pageSize);

            if (measurements == null || !measurements.Any())
                return Enumerable.Empty<MeasurementResponseDTO>(); // Returnăm listă goală, nu null

            // Mapăm entitatea la DTO (omitem câmpuri interne, expunem doar ce are nevoie frontend-ul)
            return measurements.Select(m => new MeasurementResponseDTO
            {
                Name        = m.Name,
                Activity    = m.Activity,    // Eticheta activității detectată de MPU6050 (ex: "walking")
                IsFall      = m.IsFall,      // Cădere detectată de accelerometru
                IdMonitored = m.IdMonitored,
                Pulse       = m.Pulse,       // Bătăi pe minut (MAX30100)
                Temperature = m.Temperature, // Temperatura corporală în °C
                SpO2        = m.SpO2,        // Saturația oxigenului în % (MAX30100)
                Coordinates = m.Coordinates, // "lat,lon" de la Neo-6M GPS
                CreatedAt   = m.CreatedAt
            });
        }

        // Returnează o singură măsurătoare după ID (detalii individuale)
        public async Task<MeasurementResponseDTO?> GetMeasurementByIdAsync(Guid id)
        {
            var measurement = await _measurementRepository.GetMeasurementByIdAsync(id);
            if (measurement == null)
                return null;

            return new MeasurementResponseDTO
            {
                Name        = measurement.Name,
                Activity    = measurement.Activity,
                IsFall      = measurement.IsFall,
                IdMonitored = measurement.IdMonitored,
                Pulse       = measurement.Pulse,
                Temperature = measurement.Temperature,
                SpO2        = measurement.SpO2,
                Coordinates = measurement.Coordinates,
                CreatedAt   = measurement.CreatedAt
            };
        }

        // Numărul total de măsurători înregistrate azi — afișat pe dashboard-ul Admin
        public async Task<int> GetTodayMeasurementsCountAsync()
        {
            return await _measurementRepository.GetTodayMeasurementsCountAsync();
        }

        // Șterge măsurătorile mai vechi de cutoffDate pentru persoanele date (politică de retenție date)
        public async Task<int> DeleteMeasurementsOlderThanAsync(IEnumerable<Guid> monitoredIds, DateTime cutoffDate)
        {
            return await _measurementRepository.DeleteMeasurementsOlderThanAsync(monitoredIds, cutoffDate);
        }
    }
}