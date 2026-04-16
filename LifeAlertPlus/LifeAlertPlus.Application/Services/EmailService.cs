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
                        body {{
                            font-family: 'Arial', sans-serif;
                            background-color: #f9f9f9;
                            margin: 0;
                            padding: 0;
                        }}
                        .container {{
                            max-width: 600px;
                            margin: 40px auto;
                            background: white;
                            border-radius: 10px;
                            box-shadow: 0 4px 12px rgba(0,0,0,0.1);
                            overflow: hidden;
                        }}
                        .header {{
                            background: linear-gradient(135deg, #E8A5C8 0%, #D88BB7 100%);
                            padding: 30px;
                            text-align: center;
                        }}
                        .header h1 {{
                            color: #000;
                            margin: 0;
                            font-size: 32px;
                        }}
                        .content {{
                            padding: 40px 30px;
                        }}
                        .content p {{
                            color: #333;
                            line-height: 1.6;
                            font-size: 16px;
                        }}
                        .button {{
                            display: inline-block;
                            padding: 14px 30px;
                            margin: 20px 0;
                            background: linear-gradient(135deg, #E8A5C8 0%, #D88BB7 100%);
                            color: #000;
                            text-decoration: none;
                            border-radius: 30px;
                            font-weight: bold;
                            font-size: 16px;
                        }}
                        .footer {{
                            background: #f5f5f5;
                            padding: 20px;
                            text-align: center;
                            color: #666;
                            font-size: 14px;
                        }}
                        .warning {{
                            color: #666;
                            font-size: 14px;
                            margin-top: 20px;
                        }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h1>LifeAlert+</h1>
                            <p style='color: #000; margin-top: 10px;'>Because every heartbeat matters</p>
                        </div>
                        <div class='content'>
                            <h2>Welcome, {recipientName}!</h2>
                            <p>Thank you for registering with LifeAlert+. Your account has been successfully created.</p>
                            <p>To complete your registration, please verify your email address by clicking the button below:</p>
                            <div style='text-align: center;'>
                                <a href='{verificationUrl}' class='button'>Verify Email Address</a>
                            </div>
                            <p class='warning'>This verification link will expire in 24 hours.</p>
                            <p>If you did not create an account, please ignore this email.</p>
                        </div>
                        <div class='footer'>
                            <p>&copy; 2026 LifeAlert+. All rights reserved.</p>
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
                            background-color: #f9f9f9;
                            margin: 0;
                            padding: 0;
                        }}
                        .container {{
                            max-width: 600px;
                            margin: 40px auto;
                            background: white;
                            border-radius: 10px;
                            box-shadow: 0 4px 12px rgba(0,0,0,0.1);
                            overflow: hidden;
                        }}
                        .header {{
                            background: linear-gradient(135deg, #E8A5C8 0%, #D88BB7 100%);
                            padding: 30px;
                            text-align: center;
                        }}
                        .header h1 {{
                            color: #000;
                            margin: 0;
                            font-size: 32px;
                        }}
                        .content {{
                            padding: 40px 30px;
                        }}
                        .content p {{
                            color: #333;
                            line-height: 1.6;
                            font-size: 16px;
                        }}
                        .button {{
                            display: inline-block;
                            padding: 14px 30px;
                            margin: 20px 0;
                            background: linear-gradient(135deg, #E8A5C8 0%, #D88BB7 100%);
                            color: #000;
                            text-decoration: none;
                            border-radius: 30px;
                            font-weight: bold;
                            font-size: 16px;
                        }}
                        .footer {{
                            background: #f5f5f5;
                            padding: 20px;
                            text-align: center;
                            color: #666;
                            font-size: 14px;
                        }}
                        .warning {{
                            color: #666;
                            font-size: 14px;
                            margin-top: 20px;
                        }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h1>LifeAlert+</h1>
                            <p style='color: #000; margin-top: 10px;'>Because every heartbeat matters</p>
                        </div>
                        <div class='content'>
                            <h2>Hello, {recipientName}!</h2>
                            <p>We received a request to reset your password for your LifeAlert+ account.</p>
                            <p>Click the button below to reset your password:</p>
                            <div style='text-align: center;'>
                                <a href='{resetUrl}' class='button'>Reset Your Password</a>
                            </div>
                            <p class='warning'>This password reset link will expire in 24 hours.</p>
                            <p>If you did not request a password reset, please ignore this email or contact support if you have concerns.</p>
                        </div>
                        <div class='footer'>
                            <p>&copy; 2026 LifeAlert+. All rights reserved.</p>
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
                        body {{
                            font-family: 'Arial', sans-serif;
                            background-color: #f9f9f9;
                            margin: 0;
                            padding: 0;
                        }}
                        .container {{
                            max-width: 600px;
                            margin: 40px auto;
                            background: white;
                            border-radius: 10px;
                            box-shadow: 0 4px 12px rgba(0,0,0,0.1);
                            overflow: hidden;
                        }}
                        .header {{
                            background: linear-gradient(135deg, #E8A5C8 0%, #D88BB7 100%);
                            padding: 30px;
                            text-align: center;
                        }}
                        .header h1 {{
                            color: #000;
                            margin: 0;
                            font-size: 32px;
                        }}
                        .content {{
                            padding: 40px 30px;
                        }}
                        .content p {{
                            color: #333;
                            line-height: 1.6;
                            font-size: 16px;
                        }}
                        .button {{
                            display: inline-block;
                            padding: 14px 30px;
                            margin: 20px 0;
                            background: linear-gradient(135deg, #E8A5C8 0%, #D88BB7 100%);
                            color: #000;
                            text-decoration: none;
                            border-radius: 30px;
                            font-weight: bold;
                            font-size: 16px;
                        }}
                        .footer {{
                            background: #f5f5f5;
                            padding: 20px;
                            text-align: center;
                            color: #666;
                            font-size: 14px;
                        }}
                        .warning {{
                            color: #666;
                            font-size: 14px;
                            margin-top: 20px;
                        }}
                        .email-info {{
                            background: #f8f9fa;
                            padding: 15px;
                            border-radius: 8px;
                            margin: 20px 0;
                            border-left: 4px solid #E8A5C8;
                        }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h1>LifeAlert+</h1>
                            <p style='color: #000; margin-top: 10px;'>Because every heartbeat matters</p>
                        </div>
                        <div class='content'>
                            <h2>Email Address Change Request</h2>
                            <p>Hello {recipientName},</p>
                            <p>You have requested to change your email address on your LifeAlert+ account.</p>
                            
                            <div class='email-info'>
                                <p><strong>Previous email:</strong> {oldEmail}</p>
                                <p><strong>New email:</strong> {newEmail}</p>
                            </div>
                            
                            <p>To complete this change and verify your new email address, please click the button below:</p>
                            <div style='text-align: center;'>
                                <a href='{verificationUrl}' class='button'>Verify New Email Address</a>
                            </div>
                            <p class='warning'>This verification link will expire in 24 hours.</p>
                            <p><strong>Important:</strong> If you did not request this change, please contact our support team immediately.</p>
                        </div>
                        <div class='footer'>
                            <p>&copy; 2026 LifeAlert+. All rights reserved.</p>
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
                        body {{
                            font-family: 'Arial', sans-serif;
                            background-color: #f9f9f9;
                            margin: 0;
                            padding: 0;
                        }}
                        .container {{
                            max-width: 600px;
                            margin: 40px auto;
                            background: white;
                            border-radius: 10px;
                            box-shadow: 0 4px 12px rgba(0,0,0,0.1);
                            overflow: hidden;
                        }}
                        .header {{
                            background: linear-gradient(135deg, #ff6b6b 0%, #ee5a5a 100%);
                            padding: 30px;
                            text-align: center;
                        }}
                        .header h1 {{
                            color: #fff;
                            margin: 0;
                            font-size: 32px;
                        }}
                        .content {{
                            padding: 40px 30px;
                        }}
                        .content p {{
                            color: #333;
                            line-height: 1.6;
                            font-size: 16px;
                        }}
                        .button {{
                            display: inline-block;
                            padding: 14px 30px;
                            margin: 20px 0;
                            background: linear-gradient(135deg, #ff6b6b 0%, #ee5a5a 100%);
                            color: #fff;
                            text-decoration: none;
                            border-radius: 30px;
                            font-weight: bold;
                            font-size: 16px;
                        }}
                        .safe-button {{
                            background: linear-gradient(135deg, #E8A5C8 0%, #D88BB7 100%);
                            color: #000;
                        }}
                        .footer {{
                            background: #f5f5f5;
                            padding: 20px;
                            text-align: center;
                            color: #666;
                            font-size: 14px;
                        }}
                        .warning {{
                            color: #666;
                            font-size: 14px;
                            margin-top: 20px;
                        }}
                        .email-info {{
                            background: #fff3cd;
                            padding: 15px;
                            border-radius: 8px;
                            margin: 20px 0;
                            border-left: 4px solid #ff6b6b;
                        }}
                        .alert {{
                            background: #f8d7da;
                            color: #721c24;
                            padding: 15px;
                            border-radius: 8px;
                            margin: 20px 0;
                            border-left: 4px solid #f5c6cb;
                        }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h1>🔒 Security Alert</h1>
                            <p style='color: #fff; margin-top: 10px;'>LifeAlert+ Account Protection</p>
                        </div>
                        <div class='content'>
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
                            
                            <div style='text-align: center; margin: 30px 0;'>
                                <a href='{cancelUrl}' class='button'>🚫 This Wasn't Me - Cancel Request</a>
                            </div>
                            
                            <p class='warning'>This security link will expire in 24 hours. If you don't take action, the email change will proceed.</p>
                            
                            <p>If you're experiencing issues, please contact our support team immediately.</p>
                        </div>
                        <div class='footer'>
                            <p>&copy; 2026 LifeAlert+. All rights reserved.</p>
                        </div>
                    </div>
                </body>
                </html>
            ";
        }

        public async Task SendAlertNotificationEmailAsync(string recipientEmail, string recipientName, string patientName, string severity, string details)
        {
            using var client = CreateSmtpClient();
            var sender = GetSenderAddress();

            var mail = new MailMessage(sender, new MailAddress(recipientEmail, recipientName))
            {
                Subject = $"⚠ LifeAlert+ {severity}: {patientName}",
                IsBodyHtml = true,
                Body = GenerateAlertEmailBody(recipientName, patientName, severity, details)
            };

            await client.SendMailAsync(mail);
        }

        private string GenerateAlertEmailBody(string recipientName, string patientName, string severity, string details)
        {
            var severityColor = severity == "CRITICAL" ? "#e53935" : "#FF8F00";
            var severityBg = severity == "CRITICAL" ? "#FFEBEE" : "#FFF3E0";

            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <style>
                        body {{ font-family: 'Segoe UI', Tahoma, sans-serif; background: #f5f5f5; margin: 0; padding: 20px; }}
                        .container {{ max-width: 520px; margin: 0 auto; background: #fff; border-radius: 12px; overflow: hidden; box-shadow: 0 2px 12px rgba(0,0,0,0.08); }}
                        .header {{ background: linear-gradient(135deg, {severityColor}, {(severity == "CRITICAL" ? "#c62828" : "#E65100")}); padding: 28px 24px; text-align: center; }}
                        .header h1 {{ color: #fff; margin: 0; font-size: 22px; }}
                        .header .severity {{ display: inline-block; background: rgba(255,255,255,0.25); color: #fff; padding: 4px 16px; border-radius: 20px; font-weight: 700; font-size: 13px; margin-top: 8px; letter-spacing: 1px; }}
                        .body {{ padding: 28px 24px; }}
                        .alert-box {{ background: {severityBg}; border-left: 4px solid {severityColor}; padding: 14px 18px; border-radius: 6px; margin: 16px 0; font-size: 15px; color: #333; }}
                        .footer {{ background: #fafafa; padding: 16px 24px; text-align: center; font-size: 12px; color: #999; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h1>⚠ Health Alert</h1>
                            <div class='severity'>{severity}</div>
                        </div>
                        <div class='body'>
                            <p>Hello <strong>{recipientName}</strong>,</p>
                            <p>An alert has been triggered for <strong>{patientName}</strong> that has persisted for over 2 minutes:</p>
                            <div class='alert-box'>{details}</div>
                            <p>Please check on {patientName}'s condition as soon as possible.</p>
                            <p style='font-size:13px;color:#777;'>You are receiving this because you enabled email notifications in LifeAlert+. You can disable them in Settings.</p>
                        </div>
                        <div class='footer'>
                            <p>&copy; 2026 LifeAlert+. All rights reserved.</p>
                        </div>
                    </div>
                </body>
                </html>
            ";
        }
    }
}