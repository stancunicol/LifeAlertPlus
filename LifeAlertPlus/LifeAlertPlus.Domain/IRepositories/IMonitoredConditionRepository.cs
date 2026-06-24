using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Domain.IRepositories
{
    // Interfață repository pentru tabela MonitoredConditions (afecțiunile diagnosticate ale pacientului)
    // Cheile de afecțiuni (ex: "copd", "diabetes") sunt folosite de ConditionThresholdAdjuster
    // pentru a personaliza pragurile vitale și de AlertMonitorService pentru penalizări HealthScore
    public interface IMonitoredConditionRepository
    {
        Task<IEnumerable<MonitoredCondition>> GetByMonitoredIdAsync(Guid monitoredId);    // Returnează toate afecțiunile diagnosticate ale unei persoane monitorizate
        Task ReplaceAllAsync(Guid monitoredId, IEnumerable<string> conditionKeys);        // Înlocuiește complet lista de afecțiuni (DELETE + INSERT) — simplă față de diff individual
    }
}
