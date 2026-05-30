namespace LifeAlertPlus.Domain.Entities
{
    public class SystemError
    {
        public Guid Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = "Error";   // Error | Warning | Info
        public string Source { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }
}
