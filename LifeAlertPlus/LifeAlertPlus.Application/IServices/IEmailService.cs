using System.Threading.Tasks;

namespace LifeAlertPlus.Application.IServices
{
    // Interfață pentru serviciul de email SMTP (Gmail) — toate tipurile de emailuri trimise de aplicație
    public interface IEmailService
    {
        Task SendRegistrationSuccessEmailAsync(string recipientEmail, string recipientName, string verificationUrl);                                                      // Email de bun venit cu link de confirmare adresă (trimis la înregistrare)
        Task SendPasswordResetEmailAsync(string recipientEmail, string recipientName, string resetUrl);                                                                  // Email cu link de resetare parolă (valabil limitat în timp)
        Task SendEmailChangeVerificationAsync(string recipientEmail, string recipientName, string verificationUrl, string oldEmail);                                     // Email de confirmare trimis la NOUA adresă (fluxul de schimbare email)
        Task SendEmailChangeNotificationAsync(string oldEmail, string recipientName, string newEmail, string cancelUrl);                                                 // Notificare trimisă la VECHEA adresă cu link de anulare (securitate)
        Task SendReportEmailAsync(string doctorEmail, string patientName, byte[] pdfAttachment);                                                                        // Trimite un raport PDF ca atașament medicului (exportat din UI)
        Task SendAlertNotificationEmailAsync(string recipientEmail, string recipientName, string patientName, string severity, string details, string lang = "ro");     // Email de alertă medicală (Critical/Alert) trimis îngrijitorului în timp real
        Task SendDailyReportEmailAsync(string recipientEmail, string recipientName, string reportHtmlBody, DateTime reportDate, string lang = "ro");                    // Raport zilnic HTML cu statistici de sănătate (trimis la miezul nopții locale)
        Task SendDoctorInvitationEmailAsync(string doctorEmail, string patientName, string invitationLink);                                                             // Invitație trimisă medicului cu link de acces la datele pacientului (token 24h)
        Task SendDoctorNoteNotificationEmailAsync(string recipientEmail, string recipientName, string patientName, string doctorEmail, string notePreview, string lang = "ro"); // Notificare pentru îngrijitor când medicul adaugă o notă (preview 200 caractere)
    }
}