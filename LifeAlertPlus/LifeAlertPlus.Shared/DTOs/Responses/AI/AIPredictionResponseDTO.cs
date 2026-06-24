namespace LifeAlertPlus.Shared.DTOs.Responses.AI
{
    // Server: returnat de AIController.Predict — corespunde 1:1 cu PredictionResponse din ai-service/main.py
    // (microserviciul Python). HealthScore e scorul de pericol 0-100 (UI afișează 100-HealthScore).
    // Client: deserializat de AIPredictionService.GetPredictionAsync, afișat în SelectedMonitored.razor.cs
    // (cardul de predicție AI — stare, nivel de risc, explicație, probabilități per clasă).
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
