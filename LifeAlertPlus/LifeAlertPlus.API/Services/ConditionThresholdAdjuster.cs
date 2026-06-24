namespace LifeAlertPlus.API.Services
{
    // Clasă statică care calculează pragurile recomandate de alerte vitale
    // pe baza bolilor diagnosticate ale pacientului.
    // Este apelată din MonitoredConditionController (la modificarea condițiilor medicale)
    // și din AlertMonitorService (la ingestia datelor ESP).
    // Scopul: să evite falsele alarme cauzate de valori normale pentru un anumit diagnostic
    // (ex: un pacient cu aritmie poate avea pulsul natural la 40 bpm fără a fi în pericol).
    public static class ConditionThresholdAdjuster
    {
        // Profil de praguri pentru fiecare boală: (MinHr, MaxHr, MinTemp, MaxTemp, MinSpO2, MaxSpO2)
        private record ThresholdProfile(int MinHr, int MaxHr, double MinTemp, double MaxTemp, int MinSpO2, int MaxSpO2);

        // Profiluri clinice per boală — bazate pe standarde medicale
        private static readonly Dictionary<string, ThresholdProfile> Profiles = new()
        {
            // Cheie boală     MinHr  MaxHr  MinTemp  MaxTemp  MinSpO2  MaxSpO2
            ["heart_failure"] = new(55, 110, 36.0, 37.5, 92,  100), // Insuficiență cardiacă: ritm mai lent permis
            ["arrhythmia"]    = new(40, 130, 36.0, 37.5, 95,  100), // Aritmie: variații extreme de puls normale
            ["copd"]          = new(60, 110, 36.0, 37.5, 88,  100), // BPOC: SpO2 mai mic acceptabil (88%)
            ["asthma"]        = new(60, 110, 36.0, 37.5, 90,  100), // Astm: SpO2 minim 90%
            ["hypertension"]  = new(60, 100, 36.0, 37.5, 95,  100), // Hipertensiune: puls în limite normale
            ["diabetes"]      = new(60, 100, 36.0, 37.2, 95,  100), // Diabet: temp maximă mai conservatoare (37.2)
            ["parkinson"]     = new(55, 105, 36.0, 37.5, 95,  100), // Parkinson: puls minim mai permisiv
            ["mi_risk"]       = new(60, 100, 36.0, 37.5, 93,  100), // Risc infarct: SpO2 minim 93%
            ["epilepsy"]      = new(60, 120, 36.0, 37.5, 95,  100), // Epilepsie: puls maxim mai ridicat
        };

        // Valorile implicite pentru pacienți fără boli diagnosticate
        private const int DefaultMinHr      = 60;
        private const int DefaultMaxHr      = 100;
        private const double DefaultMinTemp = 36.0;
        private const double DefaultMaxTemp = 37.5;
        private const int DefaultMinSpO2    = 95;
        private const int DefaultMaxSpO2    = 100;

        // Calculează pragurile recomandate pentru un pacient cu mai multe boli.
        // Strategia de combinare:
        //   - Puls (HR):   cel MAI permisiv interval (min cel mai mic, max cel mai mare)
        //                  → evităm false alarme pentru pacienți cu variații naturale mari
        //   - Temperatură: limita superioară cea MAI conservatoare (cea mai mică valoare MaxTemp)
        //                  → detectăm febra mai devreme (siguranță > confort)
        //   - SpO2:        minimul cel MAI permisiv (cel mai mic MinSpO2)
        //                  → BPOC/astm au baseline mai scăzut, fără a declara alerte la valori normale
        public static (int MinHr, int MaxHr, double MinTemp, double MaxTemp, int MinSpO2, int MaxSpO2) Calculate(
            IEnumerable<string> conditions)
        {
            var keys = conditions.ToList();
            // Dacă nu are boli diagnosticate, returnăm valorile implicite standard
            if (keys.Count == 0)
                return (DefaultMinHr, DefaultMaxHr, DefaultMinTemp, DefaultMaxTemp, DefaultMinSpO2, DefaultMaxSpO2);

            // Inițializăm cu valorile implicite și le ajustăm per boală
            int minHr      = DefaultMinHr;
            int maxHr      = DefaultMaxHr;
            double minTemp = DefaultMinTemp;
            double maxTemp = DefaultMaxTemp;
            int minSpO2    = DefaultMinSpO2;
            int maxSpO2    = DefaultMaxSpO2;

            foreach (var key in keys)
            {
                if (!Profiles.TryGetValue(key, out var p)) continue; // Ignorăm bolile necunoscute
                if (p.MinHr   < minHr)   minHr   = p.MinHr;   // Luăm minimul cel mai mic (permisiv)
                if (p.MaxHr   > maxHr)   maxHr   = p.MaxHr;   // Luăm maximul cel mai mare (permisiv)
                if (p.MinTemp < minTemp) minTemp = p.MinTemp;  // Temperatura minimă nu variază semnificativ
                if (p.MaxTemp < maxTemp) maxTemp = p.MaxTemp;  // Temp maximă: cea mai conservatoare (cea mai mică)
                if (p.MinSpO2 < minSpO2) minSpO2 = p.MinSpO2; // SpO2 minim: cel mai permisiv (cel mai mic)
            }

            return (minHr, maxHr, minTemp, maxTemp, minSpO2, maxSpO2);
        }
    }
}
