namespace LifeAlertPlus.Shared.DTOs.Responses.Monitoring
{
    // Server: returnat de MonitoringController.GetTrendPredictions (GET) → AlertMonitorService.GetTrendPredictions
    // analizează buffer-ul intern cu ultimele ~2 minute de măsurători și estimează direcția de evoluție
    // (crește/scade) a fiecărui semn vital, inclusiv în câte secunde ar atinge un prag de alertă
    // (SecondsToThreshold) — predicție pe termen scurt, diferită de clasificarea AI (NORMAL/ALERT/CRITICAL).
    // Client: consumat de SelectedMonitored.razor.cs (proprietatea TrendPredictions) pentru a afișa
    // tendința semnelor vitale ("HR crește cu X bpm/min, va atinge pragul în Y secunde").
    public class TrendPredictionResponseDTO
    {
        public List<TrendPredictionItemDTO> Predictions { get; set; } = new();
        public DateTime GeneratedAt { get; set; }
        public int BufferDataPoints { get; set; }
        public double BufferDurationSeconds { get; set; }
    }

    // O predicție de tendință pentru un singur semn vital (ex: "hr", direcție "increasing", severitate "warning")
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
