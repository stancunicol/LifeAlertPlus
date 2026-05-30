using LifeAlertPlus.Application.IServices;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace LifeAlertPlus.API.Services
{
    public class DailyReportService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IEmailService _emailService;
        private readonly ILogger<DailyReportService> _logger;

        public DailyReportService(
            IServiceScopeFactory scopeFactory,
            IEmailService emailService,
            ILogger<DailyReportService> logger)
        {
            _scopeFactory = scopeFactory;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task SendDailyReportsAsync()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();

                var usersWithReports = await db.Users
                    .Where(u => u.EnableDailyReport && !u.DeletedAt.HasValue && u.IsEmailConfirmed)
                    .ToListAsync();

                _logger.LogInformation("Sending daily reports to {Count} users", usersWithReports.Count);

                foreach (var user in usersWithReports)
                {
                    try
                    {
                        await SendReportForUserAsync(db, user);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send daily report for user {UserId}", user.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Daily report job failed");
            }
        }

        private async Task SendReportForUserAsync(LifeAlertPlusDbContext db, Domain.Entities.User user)
        {
            var yesterday = DateTime.UtcNow.Date.AddDays(-1);
            var monitoredIds = await db.UserMonitoreds
                .Where(um => um.IdUser == user.Id)
                .Select(um => um.IdMonitored)
                .ToListAsync();

            if (monitoredIds.Count == 0)
                return;

            // Get monitored people info — archived persons are excluded from daily reports
            // (no active monitoring => no report content).
            var monitored = await db.Monitoreds
                .Where(m => monitoredIds.Contains(m.Id) && !m.IsArchived)
                .ToListAsync();

            if (monitored.Count == 0)
                return;

            monitoredIds = monitored.Select(m => m.Id).ToList();

            var yesterdayEnd = yesterday.AddDays(1);

            // Get yesterday's measurements and notifications
            var measurements = await db.Measurements
                .Where(m => monitoredIds.Contains(m.IdMonitored) &&
                            m.CreatedAt >= yesterday && m.CreatedAt < yesterdayEnd)
                .ToListAsync();

            var alerts = await db.Notifications
                .Where(n => monitoredIds.Contains(n.IdMonitored) &&
                            n.CreatedAt >= yesterday && n.CreatedAt < yesterdayEnd &&
                            n.DeletedAt == null)
                .ToListAsync();

            // Build HTML report
            var html = GenerateReportHtml(user, yesterday, monitored, measurements, alerts);

            bool isEn = string.Equals(user.Language, "en", StringComparison.OrdinalIgnoreCase);
            string lang = isEn ? "en" : "ro";

            // Send via the dedicated daily-report email channel (own subject + template).
            await _emailService.SendDailyReportEmailAsync(
                user.Email,
                user.FirstName,
                html,
                yesterday,
                lang);

            _logger.LogInformation("Sent daily report to {Email} for {Date}", user.Email, yesterday.ToString("yyyy-MM-dd"));
        }

        private string GenerateReportHtml(
            Domain.Entities.User user,
            DateTime date,
            List<Domain.Entities.Monitored> monitoredPeople,
            List<Domain.Entities.Measurement> measurements,
            List<Domain.Entities.Notification> alerts)
        {
            bool isEn = string.Equals(user.Language, "en", StringComparison.OrdinalIgnoreCase);
            var culture = isEn ? new System.Globalization.CultureInfo("en-US") : new System.Globalization.CultureInfo("ro-RO");

            string title          = isEn ? "Daily Health Report"      : "Raport zilnic de sănătate";
            string sectionSummary = isEn ? "📈 Summary"               : "📈 Rezumat";
            string lblMonitored   = isEn ? "Monitored People:"        : "Persoane monitorizate:";
            string lblMeasure     = isEn ? "Measurements Recorded:"   : "Măsurători înregistrate:";
            string lblAlerts      = isEn ? "Alerts Triggered:"        : "Alerte declanșate:";
            string sectionAlerts  = isEn ? "🚨 Alerts"                : "🚨 Alerte";
            string sectionPeople  = isEn ? "👥 Monitored People"      : "👥 Persoane monitorizate";
            string lblAvgPulse    = isEn ? "Avg Pulse:"               : "Puls mediu:";
            string lblAvgTemp     = isEn ? "Avg Temperature:"         : "Temperatură medie:";
            string lblAvgSpO2     = isEn ? "Avg SpO₂:"               : "SpO₂ mediu:";
            string lblMeasCount   = isEn ? "measurements recorded"    : "măsurători înregistrate";
            string footer         = isEn
                ? "This is an automated daily report. You can disable these emails in your account settings."
                : "Acesta este un raport zilnic automat. Puteți dezactiva aceste emailuri din setările contului.";

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html>");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset='utf-8'>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; color: #333; line-height: 1.6; }");
            sb.AppendLine(".header { background: linear-gradient(135deg, #81C784, #66BB6A); color: white; padding: 30px; border-radius: 8px 8px 0 0; }");
            sb.AppendLine(".header h1 { margin: 0; font-size: 24px; }");
            sb.AppendLine(".header p { margin: 5px 0 0; font-size: 14px; opacity: 0.92; }");
            sb.AppendLine(".container { max-width: 600px; margin: 0 auto; border: 1px solid #e0e0e0; border-radius: 8px; overflow: hidden; }");
            sb.AppendLine(".section { padding: 20px; border-bottom: 1px solid #f0f0f0; }");
            sb.AppendLine(".section-title { font-size: 18px; font-weight: bold; color: #2e7d32; margin-bottom: 15px; }");
            sb.AppendLine(".stat-row { display: flex; justify-content: space-between; padding: 8px 0; border-bottom: 1px solid #f5f5f5; }");
            sb.AppendLine(".stat-label { font-weight: 600; color: #555; }");
            sb.AppendLine(".stat-value { color: #333; }");
            sb.AppendLine(".alert-item { background: #fff5f5; border-left: 4px solid #e53935; padding: 12px; margin-bottom: 10px; border-radius: 4px; }");
            sb.AppendLine(".alert-type { font-size: 12px; font-weight: bold; color: #c62828; text-transform: uppercase; margin-bottom: 4px; }");
            sb.AppendLine(".alert-msg { font-size: 14px; color: #555; }");
            sb.AppendLine(".footer { background: #f9f9f9; padding: 15px 20px; text-align: center; font-size: 12px; color: #999; border-top: 1px solid #e0e0e0; }");
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");

            sb.AppendLine("<div class='container'>");
            sb.AppendLine("<div class='header'>");
            sb.AppendLine($"<h1>📊 {title}</h1>");
            sb.AppendLine($"<p>{date.ToString("dd MMMM yyyy", culture)}</p>");
            sb.AppendLine("</div>");

            // Summary section
            sb.AppendLine("<div class='section'>");
            sb.AppendLine($"<div class='section-title'>{sectionSummary}</div>");
            sb.AppendLine("<div class='stat-row'>");
            sb.AppendLine($"<span class='stat-label'>{lblMonitored}</span>");
            sb.AppendLine($"<span class='stat-value'>{monitoredPeople.Count}</span>");
            sb.AppendLine("</div>");
            sb.AppendLine("<div class='stat-row'>");
            sb.AppendLine($"<span class='stat-label'>{lblMeasure}</span>");
            sb.AppendLine($"<span class='stat-value'>{measurements.Count}</span>");
            sb.AppendLine("</div>");
            sb.AppendLine("<div class='stat-row'>");
            sb.AppendLine($"<span class='stat-label'>{lblAlerts}</span>");
            sb.AppendLine($"<span class='stat-value' style='color: {(alerts.Count > 0 ? "#e53935" : "#4caf50")}'>{alerts.Count}</span>");
            sb.AppendLine("</div>");
            sb.AppendLine("</div>");

            // Alerts section
            if (alerts.Any())
            {
                sb.AppendLine("<div class='section'>");
                sb.AppendLine($"<div class='section-title'>{sectionAlerts}</div>");
                foreach (var alert in alerts.OrderByDescending(a => a.CreatedAt))
                {
                    var monitored = monitoredPeople.FirstOrDefault(m => m.Id == alert.IdMonitored);
                    sb.AppendLine("<div class='alert-item'>");
                    sb.AppendLine($"<div class='alert-type'>{alert.NotificationType}</div>");
                    sb.AppendLine($"<div class='alert-msg'><strong>{monitored?.FirstName} {monitored?.LastName}</strong><br/>{alert.Message}</div>");
                    sb.AppendLine($"<div style='font-size: 12px; color: #999; margin-top: 6px;'>{alert.CreatedAt.ToLocalTime():HH:mm:ss}</div>");
                    sb.AppendLine("</div>");
                }
                sb.AppendLine("</div>");
            }

            // People section
            sb.AppendLine("<div class='section'>");
            sb.AppendLine($"<div class='section-title'>{sectionPeople}</div>");
            foreach (var person in monitoredPeople)
            {
                var personMeasurements = measurements.Where(m => m.IdMonitored == person.Id).ToList();
                if (personMeasurements.Any())
                {
                    var avgPulse = personMeasurements.Where(m => m.Pulse > 0).Average(m => m.Pulse);
                    var avgTemp = personMeasurements.Where(m => m.Temperature > 0).Average(m => m.Temperature);
                    var avgSpO2 = personMeasurements.Where(m => m.SpO2 > 0).Average(m => m.SpO2);

                    sb.AppendLine("<div style='margin-bottom: 15px; padding-bottom: 15px; border-bottom: 1px solid #f5f5f5;'>");
                    sb.AppendLine($"<strong>{person.FirstName} {person.LastName}</strong><br/>");
                    sb.AppendLine("<div class='stat-row'>");
                    sb.AppendLine($"<span class='stat-label'>{lblAvgPulse}</span>");
                    sb.AppendLine($"<span class='stat-value'>{avgPulse:F0} bpm</span>");
                    sb.AppendLine("</div>");
                    sb.AppendLine("<div class='stat-row'>");
                    sb.AppendLine($"<span class='stat-label'>{lblAvgTemp}</span>");
                    sb.AppendLine($"<span class='stat-value'>{avgTemp:F1}°C</span>");
                    sb.AppendLine("</div>");
                    if (avgSpO2 > 0)
                    {
                        sb.AppendLine("<div class='stat-row'>");
                        sb.AppendLine($"<span class='stat-label'>{lblAvgSpO2}</span>");
                        sb.AppendLine($"<span class='stat-value'>{avgSpO2:F0}%</span>");
                        sb.AppendLine("</div>");
                    }
                    sb.AppendLine("<div style='font-size: 12px; color: #999; margin-top: 6px;'>");
                    sb.AppendLine($"{personMeasurements.Count} {lblMeasCount}");
                    sb.AppendLine("</div>");
                    sb.AppendLine("</div>");
                }
            }
            sb.AppendLine("</div>");

            sb.AppendLine("<div class='footer'>");
            sb.AppendLine(footer);
            sb.AppendLine("</div>");

            sb.AppendLine("</div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }
    }
}
