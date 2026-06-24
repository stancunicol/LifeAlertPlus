// FIȘIER DEPRECAT — Folosește LifeAlertPlus.Shared.Helpers.SimulationConstants în locul acestuia.
// Păstrat pentru compatibilitate inversă cu codul vechi care referențiază SimulationConfig direct.

namespace LifeAlertPlus.API.Services
{
    /// <summary>
    /// Constante de configurare pentru simulare (doar server-side).
    /// Constantele pentru generarea datelor au fost mutate în proiectul Shared.
    /// </summary>
    public static class SimulationConfig
    {
        // Intervalul de timp dintre două citiri simulate trimise de ESP virtual (30 secunde)
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(30);
        // Timpul maxim de așteptare la oprirea unui loop de simulare (5 secunde)
        public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

        // Constantele pentru generarea valorilor simulate au fost mutate în Shared
        // pentru a putea fi accesate și de clientul Blazor WASM (ex. pentru afișare valori limită)
        public const int PulseMin = LifeAlertPlus.Shared.Helpers.SimulationConstants.PulseMin;
        public const int PulseMax = LifeAlertPlus.Shared.Helpers.SimulationConstants.PulseMax;
        public const int SpO2Min = LifeAlertPlus.Shared.Helpers.SimulationConstants.SpO2Min;
        public const int SpO2Max = LifeAlertPlus.Shared.Helpers.SimulationConstants.SpO2Max;
        public const double TemperatureMin = LifeAlertPlus.Shared.Helpers.SimulationConstants.TemperatureMin;
        public const double TemperatureRange = LifeAlertPlus.Shared.Helpers.SimulationConstants.TemperatureRange;
        public const double BatteryMin = LifeAlertPlus.Shared.Helpers.SimulationConstants.BatteryMin;
        public const double BatteryRange = LifeAlertPlus.Shared.Helpers.SimulationConstants.BatteryRange;
        public const int AccelerometerMin = LifeAlertPlus.Shared.Helpers.SimulationConstants.AccelerometerMin;
        public const int AccelerometerMax = LifeAlertPlus.Shared.Helpers.SimulationConstants.AccelerometerMax;
        public const int GyroMin = LifeAlertPlus.Shared.Helpers.SimulationConstants.GyroMin;
        public const int GyroMax = LifeAlertPlus.Shared.Helpers.SimulationConstants.GyroMax;
        public const string MockGPSData = LifeAlertPlus.Shared.Helpers.SimulationConstants.MockGPSData; // Coordonate GPS simulate (fixe)
    }
}
