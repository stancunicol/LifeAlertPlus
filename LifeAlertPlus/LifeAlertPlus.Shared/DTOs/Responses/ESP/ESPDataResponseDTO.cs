namespace LifeAlertPlus.Shared.DTOs.Responses.ESP
{
    public class ESPDataResponseDTO
    {
        public string Serial { get; set; } = string.Empty;
        public long Date { get; set; }
        public bool IsAvailable { get; set; } = true;
        public string? ErrorMessage { get; set; }
        public List<int> Mpu6050 { get; set; } = new List<int>();
        public List<int> Gyro { get; set; } = new List<int>();
        public List<int>? Max30100 { get; set; }
        public int? Bpm { get; set; }
        public string? Neo6m { get; set; }
        public double? Temperature { get; set; }
        public double? Battery { get; set; }
    }
}