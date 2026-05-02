using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    public interface IMonitoredConditionRepository
    {
        Task<IEnumerable<MonitoredCondition>> GetByMonitoredIdAsync(Guid monitoredId);
        Task ReplaceAllAsync(Guid monitoredId, IEnumerable<string> conditionKeys);
    }
}
