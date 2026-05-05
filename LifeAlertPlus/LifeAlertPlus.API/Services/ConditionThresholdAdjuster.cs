namespace LifeAlertPlus.API.Services
{
    public static class ConditionThresholdAdjuster
    {
        private record ThresholdProfile(int MinHr, int MaxHr, double MinTemp, double MaxTemp, int MinSpO2, int MaxSpO2);

        private static readonly Dictionary<string, ThresholdProfile> Profiles = new()
        {
            // MinHr, MaxHr, MinTemp, MaxTemp, MinSpO2, MaxSpO2
            ["heart_failure"] = new(55, 110, 36.0, 37.5, 92, 100),
            ["arrhythmia"]    = new(40, 130, 36.0, 37.5, 95, 100),
            ["copd"]          = new(60, 110, 36.0, 37.5, 88, 100),
            ["asthma"]        = new(60, 110, 36.0, 37.5, 90, 100),
            ["hypertension"]  = new(60, 100, 36.0, 37.5, 95, 100),
            ["diabetes"]      = new(60, 100, 36.0, 37.2, 95, 100),
            ["parkinson"]     = new(55, 105, 36.0, 37.5, 95, 100),
            ["mi_risk"]       = new(60, 100, 36.0, 37.5, 93, 100),
            ["epilepsy"]      = new(60, 120, 36.0, 37.5, 95, 100),
        };

        private const int DefaultMinHr    = 60;
        private const int DefaultMaxHr    = 100;
        private const double DefaultMinTemp = 36.0;
        private const double DefaultMaxTemp = 37.5;
        private const int DefaultMinSpO2  = 95;
        private const int DefaultMaxSpO2  = 100;

        /// <summary>
        /// Calculates recommended threshold values for a patient given their conditions.
        /// HR: most permissive range (lowest min, highest max) — avoids false alerts for
        /// patients with naturally wider ranges.
        /// Temperature: most conservative upper bound — earlier fever detection is safer.
        /// SpO2: most permissive minimum — COPD/asthma patients tolerate lower SpO2 baselines.
        /// </summary>
        public static (int MinHr, int MaxHr, double MinTemp, double MaxTemp, int MinSpO2, int MaxSpO2) Calculate(
            IEnumerable<string> conditions)
        {
            var keys = conditions.ToList();
            if (keys.Count == 0)
                return (DefaultMinHr, DefaultMaxHr, DefaultMinTemp, DefaultMaxTemp, DefaultMinSpO2, DefaultMaxSpO2);

            int minHr      = DefaultMinHr;
            int maxHr      = DefaultMaxHr;
            double minTemp = DefaultMinTemp;
            double maxTemp = DefaultMaxTemp;
            int minSpO2    = DefaultMinSpO2;
            int maxSpO2    = DefaultMaxSpO2;

            foreach (var key in keys)
            {
                if (!Profiles.TryGetValue(key, out var p)) continue;
                if (p.MinHr   < minHr)   minHr   = p.MinHr;
                if (p.MaxHr   > maxHr)   maxHr   = p.MaxHr;
                if (p.MinTemp < minTemp) minTemp = p.MinTemp;
                if (p.MaxTemp < maxTemp) maxTemp = p.MaxTemp; // most conservative
                if (p.MinSpO2 < minSpO2) minSpO2 = p.MinSpO2; // most permissive
            }

            return (minHr, maxHr, minTemp, maxTemp, minSpO2, maxSpO2);
        }
    }
}
