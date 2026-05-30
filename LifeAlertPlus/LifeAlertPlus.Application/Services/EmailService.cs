        using LifeAlertPlus.Application.IServices;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace LifeAlertPlus.Application.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;

        public EmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private SmtpClient CreateSmtpClient()
        {
            var host = _configuration["Email:SmtpHost"] ?? "smtp.gmail.com";
            var port = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
            var username = _configuration["Email:Username"] ?? string.Empty;
            var password = _configuration["Email:Password"] ?? string.Empty;

            return new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(username, password),
                EnableSsl = true
            };
        }

        private MailAddress GetSenderAddress()
        {
            var username = _configuration["Email:Username"] ?? string.Empty;
            var fromEmail = _configuration["Email:FromEmail"] ?? username;
            var fromName = _configuration["Email:FromName"] ?? "LifeAlert+";
            return new MailAddress(fromEmail, fromName);
        }

        public async Task SendRegistrationSuccessEmailAsync(string recipientEmail, string recipientName, string verificationUrl)
        {
            using var smtpClient = CreateSmtpClient();

            var mailMessage = new MailMessage
            {
                From = GetSenderAddress(),
                Subject = "Welcome to LifeAlert+ - Please Verify Your Email",
                Body = GenerateRegistrationEmailBody(recipientName, verificationUrl),
                IsBodyHtml = true
            };

            mailMessage.To.Add(recipientEmail);

            await smtpClient.SendMailAsync(mailMessage);
        }

        public async Task SendPasswordResetEmailAsync(string recipientEmail, string recipientName, string resetUrl)
        {
            using var smtpClient = CreateSmtpClient();

            var mailMessage = new MailMessage
            {
                From = GetSenderAddress(),
                Subject = "LifeAlert+ - Password Reset Request",
                Body = GeneratePasswordResetEmailBody(recipientName, resetUrl),
                IsBodyHtml = true
            };

            mailMessage.To.Add(recipientEmail);

            await smtpClient.SendMailAsync(mailMessage);
        }

        public async Task SendEmailChangeVerificationAsync(string recipientEmail, string recipientName, string verificationUrl, string oldEmail)
        {
            using var smtpClient = CreateSmtpClient();

            var mailMessage = new MailMessage
            {
                From = GetSenderAddress(),
                Subject = "LifeAlert+ - Verify Your New Email Address",
                Body = GenerateEmailChangeVerificationBody(recipientName, verificationUrl, oldEmail, recipientEmail),
                IsBodyHtml = true
            };

            mailMessage.To.Add(recipientEmail);

            await smtpClient.SendMailAsync(mailMessage);
        }

        public async Task SendEmailChangeNotificationAsync(string oldEmail, string recipientName, string newEmail, string cancelUrl)
        {
            using var smtpClient = CreateSmtpClient();

            var mailMessage = new MailMessage
            {
                From = GetSenderAddress(),
                Subject = "LifeAlert+ - Email Change Security Notification",
                Body = GenerateEmailChangeNotificationBody(recipientName, oldEmail, newEmail, cancelUrl),
                IsBodyHtml = true
            };

            mailMessage.To.Add(oldEmail);

            await smtpClient.SendMailAsync(mailMessage);
        }

        public async Task SendReportEmailAsync(string doctorEmail, string patientName, byte[] pdfAttachment)
        {
            using var smtpClient = CreateSmtpClient();

            var mailMessage = new MailMessage
            {
                From = GetSenderAddress(),
                Subject = $"LifeAlert+ - Medical Report for {patientName}",
                Body = GenerateReportEmailBody(patientName),
                IsBodyHtml = true
            };

            mailMessage.To.Add(doctorEmail);

            var stream = new System.IO.MemoryStream(pdfAttachment);
            var attachment = new Attachment(stream, $"medical-report-{patientName.Replace(" ", "-")}.pdf", "application/pdf");
            mailMessage.Attachments.Add(attachment);

            await smtpClient.SendMailAsync(mailMessage);
        }

        private string GenerateReportEmailBody(string patientName)
        {
            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <style>
                        body {{ font-family: 'Arial', sans-serif; background-color: #f4f4f4; margin: 0; padding: 20px; }}
                        .container {{ max-width: 600px; margin: 0 auto; background: white; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 16px rgba(0,0,0,0.08); }}
                        .header {{ background: linear-gradient(135deg, #81C784, #66BB6A); padding: 30px; text-align: center; color: white; }}
                        .header h1 {{ margin: 0; font-size: 24px; }}
                        .body {{ padding: 30px; color: #444; line-height: 1.6; }}
                        .footer {{ padding: 20px 30px; background: #f9f9f9; text-align: center; font-size: 12px; color: #999; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h1>Life Alert +</h1>
                            <p>Medical Report</p>
                        </div>
                        <div class='body'>
                            <p>Dear Doctor,</p>
                            <p>Please find attached the medical report for patient <strong>{patientName}</strong>.</p>
                            <p>This report was generated by <strong>LifeAlert+</strong> and contains health monitoring data including vital signs, alerts, and clinical interpretation.</p>
                            <p>Best regards,<br/>LifeAlert+ Team</p>
                        </div>
                        <div class='footer'>
                            <p>This is an automated message from LifeAlert+. Please do not reply to this email.</p>
                        </div>
                    </div>
                </body>
                </html>";
        }

        private string GenerateRegistrationEmailBody(string recipientName, string verificationUrl)
        {
            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <style>
                        body {{ font-family: 'Arial', sans-serif; background-color: #f4f4f4; margin: 0; padding: 20px; }}
                        .container {{ max-width: 600px; margin: 0 auto; background: white; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 16px rgba(0,0,0,0.08); }}
                        .header {{ background: linear-gradient(135deg, #81C784, #66BB6A); padding: 30px; text-align: center; color: white; }}
                        .header h1 {{ margin: 0; font-size: 28px; }}
                        .header p {{ margin: 10px 0 0; color: rgba(255,255,255,0.92); font-size: 14px; }}
                        .body {{ padding: 30px; color: #444; line-height: 1.6; }}
                        .body h2 {{ margin: 0 0 16px; color: #2e7d32; font-size: 20px; }}
                        .body p {{ font-size: 16px; margin: 12px 0; }}
                        .button {{ display: inline-block; padding: 12px 28px; background: #66BB6A; color: white; border-radius: 6px; text-decoration: none; font-weight: bold; font-size: 16px; margin-top: 20px; }}
                        .warning {{ color: #777; font-size: 13px; margin-top: 18px; }}
                        .footer {{ padding: 20px 30px; background: #f9f9f9; text-align: center; font-size: 12px; color: #999; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h1>Life Alert +</h1>
                            <p>Because every heartbeat matters</p>
                        </div>
                        <div class='body'>
                            <h2>Welcome, {recipientName}!</h2>
                            <p>Thank you for registering with LifeAlert+. Your account has been successfully created.</p>
                            <p>To complete your registration, please verify your email address by clicking the button below:</p>
                            <p style='text-align:center;'>
                                <a href='{verificationUrl}' class='button'>Verify Email Address</a>
                            </p>
                            <p class='warning'>This verification link will expire in 24 hours.</p>
                            <p>If you did not create an account, please ignore this email.</p>
                        </div>
                        <div class='footer'>
                            <p>This is an automated message from LifeAlert+. Please do not reply to this email.</p>
                        </div>
                    </div>
                </body>
                </html>
            ";
        }

        private string GeneratePasswordResetEmailBody(string recipientName, string resetUrl)
        {
            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <style>
                        body {{
                            font-family: 'Arial', sans-serif;
                            background-color: #f4f4f4;
                            margin: 0;
                            padding: 20px;
                        }}
                        .container {{
                            max-width: 600px;
                            margin: 0 auto;
                            background: white;
                            border-radius: 12px;
                            overflow: hidden;
                            box-shadow: 0 4px 16px rgba(0,0,0,0.08);
                        }}
                        .header {{
                            background: linear-gradient(135deg, #81C784, #66BB6A);
                            padding: 30px;
                            text-align: center;
                            color: white;
                        }}
                        .header h1 {{
                            margin: 0;
                            font-size: 28px;
                        }}
                        .header p {{
                            margin: 10px 0 0;
                            color: rgba(255,255,255,0.92);
                            font-size: 14px;
                        }}
                        .body {{
                            padding: 30px;
                            color: #444;
                            line-height: 1.6;
                        }}
                        .body h2 {{
                            margin: 0 0 16px;
                            color: #2e7d32;
                            font-size: 20px;
                        }}
                        .body p {{
                            font-size: 16px;
                            margin: 12px 0;
                        }}
                        .button {{
                            display: inline-block;
                            padding: 12px 28px;
                            background: #66BB6A;
                            color: white;
                            border-radius: 6px;
                            text-decoration: none;
                            font-weight: bold;
                            font-size: 16px;
                            margin-top: 20px;
                        }}
                        .warning {{
                            color: #777;
                            font-size: 13px;
                            margin-top: 18px;
                        }}
                        .footer {{
                            padding: 20px 30px;
                            background: #f9f9f9;
                            text-align: center;
                            font-size: 12px;
                            color: #999;
                        }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h1>Life Alert +</h1>
                            <p>Because every heartbeat matters</p>
                        </div>
                        <div class='body'>
                            <h2>Hello, {recipientName}!</h2>
                            <p>We received a request to reset your password for your LifeAlert+ account.</p>
                            <p>Click the button below to choose a new password:</p>
                            <p style='text-align:center;'>
                                <a href='{resetUrl}' class='button'>Reset Your Password</a>
                            </p>
                            <p class='warning'>This password reset link will expire in 24 hours.</p>
                            <p>If you did not request a password reset, please ignore this email or contact support if you have concerns.</p>
                        </div>
                        <div class='footer'>
                            <p>This is an automated message from LifeAlert+. Please do not reply to this email.</p>
                        </div>
                    </div>
                </body>
                </html>
            ";
        }

        private string GenerateEmailChangeVerificationBody(string recipientName, string verificationUrl, string oldEmail, string newEmail)
        {
            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <style>
                        body {{ font-family: 'Arial', sans-serif; background-color: #f4f4f4; margin: 0; padding: 20px; }}
                        .container {{ max-width: 600px; margin: 0 auto; background: white; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 16px rgba(0,0,0,0.08); }}
                        .header {{ background: linear-gradient(135deg, #81C784, #66BB6A); padding: 30px; text-align: center; color: white; }}
                        .header h1 {{ margin: 0; font-size: 28px; }}
                        .header p {{ margin: 10px 0 0; color: rgba(255,255,255,0.92); font-size: 14px; }}
                        .body {{ padding: 30px; color: #444; line-height: 1.6; }}
                        .body h2 {{ margin: 0 0 16px; color: #2e7d32; font-size: 20px; }}
                        .body p {{ font-size: 16px; margin: 12px 0; }}
                        .button {{ display: inline-block; padding: 12px 28px; background: #66BB6A; color: white; border-radius: 6px; text-decoration: none; font-weight: bold; font-size: 16px; margin-top: 20px; }}
                        .warning {{ color: #777; font-size: 13px; margin-top: 18px; }}
                        .email-info {{ background: #f1f8e9; padding: 15px; border-radius: 8px; margin: 20px 0; border-left: 4px solid #66BB6A; }}
                        .email-info p {{ margin: 6px 0; font-size: 15px; }}
                        .footer {{ padding: 20px 30px; background: #f9f9f9; text-align: center; font-size: 12px; color: #999; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h1>Life Alert +</h1>
                            <p>Because every heartbeat matters</p>
                        </div>
                        <div class='body'>
                            <h2>Email Address Change Request</h2>
                            <p>Hello {recipientName},</p>
                            <p>You have requested to change your email address on your LifeAlert+ account.</p>
                            <div class='email-info'>
                                <p><strong>Previous email:</strong> {oldEmail}</p>
                                <p><strong>New email:</strong> {newEmail}</p>
                            </div>
                            <p>To complete this change and verify your new email address, please click the button below:</p>
                            <p style='text-align:center;'>
                                <a href='{verificationUrl}' class='button'>Verify New Email Address</a>
                            </p>
                            <p class='warning'>This verification link will expire in 24 hours.</p>
                            <p><strong>Important:</strong> If you did not request this change, please contact our support team immediately.</p>
                        </div>
                        <div class='footer'>
                            <p>This is an automated message from LifeAlert+. Please do not reply to this email.</p>
                        </div>
                    </div>
                </body>
                </html>
            ";
        }

        private string GenerateEmailChangeNotificationBody(string recipientName, string oldEmail, string newEmail, string cancelUrl)
        {
            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <style>
                        body {{ font-family: 'Arial', sans-serif; background-color: #f4f4f4; margin: 0; padding: 20px; }}
                        .container {{ max-width: 600px; margin: 0 auto; background: white; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 16px rgba(0,0,0,0.08); }}
                        .header {{ background: linear-gradient(135deg, #81C784, #66BB6A); padding: 30px; text-align: center; color: white; }}
                        .header h1 {{ margin: 0; font-size: 28px; }}
                        .header p {{ margin: 10px 0 0; color: rgba(255,255,255,0.92); font-size: 14px; }}
                        .body {{ padding: 30px; color: #444; line-height: 1.6; }}
                        .body h2 {{ margin: 0 0 16px; color: #2e7d32; font-size: 20px; }}
                        .body p {{ font-size: 16px; margin: 12px 0; }}
                        /* Cancel-request button is the destructive action: keep it warning-red so it is unambiguous. */
                        .button {{ display: inline-block; padding: 12px 28px; background: #d32f2f; color: #fff; border-radius: 6px; text-decoration: none; font-weight: bold; font-size: 16px; margin-top: 20px; }}
                        .warning {{ color: #777; font-size: 13px; margin-top: 18px; }}
                        .email-info {{ background: #f1f8e9; padding: 15px; border-radius: 8px; margin: 20px 0; border-left: 4px solid #66BB6A; }}
                        .email-info p {{ margin: 6px 0; font-size: 15px; }}
                        .alert {{ background: #fff3cd; color: #6e4a00; padding: 15px; border-radius: 8px; margin: 20px 0; border-left: 4px solid #ffb300; }}
                        .footer {{ padding: 20px 30px; background: #f9f9f9; text-align: center; font-size: 12px; color: #999; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h1>Life Alert +</h1>
                            <p>Security notification</p>
                        </div>
                        <div class='body'>
                            <h2>Email Change Request Detected</h2>
                            <p>Hello {recipientName},</p>
                            <div class='alert'>
                                <strong>⚠️ Important Security Notice:</strong> Someone has requested to change the email address associated with your LifeAlert+ account.
                            </div>
                            <div class='email-info'>
                                <p><strong>Current email:</strong> {oldEmail}</p>
                                <p><strong>Requested new email:</strong> {newEmail}</p>
                                <p><strong>Request time:</strong> {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</p>
                            </div>
                            <p><strong>If this was you:</strong> No action is needed. You will receive a verification email at your new address to complete the change.</p>
                            <p><strong>If this was NOT you:</strong> Your account security may be compromised. Click the button below immediately to cancel this request and secure your account:</p>
                            <p style='text-align:center;'>
                                <a href='{cancelUrl}' class='button'>🚫 This Wasn't Me — Cancel Request</a>
                            </p>
                            <p class='warning'>This security link will expire in 24 hours. If you don't take action, the email change will proceed.</p>
                            <p>If you're experiencing issues, please contact our support team immediately.</p>
                        </div>
                        <div class='footer'>
                            <p>This is an automated message from LifeAlert+. Please do not reply to this email.</p>
                        </div>
                    </div>
                </body>
                </html>
            ";
        }

        public async Task SendAlertNotificationEmailAsync(string recipientEmail, string recipientName, string patientName, string severity, string details, string lang = "ro")
        {
            using var client = CreateSmtpClient();
            var sender = GetSenderAddress();

            bool isEn = string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase);
            string severityLabel = severity == "CRITICAL"
                ? (isEn ? "CRITICAL" : "CRITIC")
                : (isEn ? "ALERT" : "ALERTĂ");

            var mail = new MailMessage(sender, new MailAddress(recipientEmail, recipientName))
            {
                Subject = $"⚠ LifeAlert+ {severityLabel}: {patientName}",
                IsBodyHtml = true,
                Body = GenerateAlertEmailBody(recipientName, patientName, severity, details, isEn)
            };

            await client.SendMailAsync(mail);
        }

        public async Task SendDailyReportEmailAsync(string recipientEmail, string recipientName, string reportHtmlBody, DateTime reportDate, string lang = "ro")
        {
            using var client = CreateSmtpClient();
            var sender = GetSenderAddress();

            bool isEn = string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase);
            var culture = isEn ? new System.Globalization.CultureInfo("en-US") : new System.Globalization.CultureInfo("ro-RO");
            string dateText = reportDate.ToString("dd MMMM yyyy", culture);
            string subject = isEn
                ? $"📊 LifeAlert+ Daily Report — {dateText}"
                : $"📊 LifeAlert+ Raport zilnic — {dateText}";

            var mail = new MailMessage(sender, new MailAddress(recipientEmail, recipientName))
            {
                Subject = subject,
                IsBodyHtml = true,
                Body = reportHtmlBody
            };

            await client.SendMailAsync(mail);
        }

        private string GenerateAlertEmailBody(string recipientName, string patientName, string severity, string details, bool isEn)
        {
            var severityColor = severity == "CRITICAL" ? "#e53935" : "#FF8F00";
            var severityBg = severity == "CRITICAL" ? "#FFEBEE" : "#FFF3E0";
            var headerDark = severity == "CRITICAL" ? "#c62828" : "#E65100";

            string severityLabel = severity == "CRITICAL"
                ? (isEn ? "CRITICAL" : "CRITIC")
                : (isEn ? "ALERT" : "ALERTĂ");

            string headerTitle  = isEn ? "⚠ Health Alert"    : "⚠ Alertă Medicală";
            string greeting     = isEn ? $"Hello <strong>{recipientName}</strong>,"
                                       : $"Bună ziua, <strong>{recipientName}</strong>,";
            string bodyText     = isEn
                ? $"An alert has been triggered for <strong>{patientName}</strong> that has persisted for over 2 minutes:"
                : $"O alertă a fost declanșată pentru <strong>{patientName}</strong> și persistă de peste 2 minute:";
            string disclaimer   = isEn
                ? "You are receiving this because you enabled email notifications in LifeAlert+. You can disable them in Settings."
                : "Primiți acest email deoarece ați activat notificările prin email în LifeAlert+. Le puteți dezactiva din Setări.";
            string footerText   = isEn ? "&copy; 2026 LifeAlert+. All rights reserved."
                                       : "&copy; 2026 LifeAlert+. Toate drepturile rezervate.";

            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <style>
                        body {{ font-family: 'Segoe UI', Tahoma, sans-serif; background: #f5f5f5; margin: 0; padding: 20px; }}
                        .container {{ max-width: 520px; margin: 0 auto; background: #fff; border-radius: 12px; overflow: hidden; box-shadow: 0 2px 12px rgba(0,0,0,0.08); }}
                        .header {{ background: linear-gradient(135deg, {severityColor}, {headerDark}); padding: 28px 24px; text-align: center; }}
                        .header h1 {{ color: #fff; margin: 0; font-size: 22px; }}
                        .header .severity {{ display: inline-block; background: rgba(255,255,255,0.25); color: #fff; padding: 4px 16px; border-radius: 20px; font-weight: 700; font-size: 13px; margin-top: 8px; letter-spacing: 1px; }}
                        .body {{ padding: 28px 24px; }}
                        .alert-box {{ background: {severityBg}; border-left: 4px solid {severityColor}; padding: 14px 18px; border-radius: 6px; margin: 16px 0; font-size: 15px; color: #333; white-space: pre-line; }}
                        .footer {{ background: #fafafa; padding: 16px 24px; text-align: center; font-size: 12px; color: #999; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h1>{headerTitle}</h1>
                            <div class='severity'>{severityLabel}</div>
                        </div>
                        <div class='body'>
                            <p>{greeting}</p>
                            <p>{bodyText}</p>
                            <div class='alert-box'>{details}</div>
                            <p style='font-size:13px;color:#777;'>{disclaimer}</p>
                        </div>
                        <div class='footer'>
                            <p>{footerText}</p>
                        </div>
                    </div>
                </body>
                </html>
            ";
        }

        public async Task SendDoctorInvitationEmailAsync(string doctorEmail, string patientName, string invitationLink)
        {
            using var smtpClient = CreateSmtpClient();
            var mailMessage = new MailMessage
            {
                From = GetSenderAddress(),
                Subject = $"LifeAlert+ - Invitație pentru acces la datele pacientului {patientName}",
                Body = GenerateDoctorInvitationEmailBody(patientName, invitationLink),
                IsBodyHtml = true
            };
            mailMessage.To.Add(doctorEmail);
            await smtpClient.SendMailAsync(mailMessage);
        }

        private string GenerateDoctorInvitationEmailBody(string patientName, string invitationLink)
        {
            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <style>
                        body {{ font-family: 'Arial', sans-serif; background-color: #f4f4f4; margin: 0; padding: 20px; }}
                        .container {{ max-width: 600px; margin: 0 auto; background: white; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 16px rgba(0,0,0,0.08); }}
                        .header {{ background: linear-gradient(135deg, #81C784, #66BB6A); padding: 30px; text-align: center; color: white; }}
                        .header h1 {{ margin: 0; font-size: 24px; }}
                        .body {{ padding: 30px; color: #444; line-height: 1.6; }}
                        .footer {{ padding: 20px 30px; background: #f9f9f9; text-align: center; font-size: 12px; color: #999; }}
                        .button {{ display: inline-block; padding: 12px 24px; background: #66BB6A; color: white; border-radius: 6px; text-decoration: none; font-weight: bold; margin-top: 20px; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h1>Life Alert +</h1>
                            <p>Invitație pentru acces la datele pacientului</p>
                        </div>
                        <div class='body'>
                            <p>Stimate doctor,</p>
                            <p>Ați fost invitat să vizualizați datele medicale ale pacientului <strong>{patientName}</strong> prin intermediul platformei <strong>LifeAlert+</strong>.</p>
                            <p>Pentru a vizualiza datele pacientului, vă rugăm să apăsați pe butonul de mai jos:</p>
                            <p><a href='{invitationLink}' class='button'>Vezi datele pacientului</a></p>
                            <p style='color:#777;font-size:13px;margin-top:10px;'>Link-ul este valabil 24 de ore.</p>
                            <p>Dacă nu ați solicitat această invitație, puteți ignora acest email.</p>
                            <p>Cu stimă,<br/>Echipa LifeAlert+</p>
                        </div>
                        <div class='footer'>
                            <p>Acesta este un mesaj automat. Vă rugăm să nu răspundeți la acest email.</p>
                        </div>
                    </div>
                </body>
                </html>";
        }

        public async Task SendDoctorNoteNotificationEmailAsync(string recipientEmail, string recipientName, string patientName, string doctorEmail, string notePreview, string lang = "ro")
        {
            using var smtpClient = CreateSmtpClient();
            var isEn = lang == "en";
            var subject = isEn
                ? $"LifeAlert+ - New medical note for {patientName}"
                : $"LifeAlert+ - Notiță medicală nouă pentru {patientName}";

            var mailMessage = new MailMessage
            {
                From = GetSenderAddress(),
                Subject = subject,
                Body = GenerateDoctorNoteNotificationEmailBody(recipientName, patientName, doctorEmail, notePreview, isEn),
                IsBodyHtml = true
            };
            mailMessage.To.Add(recipientEmail);
            await smtpClient.SendMailAsync(mailMessage);
        }

        private string GenerateDoctorNoteNotificationEmailBody(string recipientName, string patientName, string doctorEmail, string notePreview, bool isEn)
        {
            var headerTitle = isEn ? "📝 Medical Note" : "📝 Notiță medicală";
            var greeting = isEn ? $"Hello <strong>{recipientName}</strong>," : $"Bună ziua, <strong>{recipientName}</strong>,";
            var bodyText = isEn
                ? $"Doctor <strong>{doctorEmail}</strong> has added a medical note for <strong>{patientName}</strong>:"
                : $"Doctorul <strong>{doctorEmail}</strong> a adăugat o notiță medicală pentru <strong>{patientName}</strong>:";
            var ctaText = isEn ? "View Patient Data" : "Vezi datele pacientului";
            var disclaimer = isEn
                ? "You are receiving this because you enabled email notifications in LifeAlert+. You can disable them in Settings."
                : "Primiți acest email deoarece ați activat notificările prin email în LifeAlert+. Le puteți dezactiva din Setări.";
            var footerText = isEn ? "&copy; 2026 LifeAlert+. All rights reserved." : "&copy; 2026 LifeAlert+. Toate drepturile rezervate.";

            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <style>
                        body {{ font-family: 'Segoe UI', Tahoma, sans-serif; background: #f5f5f5; margin: 0; padding: 20px; }}
                        .container {{ max-width: 520px; margin: 0 auto; background: #fff; border-radius: 12px; overflow: hidden; box-shadow: 0 2px 12px rgba(0,0,0,0.08); }}
                        .header {{ background: linear-gradient(135deg, #2196F3, #1976D2); padding: 28px 24px; text-align: center; }}
                        .header h1 {{ color: #fff; margin: 0; font-size: 22px; }}
                        .body {{ padding: 28px 24px; }}
                        .note-box {{ background: #f0f7ff; border-left: 4px solid #2196F3; padding: 14px 18px; border-radius: 6px; margin: 16px 0; font-size: 14px; color: #333; line-height: 1.5; white-space: pre-wrap; word-wrap: break-word; }}
                        .footer {{ background: #fafafa; padding: 16px 24px; text-align: center; font-size: 12px; color: #999; }}
                        .button {{ display: inline-block; padding: 10px 20px; background: #2196F3; color: white; border-radius: 4px; text-decoration: none; font-weight: bold; margin-top: 14px; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h1>{headerTitle}</h1>
                        </div>
                        <div class='body'>
                            <p>{greeting}</p>
                            <p>{bodyText}</p>
                            <div class='note-box'>{notePreview}</div>
                            <p style='font-size:13px;color:#777;'>{disclaimer}</p>
                        </div>
                        <div class='footer'>
                            <p>{footerText}</p>
                        </div>
                    </div>
                </body>
                </html>";
        }
    }
}