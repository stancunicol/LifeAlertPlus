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
    }
}