// This file is deprecated. Use LifeAlertPlus.Shared.Helpers.SimulationConstants instead.
// Keeping for backward compatibility.

namespace LifeAlertPlus.API.Services
{
    /// <summary>
    /// Configuration constants for simulation timing (server-side only)
    /// For data generation constants, use LifeAlertPlus.Shared.Helpers.SimulationConstants
    /// </summary>
    public static class SimulationConfig
    {
        // Timing (server-side configuration)
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

        // Data generation constants moved to Shared project
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
        public const string MockGPSData = LifeAlertPlus.Shared.Helpers.SimulationConstants.MockGPSData;
    }
}
