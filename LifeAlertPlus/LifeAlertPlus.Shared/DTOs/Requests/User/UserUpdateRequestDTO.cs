namespace LifeAlertPlus.Shared.DTOs.Requests.User
{
    public class UserUpdateRequestDTO
    {
        public string? FirstName { get; set; } = string.Empty;
        public string? LastName { get; set; } = string.Empty;
        public string? FirstDayOfTheWeek { get; set; } = string.Empty;
        public string? Language { get; set; } = string.Empty;
        public string? ThemeColor { get; set; } = string.Empty;
        public string? FontSize { get; set; } = string.Empty;
        public int? MinHeartRate { get; set; }
        public int? MaxHeartRate { get; set; }
        public float? MinTemperature { get; set; }
        public float? MaxTemperature { get; set; }
    }
}