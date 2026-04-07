using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using System.Globalization;

namespace LifeAlertPlus.Shared.Helpers
{
    /// <summary>
    /// Configuration constants for ESP data simulation
    /// </summary>
    public static class SimulationConstants
    {
        // Pulse (BPM)
        public const int PulseMin = 62;
        public const int PulseMax = 101;

        // SpO2 (%)
        public const int SpO2Min = 93;
        public const int SpO2Max = 99;

        // Temperature (°C)
        public const double TemperatureMin = 36.2;
        public const double TemperatureRange = 1.2; // 36.2 - 37.4

        // Battery (%)
        public const double BatteryMin = 30.0;
        public const double BatteryRange = 70.0; // 30-100

        // Accelerometer/Gyro
        public const int AccelerometerMin = -16000;
        public const int AccelerometerMax = 16001;
        public const int GyroMin = -5000;
        public const int GyroMax = 5001;

        // GPS mock data
        public const string MockGPSData = "$GPRMC,123519,A,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W*6A";
    }

    /// <summary>
    /// Generates simulated ESP device data
    /// </summary>
    public static class ESPDataGenerator
    {
        public static ESPDataResponseDTO GeneratePayload(string serial)
        {
            var rnd = Random.Shared;

            var pulse = rnd.Next(SimulationConstants.PulseMin, SimulationConstants.PulseMax);
            var spo2 = rnd.Next(SimulationConstants.SpO2Min, SimulationConstants.SpO2Max);
            var temp = SimulationConstants.TemperatureMin + (rnd.NextDouble() * SimulationConstants.TemperatureRange);
            var battery = SimulationConstants.BatteryMin + (rnd.NextDouble() * SimulationConstants.BatteryRange);

            // generate a random coordinate near a reasonable default (small area jitter)
            // (adjust baseLat/baseLon if you want a different simulation region)
            var baseLat = 44.4268; // Bucharest center as default
            var baseLon = 26.1025;
            var lat = baseLat + (rnd.NextDouble() - 0.5) * 0.2; // +/- 0.1 degrees
            var lon = baseLon + (rnd.NextDouble() - 0.5) * 0.2;
            var latStr = lat.ToString(CultureInfo.InvariantCulture);
            var lonStr = lon.ToString(CultureInfo.InvariantCulture);

            return new ESPDataResponseDTO
            {
                Serial = serial,
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsAvailable = true,
                Mpu6050 = new List<int>
                {
                    rnd.Next(SimulationConstants.AccelerometerMin, SimulationConstants.AccelerometerMax),
                    rnd.Next(SimulationConstants.AccelerometerMin, SimulationConstants.AccelerometerMax),
                    rnd.Next(SimulationConstants.AccelerometerMin, SimulationConstants.AccelerometerMax)
                },
                Gyro = new List<int>
                {
                    rnd.Next(SimulationConstants.GyroMin, SimulationConstants.GyroMax),
                    rnd.Next(SimulationConstants.GyroMin, SimulationConstants.GyroMax),
                    rnd.Next(SimulationConstants.GyroMin, SimulationConstants.GyroMax)
                },
                Max30100 = new List<int> { pulse, spo2 },
                // store coordinates as plain "lat,lon" (parsed by client map helpers)
                Neo6m = $"{latStr},{lonStr}",
                Temperature = Math.Round(temp, 1),
                Battery = Math.Round(battery, 1),
                ErrorMessage = null
            };
        }
    }
}
