using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LifeAlertPlus.API.Services
{
    // Motor de reguli medicale: evaluează semnele vitale în contextul bolilor cunoscute ale pacientului
    // și ajustează severitatea alertei, adăugând recomandări specifice fiecărei afecțiuni
    public class ConditionRuleEngine
    {
        private readonly IServiceScopeFactory _scopeFactory; // Folosit pentru a accesa DB dintr-un Singleton
        private readonly ILogger<ConditionRuleEngine> _logger;
        // Cache thread-safe: pentru fiecare persoană monitorizată stocăm lista bolilor sale și momentul cache-ării
        private readonly ConcurrentDictionary<Guid, (List<string> Keys, DateTime CachedAt)> _conditionCache = new();
        // Durata de valabilitate a cache-ului: 2 ore (bolile se schimbă rar)
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(2);

        public ConditionRuleEngine(IServiceScopeFactory scopeFactory, ILogger<ConditionRuleEngine> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        // Invalidează cache-ul pentru o persoană monitorizată (apelat când bolile sunt modificate)
        public void InvalidateCache(Guid monitoredId) => _conditionCache.TryRemove(monitoredId, out _);

        // Returnează lista de boli (condition keys) pentru o persoană, cu cache în memorie
        private async Task<List<string>> GetConditionsAsync(Guid monitoredId)
        {
            // Dacă avem date în cache și nu au expirat, le returnăm fără a accesa DB
            if (_conditionCache.TryGetValue(monitoredId, out var cached) && (DateTime.UtcNow - cached.CachedAt) < CacheTtl)
                return cached.Keys;

            // Cache expirat sau absent — citim din DB
            using var scope = _scopeFactory.CreateScope(); // Singleton nu poate injecta Scoped direct
            var repo = scope.ServiceProvider.GetRequiredService<Domain.IRepositories.IMonitoredConditionRepository>();
            var conditions = await repo.GetByMonitoredIdAsync(monitoredId);
            // Extragem doar cheile (ex: "hypertension", "diabetes") nu obiectele complete
            var keys = conditions.Select(c => c.ConditionKey).ToList();
            // Actualizăm cache-ul cu datele proaspete
            _conditionCache[monitoredId] = (keys, DateTime.UtcNow);
            return keys;
        }

        // Metodă principală: evaluează semnele vitale față de fiecare boală a pacientului
        // Returnează: severitatea ajustată, lista de recomandări medicale, și dacă e nevoie de acțiune imediată
        public async Task<(AlertSeverity AdjustedSeverity, List<string> Recommendations, bool ImmediateAction)> EvaluateAsync(
            Guid monitoredId, double pulse, double temp, double spo2, bool isFall, AlertSeverity baseSeverity)
        {
            List<string> conditions;
            try
            {
                conditions = await GetConditionsAsync(monitoredId); // Obținem bolile pacientului
            }
            catch (Exception ex)
            {
                // Dacă nu putem citi bolile, folosim severitatea de bază fără ajustări
                _logger.LogWarning(ex, "Failed to load conditions for {MonitoredId}. Using base severity.", monitoredId);
                return (baseSeverity, new List<string>(), false);
            }

            // Dacă pacientul nu are boli înregistrate, nu ajustăm nimic
            if (conditions.Count == 0)
                return (baseSeverity, new List<string>(), false);

            // Pornim cu severitatea de bază și o putem doar crește (nu o reducem)
            var severity = baseSeverity;
            var recommendations = new List<string>();
            bool immediateAction = false;

            // Evaluăm fiecare boală separat și acumulăm severitatea maximă + toate recomandările
            foreach (var key in conditions)
            {
                var (condSeverity, recs, immediate) = EvaluateCondition(key, pulse, temp, spo2, isFall);
                if (condSeverity > severity) severity = condSeverity; // Luăm severitatea maximă
                recommendations.AddRange(recs); // Adăugăm recomandările acestei boli
                if (immediate) immediateAction = true; // Dacă orice boală necesită acțiune imediată, setăm flag-ul
            }

            return (severity, recommendations, immediateAction);
        }

        // Evaluează o singură boală față de semnele vitale curente
        // Returnează severitatea specifică bolii, recomandări și dacă e nevoie de acțiune imediată
        private static (AlertSeverity Severity, List<string> Recommendations, bool ImmediateAction) EvaluateCondition(
            string conditionKey, double pulse, double temp, double spo2, bool isFall)
        {
            var recs = new List<string>();
            var severity = AlertSeverity.Normal;
            bool immediateAction = false;

            switch (conditionKey)
            {
                // HIPERTENSIUNE ARTERIALĂ: puls crescut este mai periculos la hipertensivi
                case "hypertension":
                    if (pulse > 135)
                    {
                        severity = AlertSeverity.Critical;
                        recs.Add("CRITIC [HTA]: Puls extrem de ridicat la pacient hipertensiv. Verificați tensiunea arterială imediat și contactați serviciul de urgență.");
                    }
                    else if (pulse > 100 || (temp > 38.0 && pulse > 95))
                    {
                        // Febra + puls crescut = stres cardiovascular suplimentar la hipertensivi
                        severity = AlertSeverity.Alert;
                        recs.Add("[HTA]: Puls crescut la pacient hipertensiv. Monitorizați tensiunea arterială și evitați efortul fizic.");
                    }
                    break;

                // ARITMIE: praguri mai stricte pentru frecvența cardiacă
                case "arrhythmia":
                    if (pulse > 140 || (pulse > 0 && pulse < 42))
                    {
                        // Frecvențe extreme pot indica fibrilație ventriculară sau bloc cardiac
                        severity = AlertSeverity.Critical;
                        recs.Add("CRITIC [Aritmie]: Frecvență cardiacă critică. Verificați imediat pacientul și contactați cardiologul sau apelați 112.");
                    }
                    else if (pulse > 110 || (pulse > 0 && pulse < 52))
                    {
                        severity = AlertSeverity.Alert;
                        recs.Add("[Aritmie]: Ritm cardiac anormal posibil. Monitorizați atent pacientul și pregătiți-vă să apelați 112.");
                    }
                    break;

                // INSUFICIENȚĂ CARDIACĂ: combinația SpO2 scăzut + tahicardie = decompensare acută
                case "heart_failure":
                    if (spo2 > 0 && spo2 < 90 && pulse > 100)
                    {
                        // SpO2 sub 90% + puls rapid = edem pulmonar acut posibil
                        severity = AlertSeverity.Critical;
                        recs.Add("CRITIC [Insuf. cardiacă]: Saturație de oxigen critică combinată cu tahicardie. Decompensare cardiacă posibilă. Apelați IMEDIAT 112.");
                    }
                    else if (spo2 > 0 && spo2 < 93)
                    {
                        severity = AlertSeverity.Alert;
                        recs.Add("[Insuf. cardiacă]: SpO2 scăzută. Monitorizați respirația și poziționați pacientul semișezând.");
                    }
                    break;

                // RISC DE INFARCT MIOCARDIC: orice stres cardiac e potențial fatal
                case "mi_risk":
                    if (pulse > 130)
                    {
                        severity = AlertSeverity.Critical;
                        recs.Add("CRITIC [Risc IM]: Puls foarte ridicat la pacient cu risc de infarct. Apelați IMEDIAT 112. Administrați aspirină dacă nu este contraindicată.");
                    }
                    else if (pulse > 110 && temp > 37.8)
                    {
                        // Febra crește cererea de oxigen a inimii — periculos la pacienții cu risc IM
                        severity = AlertSeverity.Alert;
                        recs.Add("[Risc IM]: Markeri de stres cardiac detectați. Monitorizați atent și pregătiți-vă să apelați 112.");
                    }
                    break;

                // ASTM BRONȘIC: SpO2 scăzut poate indica criză astmatică
                case "asthma":
                    if (spo2 > 0 && spo2 < 88)
                    {
                        // SpO2 sub 88% = criză severă, potențial fatală fără bronhodilatator
                        severity = AlertSeverity.Critical;
                        recs.Add("CRITIC [Astm bronșic]: SpO2 critic de scăzută. Criză de astm severă posibilă. Administrați bronhodilatator și apelați IMEDIAT 112.");
                    }
                    else if (spo2 > 0 && spo2 < 93)
                    {
                        severity = AlertSeverity.Alert;
                        recs.Add("[Astm bronșic]: SpO2 scăzută. Verificați dacă pacientul are acces la inhalator și monitorizați respirația.");
                    }
                    break;

                // BPOC (Bronhopneumopatie Obstructivă Cronică): praguri de SpO2 mai permisive decât normalul
                case "copd":
                    if (spo2 > 0 && spo2 < 86)
                    {
                        // La BPOC, valori sub 88% sunt critice (pragul lor "normal" e mai jos)
                        severity = AlertSeverity.Critical;
                        recs.Add("CRITIC [BPOC]: Insuficiență respiratorie acută posibilă. Saturație de oxigen extrem de scăzută. Apelați IMEDIAT 112.");
                    }
                    else if (spo2 > 0 && spo2 < 91)
                    {
                        severity = AlertSeverity.Alert;
                        recs.Add("[BPOC]: SpO2 sub pragul optim pentru BPOC. Monitorizați respirația și administrați oxigen suplimentar dacă disponibil.");
                    }
                    break;

                // PARKINSON: căderile sunt deosebit de periculoase (risc fractură de șold)
                case "parkinson":
                    if (isFall)
                    {
                        severity = AlertSeverity.Critical;
                        immediateAction = true; // Bypass-ăm toate cooldown-urile — notificăm imediat
                        recs.Add("CRITIC [Parkinson]: Cădere detectată la pacient cu Parkinson. Risc crescut de fractură de șold. Verificați IMEDIAT pacientul și apelați 112.");
                    }
                    break;

                // EPILEPSIE: căderea poate fi o criză epileptică în desfășurare
                case "epilepsy":
                    if (isFall)
                    {
                        severity = AlertSeverity.Critical;
                        immediateAction = true; // Notificare imediată — criza poate fi periculoasă
                        recs.Add("CRITIC [Epilepsie]: Posibilă criză epileptică. Puneți pacientul în poziție de siguranță (decubit lateral). Apelați 112 dacă criza depășește 5 minute.");
                    }
                    break;

                // DIABET: puls crescut + temperatură scăzută poate indica hipoglicemie
                case "diabetes":
                    if (pulse > 100 && temp > 0 && temp < 36.5)
                    {
                        // Hipoglicemia provoacă tahicardie și transpirații reci (temperatură scăzută)
                        severity = AlertSeverity.Alert;
                        recs.Add("[Diabet]: Posibilă hipoglicemie (puls crescut, temperatură scăzută). Verificați glicemia imediat. Administrați glucoză dacă pacientul este conștient.");
                    }
                    else if (temp > 38.0)
                    {
                        // Infecțiile sunt mai grave la diabetici și înrăutățesc controlul glicemic
                        if (severity < AlertSeverity.Alert) severity = AlertSeverity.Alert;
                        recs.Add("[Diabet]: Febră la pacient diabetic – risc crescut de infecție. Monitorizați glicemia și consultați medicul.");
                    }
                    break;
            }

            return (severity, recs, immediateAction);
        }
    }
}
