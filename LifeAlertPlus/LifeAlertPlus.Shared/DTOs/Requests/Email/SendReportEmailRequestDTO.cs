namespace LifeAlertPlus.Shared.DTOs.Requests.Email
{
    public class SendReportEmailRequestDTO
    {
        public string DoctorEmail { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public string PdfBase64 { get; set; } = string.Empty;
    }
}
