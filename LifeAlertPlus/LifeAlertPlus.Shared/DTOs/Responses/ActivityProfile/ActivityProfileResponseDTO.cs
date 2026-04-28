namespace LifeAlertPlus.Shared.DTOs.Responses.ActivityProfile
{
    public class ActivityProfileResponseDTO
    {
        public Guid IdMonitored { get; set; }
        public List<HourlyProfileDTO> HourlyProfiles { get; set; } = new();
        public DateTime? LastUpdated { get; set; }
    }

    public class HourlyProfileDTO
    {
        public int HourOfDay { get; set; }
        public double AveragePulse { get; set; }
        public double MovementRate { get; set; }
        public double SleepProbability { get; set; }
        public int DataPoints { get; set; }
        // "Active", "Sleeping", "Resting", "Insufficient data"
        public string Label { get; set; } = string.Empty;
    }
}
