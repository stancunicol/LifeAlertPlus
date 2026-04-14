namespace LifeAlertPlus.Shared.DTOs.Requests.AI
{
    public class AIPredictionRequestDTO
    {
        public double Pulse { get; set; }
        public double Temperature { get; set; }
        public double Spo2 { get; set; } = 97.0;
        public double AccelX { get; set; }
        public double AccelY { get; set; }
        public double AccelZ { get; set; }
        public double GyroX { get; set; }
        public double GyroY { get; set; }
        public double GyroZ { get; set; }
    }
}
