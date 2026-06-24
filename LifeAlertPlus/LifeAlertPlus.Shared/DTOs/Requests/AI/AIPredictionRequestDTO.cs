namespace LifeAlertPlus.Shared.DTOs.Requests.AI
{
    // Cerere de predicție AI pe baza valorilor vitale curente ale unui pacient.
    // Server: primit de AIController.Predict (POST /api/ai/predict), care îl reîmpachetează
    // și îl trimite către microserviciul Python (ai-service/main.py, endpoint /predict).
    // Client: construit și trimis de SelectedMonitored.razor.cs (RunAIPredictionAsync) prin
    // AIPredictionService.GetPredictionAsync — pacientul/îngrijitorul poate cere o reevaluare
    // AI manuală a stării curente, separat de evaluarea automată făcută la fiecare măsurătoare.
    public class AIPredictionRequestDTO
    {
        public double Pulse { get; set; }
        public double Temperature { get; set; }
        public double Spo2 { get; set; } = 97.0;
        public double AccelX { get; set; }
        public double AccelY { get; set; }
        public double AccelZ { get; set; }
        public double GyroX { get; set; }
        public double GyroY { get; set; }
        public double GyroZ { get; set; }

        public Guid? MonitoredId { get; set; }
    }
}
