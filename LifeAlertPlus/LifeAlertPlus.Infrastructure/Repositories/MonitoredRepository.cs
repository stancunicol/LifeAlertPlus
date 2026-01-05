using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeAlertPlus.Infrastructure.Context;

namespace LifeAlertPlus.Infrastructure.Repositories
{
    public class MonitoredRepository : IMonitoredRepository
    {
        private readonly LifeAlertPlusDbContext _context;

        public MonitoredRepository(LifeAlertPlusDbContext context)
        {
            _context = context;
        }

        public async Task<Monitored> AddMonitoredPersonAsync(Monitored monitoredPerson)
        {
            _context.Monitoreds.Add(monitoredPerson);
            await _context.SaveChangesAsync();
            return monitoredPerson;
        }

        public async Task DeleteMonitoredPersonAsync(Guid id)
        {
            var monitoredPerson = await _context.Monitoreds.FindAsync(id);
            if (monitoredPerson != null)
            {
                _context.Monitoreds.Remove(monitoredPerson);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<IEnumerable<Monitored>> GetAllMonitoredPeopleAsync()
        {
            return await _context.Monitoreds.ToListAsync();
        }

        public async Task<Monitored> GetMonitoredPersonByDeviceSerialNumberAsync(string deviceSerialNumber)
        {
            return await _context.Monitoreds
                .FirstOrDefaultAsync(m => m.DeviceSerialNumber == deviceSerialNumber);
        }

        public async Task<Monitored> GetMonitoredPersonByIdAsync(Guid id)
        {
            return await _context.Monitoreds.FindAsync(id);
        }

        public async Task UpdateMonitoredPersonAsync(Monitored monitoredPerson)
        {
            _context.Monitoreds.Update(monitoredPerson);
            await _context.SaveChangesAsync();
        }
    }
}