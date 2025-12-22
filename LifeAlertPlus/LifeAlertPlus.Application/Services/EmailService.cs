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

        public async Task SendRegistrationSuccessEmailAsync(string recipientEmail, string recipientName, string verificationUrl)
        {
            var smtpHost = _configuration["Email:SmtpHost"] ?? "smtp.gmail.com";
            var smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
            var smtpUsername = _configuration["Email:Username"] ?? string.Empty;
            var smtpPassword = _configuration["Email:Password"] ?? string.Empty;
            var fromEmail = _configuration["Email:FromEmail"] ?? smtpUsername;
            var fromName = _configuration["Email:FromName"] ?? "LifeAlert+";

            using var smtpClient = new SmtpClient(smtpHost, smtpPort)
            {
                Credentials = new NetworkCredential(smtpUsername, smtpPassword),
                EnableSsl = true
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = "Welcome to LifeAlert+ - Please Verify Your Email",
                Body = GenerateRegistrationEmailBody(recipientName, verificationUrl),
                IsBodyHtml = true
            };

            mailMessage.To.Add(recipientEmail);

            await smtpClient.SendMailAsync(mailMessage);
        }

        public async Task SendPasswordResetEmailAsync(string recipientEmail, string recipientName, string resetUrl)
        {
            var smtpHost = _configuration["Email:SmtpHost"] ?? "smtp.gmail.com";
            var smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
            var smtpUsername = _configuration["Email:Username"] ?? string.Empty;
            var smtpPassword = _configuration["Email:Password"] ?? string.Empty;
            var fromEmail = _configuration["Email:FromEmail"] ?? smtpUsername;
            var fromName = _configuration["Email:FromName"] ?? "LifeAlert+";

            using var smtpClient = new SmtpClient(smtpHost, smtpPort)
            {
                Credentials = new NetworkCredential(smtpUsername, smtpPassword),
                EnableSsl = true
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = "LifeAlert+ - Password Reset Request",
                Body = GeneratePasswordResetEmailBody(recipientName, resetUrl),
                IsBodyHtml = true
            };

            mailMessage.To.Add(recipientEmail);

            await smtpClient.SendMailAsync(mailMessage);
        }

        public async Task SendEmailChangeVerificationAsync(string recipientEmail, string recipientName, string verificationUrl, string oldEmail)
        {
            var smtpHost = _configuration["Email:SmtpHost"] ?? "smtp.gmail.com";
            var smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
            var smtpUsername = _configuration["Email:Username"] ?? string.Empty;
            var smtpPassword = _configuration["Email:Password"] ?? string.Empty;
            var fromEmail = _configuration["Email:FromEmail"] ?? smtpUsername;
            var fromName = _configuration["Email:FromName"] ?? "LifeAlert+";

            using var smtpClient = new SmtpClient(smtpHost, smtpPort)
            {
                Credentials = new NetworkCredential(smtpUsername, smtpPassword),
                EnableSsl = true
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = "LifeAlert+ - Verify Your New Email Address",
                Body = GenerateEmailChangeVerificationBody(recipientName, verificationUrl, oldEmail, recipientEmail),
                IsBodyHtml = true
            };

            mailMessage.To.Add(recipientEmail);

            await smtpClient.SendMailAsync(mailMessage);
        }

        public async Task SendEmailChangeNotificationAsync(string oldEmail, string recipientName, string newEmail, string cancelUrl)
        {
            var smtpHost = _configuration["Email:SmtpHost"] ?? "smtp.gmail.com";
            var smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
            var smtpUsername = _configuration["Email:Username"] ?? string.Empty;
            var smtpPassword = _configuration["Email:Password"] ?? string.Empty;
            var fromEmail = _configuration["Email:FromEmail"] ?? smtpUsername;
            var fromName = _configuration["Email:FromName"] ?? "LifeAlert+";

            using var smtpClient = new SmtpClient(smtpHost, smtpPort)
            {
                Credentials = new NetworkCredential(smtpUsername, smtpPassword),
                EnableSsl = true
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = "LifeAlert+ - Email Change Security Notification",
                Body = GenerateEmailChangeNotificationBody(recipientName, oldEmail, newEmail, cancelUrl),
                IsBodyHtml = true
            };

            mailMessage.To.Add(oldEmail);

            await smtpClient.SendMailAsync(mailMessage);
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
                            <p>&copy; 2025 LifeAlert+. All rights reserved.</p>
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
                            <p>&copy; 2025 LifeAlert+. All rights reserved.</p>
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
                            <p>&copy; 2025 LifeAlert+. All rights reserved.</p>
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
                            <p>&copy; 2025 LifeAlert+. All rights reserved.</p>
                        </div>
                    </div>
                </body>
                </html>
            ";
        }
    }
}