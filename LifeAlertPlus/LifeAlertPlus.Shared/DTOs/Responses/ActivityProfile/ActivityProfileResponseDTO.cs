namespace LifeAlertPlus.Shared.DTOs.Responses.ActivityProfile
{
    // Server: returnat de ActivityProfileController (GET) — mapează cele 24 înregistrări ActivityProfile
    // (una per oră, reconstruite zilnic de ActivityProfileRebuildBackgroundService din fereastra de 7 zile).
    // Client: consumat de SelectedMonitored.razor.cs pentru a desena profilul comportamental orar al pacientului.
    public class ActivityProfileResponseDTO
    {
        public Guid IdMonitored { get; set; }
        public List<HourlyProfileDTO> HourlyProfiles { get; set; } = new();
        public DateTime? LastUpdated { get; set; }
    }

    // O oră individuală din profil, cu eticheta calculată server-side (Active/Sleeping/Resting/Insufficient data)
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
