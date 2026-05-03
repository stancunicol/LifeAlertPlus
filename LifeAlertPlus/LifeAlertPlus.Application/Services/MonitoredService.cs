using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;

namespace LifeAlertPlus.Application.Services
{
    public class MonitoredService : IMonitoredService
    {
        private readonly IMonitoredRepository _monitoredRepository;

        public MonitoredService(IMonitoredRepository monitoredRepository)
        {
            _monitoredRepository = monitoredRepository;
        }

        public async Task<Monitored> AddMonitoredPersonAsync(MonitorCreateRequestDTO monitoredPersonDto)
        {
            var monitoredPerson = new Monitored
            {
                Id = Guid.NewGuid(),
                FirstName = monitoredPersonDto.FirstName,
                LastName = monitoredPersonDto.LastName,
                Birthdate = monitoredPersonDto.Birthdate,
                Gender = monitoredPersonDto.Gender,
                Address = monitoredPersonDto.Address,
                DeviceSerialNumber = monitoredPersonDto.DeviceSerialNumber,
                MinHeartRate  = 60,
                MaxHeartRate  = 100,
                MinTemperature = 36.0,
                MaxTemperature = 37.5,
                CreatedAt = DateTime.UtcNow
            };

            return await _monitoredRepository.AddMonitoredPersonAsync(monitoredPerson);
        }

        public async Task DeleteMonitoredPersonAsync(Guid id)
        {
            await _monitoredRepository.DeleteMonitoredPersonAsync(id);
        }

        public async Task<IEnumerable<Monitored>> GetAllMonitoredPeopleAsync()
        {
            return await _monitoredRepository.GetAllMonitoredPeopleAsync();
        }

        public async Task<Monitored?> GetMonitoredPersonByDeviceSerialNumberAsync(string deviceSerialNumber)
        {
            return await _monitoredRepository.GetMonitoredPersonByDeviceSerialNumberAsync(deviceSerialNumber);
        }

        public async Task<Monitored?> GetMonitoredPersonByIdAsync(Guid id)
        {
            return await _monitoredRepository.GetMonitoredPersonByIdAsync(id);
        }

        public async Task UpdateMonitoredPersonAsync(Monitored monitoredPerson)
        {
            await _monitoredRepository.UpdateMonitoredPersonAsync(monitoredPerson);
        }
    }
}