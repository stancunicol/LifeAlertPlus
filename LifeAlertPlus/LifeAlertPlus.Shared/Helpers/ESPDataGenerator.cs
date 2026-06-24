using LifeAlertPlus.Shared.DTOs.Responses.ESP;
using System.Globalization;

namespace LifeAlertPlus.Shared.Helpers
{
    // Praguri folosite pentru a genera date "plauzibil normale" în modul simulare (fără dispozitiv ESP32 real) —
    // intervalele sunt alese explicit sub pragurile de alertă implicite, ca să nu declanșeze notificări false
    // în timpul demonstrațiilor/testării UI.
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

    // Generează payload-uri ESPDataResponseDTO false, fără un dispozitiv fizic — folosit de modul
    // de simulare (SimulationManager.cs din API, SimulationPage.razor.cs din Client) pentru demonstrații
    // și testare a fluxului de alertare fără hardware. Aceeași formă de date ca cea trimisă real de
    // firmware (main.cpp → build_json), ca restul sistemului (AlertMonitorService, AI, UI) să nu poată
    // distinge o măsurătoare simulată de una reală.
    public static class ESPDataGenerator
    {
        // Generează date la nivel de alertă: puls ridicat, SpO2 scăzut, temperatură crescută —
        // folosit pentru a testa vizual cum reacționează UI-ul/notificările la o stare de pericol
        public static ESPDataResponseDTO GenerateAlertPayload(string serial)
        {
            var rnd = Random.Shared;

            // Alert-level ranges
            var pulse = rnd.Next(125, 145);          // >120 => Alert
            var spo2 = rnd.Next(88, 94);             // <95 => Alert, <90 => Critical
            var temp = 38.6 + (rnd.NextDouble() * 1.0); // >38.5 => Alert, >39.5 => Critical
            var battery = SimulationConstants.BatteryMin + (rnd.NextDouble() * SimulationConstants.BatteryRange);

            var baseLat = 44.4268;
            var baseLon = 26.1025;
            var lat = baseLat + (rnd.NextDouble() - 0.5) * 0.2;
            var lon = baseLon + (rnd.NextDouble() - 0.5) * 0.2;

            return new ESPDataResponseDTO
            {
                Serial = serial,
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsAvailable = true,
                Bpm = pulse,
                Spo2 = spo2,
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
                Neo6m = $"{lat.ToString(CultureInfo.InvariantCulture)},{lon.ToString(CultureInfo.InvariantCulture)}",
                Temperature = Math.Round(temp, 1),
                Battery = Math.Round(battery, 1),
                ErrorMessage = null
            };
        }

        // Generează date "normale" (în intervalele din SimulationConstants) — folosit pentru simularea
        // de rutină, fără alerte, când utilizatorul doar vrea să vadă dashboard-ul populat cu date live
        public static ESPDataResponseDTO GeneratePayload(string serial)
        {
            var rnd = Random.Shared;

            var pulse = rnd.Next(SimulationConstants.PulseMin, SimulationConstants.PulseMax);
            var spo2 = rnd.Next(SimulationConstants.SpO2Min, SimulationConstants.SpO2Max);
            var temp = SimulationConstants.TemperatureMin + (rnd.NextDouble() * SimulationConstants.TemperatureRange);
            var battery = SimulationConstants.BatteryMin + (rnd.NextDouble() * SimulationConstants.BatteryRange);

            var baseLat = 44.4268;
            var baseLon = 26.1025;
            var lat = baseLat + (rnd.NextDouble() - 0.5) * 0.2;
            var lon = baseLon + (rnd.NextDouble() - 0.5) * 0.2;
            var latStr = lat.ToString(CultureInfo.InvariantCulture);
            var lonStr = lon.ToString(CultureInfo.InvariantCulture);

            return new ESPDataResponseDTO
            {
                Serial = serial,
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsAvailable = true,
                Bpm = pulse,
                Spo2 = spo2,
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
                Neo6m = $"{latStr},{lonStr}",
                Temperature = Math.Round(temp, 1),
                Battery = Math.Round(battery, 1),
                ErrorMessage = null
            };
        }
    }
}
