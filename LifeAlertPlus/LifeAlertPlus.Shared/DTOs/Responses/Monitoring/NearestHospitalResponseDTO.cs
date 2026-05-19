namespace LifeAlertPlus.Shared.DTOs.Responses.Monitoring
{
    public class NearestHospitalResponseDTO
    {
        public string HospitalName { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int EstimatedMinutes { get; set; }
        public double DistanceKm { get; set; }
    }
}
