namespace LifeAlertPlus.API.Services
{
    public static class ConditionThresholdAdjuster
    {
        private record ThresholdProfile(int MinHr, int MaxHr, double MinTemp, double MaxTemp);

        private static readonly Dictionary<string, ThresholdProfile> Profiles = new()
        {
            ["heart_failure"] = new(55, 110, 36.0, 37.5),
            ["arrhythmia"]    = new(40, 130, 36.0, 37.5),
            ["copd"]          = new(60, 110, 36.0, 37.5),
            ["asthma"]        = new(60, 110, 36.0, 37.5),
            ["hypertension"]  = new(60, 100, 36.0, 37.5),
            ["diabetes"]      = new(60, 100, 36.0, 37.2),
            ["parkinson"]     = new(55, 105, 36.0, 37.5),
            ["mi_risk"]       = new(60, 100, 36.0, 37.5),
            ["epilepsy"]      = new(60, 120, 36.0, 37.5),
        };

        private const int DefaultMinHr   = 60;
        private const int DefaultMaxHr   = 100;
        private const double DefaultMinTemp = 36.0;
        private const double DefaultMaxTemp = 37.5;

        /// <summary>
        /// Calculates the recommended threshold values for a patient given their conditions.
        /// Uses the most permissive range on HR (to avoid false alerts for patients with
        /// naturally wider ranges) and the most conservative upper temperature bound (earlier
        /// fever detection is safer).
        /// Returns standard clinical values when no conditions are present.
        /// </summary>
        public static (int MinHr, int MaxHr, double MinTemp, double MaxTemp) Calculate(
            IEnumerable<string> conditions)
        {
            var keys = conditions.ToList();
            if (keys.Count == 0)
                return (DefaultMinHr, DefaultMaxHr, DefaultMinTemp, DefaultMaxTemp);

            int minHr   = DefaultMinHr;
            int maxHr   = DefaultMaxHr;
            double minTemp = DefaultMinTemp;
            double maxTemp = DefaultMaxTemp;

            foreach (var key in keys)
            {
                if (!Profiles.TryGetValue(key, out var p)) continue;
                if (p.MinHr   < minHr)   minHr   = p.MinHr;
                if (p.MaxHr   > maxHr)   maxHr   = p.MaxHr;
                if (p.MinTemp < minTemp) minTemp = p.MinTemp;
                if (p.MaxTemp < maxTemp) maxTemp = p.MaxTemp; // most conservative
            }

            return (minHr, maxHr, minTemp, maxTemp);
        }
    }
}
