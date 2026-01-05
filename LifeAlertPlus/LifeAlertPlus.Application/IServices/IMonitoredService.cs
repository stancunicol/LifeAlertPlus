using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Shared.DTOs.Requests.Monitored;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LifeAlertPlus.Application.IServices
{
    public interface IMonitoredService
    {
        Task<Monitored> AddMonitoredPersonAsync(MonitorCreateRequestDTO monitoredPersonDto);
        Task<Monitored> GetMonitoredPersonByDeviceSerialNumberAsync(string deviceSerialNumber);
        Task<Monitored> GetMonitoredPersonByIdAsync(Guid id);
        Task<IEnumerable<Monitored>> GetAllMonitoredPeopleAsync();
        Task UpdateMonitoredPersonAsync(Monitored monitoredPerson);
        Task DeleteMonitoredPersonAsync(Guid id);
    }
}