using System.Collections.Generic;

namespace LifeAlertPlus.Shared.DTOs.Responses.ESP
{
    public class ESPDataResponseDTO
    {
        public string Serial { get; set; }
        public long Date { get; set; }
        public List<int> Mpu6050 { get; set; }
        public List<int> Gyro { get; set; }
        public List<int>? Max30100 { get; set; }
        public double Hmc5883l { get; set; }
        public string? Neo6m { get; set; }
    }
}