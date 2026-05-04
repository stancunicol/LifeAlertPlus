namespace LifeAlertPlus.Shared.DTOs.Requests.User
{
    public class UserUpdateRequestDTO
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? FirstDayOfTheWeek { get; set; }
        public string? Language { get; set; }
        public string? ThemeColor { get; set; }
        public string? FontSize { get; set; }
        public int? MinHeartRate { get; set; }
        public int? MaxHeartRate { get; set; }
        public float? MinTemperature { get; set; }
        public float? MaxTemperature { get; set; }
        public int? MinSpO2 { get; set; }
        public int? MaxSpO2 { get; set; }
        public int? UpdateFrequency { get; set; }
        public int? DataRetentionDays { get; set; }
        public bool? NotifyByEmail { get; set; }
        public bool? NotifyByPush { get; set; }
        public bool? NotifyBySms { get; set; }
        public bool? EnableDailyReport { get; set; }
    }
}