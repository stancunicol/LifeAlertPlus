using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LifeAlertPlus.API.Services
{
    public class ConditionRuleEngine
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ConditionRuleEngine> _logger;
        private readonly ConcurrentDictionary<Guid, (List<string> Keys, DateTime CachedAt)> _conditionCache = new();
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(2);

        public ConditionRuleEngine(IServiceScopeFactory scopeFactory, ILogger<ConditionRuleEngine> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public void InvalidateCache(Guid monitoredId) => _conditionCache.TryRemove(monitoredId, out _);

        private async Task<List<string>> GetConditionsAsync(Guid monitoredId)
        {
            if (_conditionCache.TryGetValue(monitoredId, out var cached) && (DateTime.UtcNow - cached.CachedAt) < CacheTtl)
                return cached.Keys;

            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<Domain.IRepositories.IMonitoredConditionRepository>();
            var conditions = await repo.GetByMonitoredIdAsync(monitoredId);
            var keys = conditions.Select(c => c.ConditionKey).ToList();
            _conditionCache[monitoredId] = (keys, DateTime.UtcNow);
            return keys;
        }

        public async Task<(AlertSeverity AdjustedSeverity, List<string> Recommendations, bool ImmediateAction)> EvaluateAsync(
            Guid monitoredId, double pulse, double temp, double spo2, bool isFall, AlertSeverity baseSeverity)
        {
            List<string> conditions;
            try
            {
                conditions = await GetConditionsAsync(monitoredId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load conditions for {MonitoredId}. Using base severity.", monitoredId);
                return (baseSeverity, new List<string>(), false);
            }

            if (conditions.Count == 0)
                return (baseSeverity, new List<string>(), false);

            var severity = baseSeverity;
            var recommendations = new List<string>();
            bool immediateAction = false;

            foreach (var key in conditions)
            {
                var (condSeverity, recs, immediate) = EvaluateCondition(key, pulse, temp, spo2, isFall);
                if (condSeverity > severity) severity = condSeverity;
                recommendations.AddRange(recs);
                if (immediate) immediateAction = true;
            }

            return (severity, recommendations, immediateAction);
        }

        private static (AlertSeverity Severity, List<string> Recommendations, bool ImmediateAction) EvaluateCondition(
            string conditionKey, double pulse, double temp, double spo2, bool isFall)
        {
            var recs = new List<string>();
            var severity = AlertSeverity.Normal;
            bool immediateAction = false;

            switch (conditionKey)
            {
                case "hypertension":
                    if (pulse > 135)
                    {
                        severity = AlertSeverity.Critical;
                        recs.Add("CRITIC [HTA]: Puls extrem de ridicat la pacient hipertensiv. Verificați tensiunea arterială imediat și contactați serviciul de urgență.");
                    }
                    else if (pulse > 100 || (temp > 38.0 && pulse > 95))
                    {
                        severity = AlertSeverity.Alert;
                        recs.Add("[HTA]: Puls crescut la pacient hipertensiv. Monitorizați tensiunea arterială și evitați efortul fizic.");
                    }
                    break;

                case "arrhythmia":
                    if (pulse > 140 || (pulse > 0 && pulse < 42))
                    {
                        severity = AlertSeverity.Critical;
                        recs.Add("CRITIC [Aritmie]: Frecvență cardiacă critică. Verificați imediat pacientul și contactați cardiologul sau apelați 112.");
                    }
                    else if (pulse > 110 || (pulse > 0 && pulse < 52))
                    {
                        severity = AlertSeverity.Alert;
                        recs.Add("[Aritmie]: Ritm cardiac anormal posibil. Monitorizați atent pacientul și pregătiți-vă să apelați 112.");
                    }
                    break;

                case "heart_failure":
                    if (spo2 > 0 && spo2 < 90 && pulse > 100)
                    {
                        severity = AlertSeverity.Critical;
                        recs.Add("CRITIC [Insuf. cardiacă]: Saturație de oxigen critică combinată cu tahicardie. Decompensare cardiacă posibilă. Apelați IMEDIAT 112.");
                    }
                    else if (spo2 > 0 && spo2 < 93)
                    {
                        severity = AlertSeverity.Alert;
                        recs.Add("[Insuf. cardiacă]: SpO2 scăzută. Monitorizați respirația și poziționați pacientul semișezând.");
                    }
                    break;

                case "mi_risk":
                    if (pulse > 130)
                    {
                        severity = AlertSeverity.Critical;
                        recs.Add("CRITIC [Risc IM]: Puls foarte ridicat la pacient cu risc de infarct. Apelați IMEDIAT 112. Administrați aspirină dacă nu este contraindicată.");
                    }
                    else if (pulse > 110 && temp > 37.8)
                    {
                        severity = AlertSeverity.Alert;
                        recs.Add("[Risc IM]: Markeri de stres cardiac detectați. Monitorizați atent și pregătiți-vă să apelați 112.");
                    }
                    break;

                case "asthma":
                    if (spo2 > 0 && spo2 < 88)
                    {
                        severity = AlertSeverity.Critical;
                        recs.Add("CRITIC [Astm bronșic]: SpO2 critic de scăzută. Criză de astm severă posibilă. Administrați bronhodilatator și apelați IMEDIAT 112.");
                    }
                    else if (spo2 > 0 && spo2 < 93)
                    {
                        severity = AlertSeverity.Alert;
                        recs.Add("[Astm bronșic]: SpO2 scăzută. Verificați dacă pacientul are acces la inhalator și monitorizați respirația.");
                    }
                    break;

                case "copd":
                    if (spo2 > 0 && spo2 < 86)
                    {
                        severity = AlertSeverity.Critical;
                        recs.Add("CRITIC [BPOC]: Insuficiență respiratorie acută posibilă. Saturație de oxigen extrem de scăzută. Apelați IMEDIAT 112.");
                    }
                    else if (spo2 > 0 && spo2 < 91)
                    {
                        severity = AlertSeverity.Alert;
                        recs.Add("[BPOC]: SpO2 sub pragul optim pentru BPOC. Monitorizați respirația și administrați oxigen suplimentar dacă disponibil.");
                    }
                    break;

                case "parkinson":
                    if (isFall)
                    {
                        severity = AlertSeverity.Critical;
                        immediateAction = true;
                        recs.Add("CRITIC [Parkinson]: Cădere detectată la pacient cu Parkinson. Risc crescut de fractură de șold. Verificați IMEDIAT pacientul și apelați 112.");
                    }
                    break;

                case "epilepsy":
                    if (isFall)
                    {
                        severity = AlertSeverity.Critical;
                        immediateAction = true;
                        recs.Add("CRITIC [Epilepsie]: Posibilă criză epileptică. Puneți pacientul în poziție de siguranță (decubit lateral). Apelați 112 dacă criza depășește 5 minute.");
                    }
                    break;

                case "diabetes":
                    if (pulse > 100 && temp > 0 && temp < 36.5)
                    {
                        severity = AlertSeverity.Alert;
                        recs.Add("[Diabet]: Posibilă hipoglicemie (puls crescut, temperatură scăzută). Verificați glicemia imediat. Administrați glucoză dacă pacientul este conștient.");
                    }
                    else if (temp > 38.0)
                    {
                        if (severity < AlertSeverity.Alert) severity = AlertSeverity.Alert;
                        recs.Add("[Diabet]: Febră la pacient diabetic – risc crescut de infecție. Monitorizați glicemia și consultați medicul.");
                    }
                    break;
            }

            return (severity, recs, immediateAction);
        }
    }
}
