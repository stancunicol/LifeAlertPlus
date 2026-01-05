using LifeAlertPlus.Domain.Entities;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

namespace LifeAlertPlus.Domain.IRepositories
{
    public interface IMonitoredRepository
    {
        Task<Monitored> AddMonitoredPersonAsync(Monitored monitoredPerson);
        Task<Monitored> GetMonitoredPersonByDeviceSerialNumberAsync(string deviceSerialNumber);
        Task<Monitored> GetMonitoredPersonByIdAsync(Guid id);
        Task<IEnumerable<Monitored>> GetAllMonitoredPeopleAsync();
        Task UpdateMonitoredPersonAsync(Monitored monitoredPerson);
        Task DeleteMonitoredPersonAsync(Guid id);
    }
}