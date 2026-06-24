namespace LifeAlertPlus.Shared.DTOs.Requests.Email
{
    // Server: primit de EmailController.SendReport (POST /api/email/send-report) → decodează PdfBase64
    // și apelează EmailService.SendReportEmailAsync cu PDF-ul ca atașament.
    // Client: trimis din SelectedMonitored.razor.cs (linia ~2456), după ce utilizatorul generează raportul
    // medical PDF (pdfExport.js) și apasă "Email to Doctor" în preview-ul de export.
    public class SendReportEmailRequestDTO
    {
        public string DoctorEmail { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public string PdfBase64 { get; set; } = string.Empty;
    }
}
