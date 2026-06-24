namespace LifeAlertPlus.Shared.DTOs.Responses.Monitoring
{
    // NEFOLOSIT în prezent: NearestHospitalService (API/Services) calculează cel mai apropiat spital,
    // dar returnează propriul record intern `HospitalRouteResult` (folosit doar de AlertMonitorService,
    // probabil inclus direct în textul notificării/emailului de alertă), nu acest DTO din Shared.
    // Păstrat aici ca formă publică potențială a aceluiași rezultat, dacă va fi nevoie de un endpoint
    // API dedicat (ex: afișarea spitalului pe hartă în UI) — la acest moment nu există un astfel de endpoint.
    public class NearestHospitalResponseDTO
    {
        public string HospitalName { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int EstimatedMinutes { get; set; }
        public double DistanceKm { get; set; }
    }
}
