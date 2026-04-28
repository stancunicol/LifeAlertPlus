namespace LifeAlertPlus.Shared.DTOs.Responses.Monitoring
{
    public class TrendPredictionResponseDTO
    {
        public List<TrendPredictionItemDTO> Predictions { get; set; } = new();
        public DateTime GeneratedAt { get; set; }
        public int BufferDataPoints { get; set; }
        public double BufferDurationSeconds { get; set; }
    }

    public class TrendPredictionItemDTO
    {
        public string Metric { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public double CurrentValue { get; set; }
        public double AverageValue { get; set; }
        public double ChangeRatePerMinute { get; set; }
        public int? SecondsToThreshold { get; set; }
        public string? ThresholdDescription { get; set; }
    }
}
