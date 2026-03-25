using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Shared.DTOs.Responses.Measurement;

namespace LifeAlertPlus.Application.Services
{
    public class MeasurementService : IMeasurementService
    {
        private readonly IMeasurementRepository _measurementRepository;

        public MeasurementService(IMeasurementRepository measurementRepository)
        {
            _measurementRepository = measurementRepository;
        }

        public async Task AddMeasurementAsync(Measurement measurement)
        {
            await _measurementRepository.AddMeasurementAsync(measurement);
        }

        public async Task<IEnumerable<MeasurementResponseDTO>?> GetMeasurementsByMonitoredIdAsync(Guid idMonitored, int pageNumber, int pageSize)
        {
            var measurements = await _measurementRepository.GetMeasurementsByMonitoredIdAsync(idMonitored, pageNumber, pageSize);

            if(measurements == null || !measurements.Any())
                return Enumerable.Empty<MeasurementResponseDTO>();

            return measurements.Select(m => new MeasurementResponseDTO
            {
                Name = m.Name,
                Activity = m.Activity,
                IsFall = m.IsFall,
                IdMonitored = m.IdMonitored,
                Pulse = m.Pulse,
                Temperature = m.Temperature,
                Coordinates = m.Coordinates,
                CreatedAt = m.CreatedAt
            });
        }

        public async Task<MeasurementResponseDTO?> GetMeasurementByIdAsync(Guid id)
        {
            var measurement = await _measurementRepository.GetMeasurementByIdAsync(id);
            if (measurement == null)
                return null;

            return new MeasurementResponseDTO
            {
                Name = measurement.Name,
                Activity = measurement.Activity,
                IsFall = measurement.IsFall,
                IdMonitored = measurement.IdMonitored,
                Pulse = measurement.Pulse,
                Temperature = measurement.Temperature,
                Coordinates = measurement.Coordinates,
                CreatedAt = measurement.CreatedAt
            };
        }

        public async Task<int> GetTodayMeasurementsCountAsync()
        {
            return await _measurementRepository.GetTodayMeasurementsCountAsync();
        }
    }
}