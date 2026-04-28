using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    public interface IActivityProfileRepository
    {
        Task<IEnumerable<ActivityProfile>> GetByMonitoredIdAsync(Guid monitoredId);
        Task UpsertAsync(ActivityProfile profile);
        Task DeleteByMonitoredIdAsync(Guid monitoredId);
    }
}
