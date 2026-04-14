namespace LifeAlertPlus.Shared.DTOs.Responses.AI
{
    public class AIPredictionResponseDTO
    {
        public string Prediction { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public string RiskLevel { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public int HealthScore { get; set; }
        public Dictionary<string, double> AllProbabilities { get; set; } = new();
    }
}
