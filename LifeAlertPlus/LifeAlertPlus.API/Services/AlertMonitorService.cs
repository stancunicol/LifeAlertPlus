using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LifeAlertPlus.API.Services
{
    public class AlertMonitorService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AlertMonitorService> _logger;
        private readonly bool _sendCriticalSmsImmediately;
    private readonly bool _sendAlertSmsImmediately;
        // Track alert state per monitored person: monitoredId -> AlertState
        private readonly ConcurrentDictionary<Guid, AlertState> _alertStates = new();

        // Cooldown: don't send another notification for the same monitored person within 10 minutes
        private readonly ConcurrentDictionary<Guid, DateTime> _lastNotificationSent = new();

        private static readonly TimeSpan PersistenceThreshold = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan CriticalSmsThreshold = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan NotificationCooldown = TimeSpan.FromMinutes(10);

        private readonly IPushNotificationService _pushNotificationService;
        public AlertMonitorService(IServiceScopeFactory scopeFactory, ILogger<AlertMonitorService> logger, IConfiguration configuration, IPushNotificationService pushNotificationService)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _pushNotificationService = pushNotificationService;
            _sendCriticalSmsImmediately = bool.TryParse(configuration["AlertMonitor:SendCriticalSmsImmediately"], out var immediate) && immediate;
            _sendAlertSmsImmediately = bool.TryParse(configuration["AlertMonitor:SendAlertSmsImmediately"], out var immediateAlert) && immediateAlert;

            if (_sendCriticalSmsImmediately)
            {
                _logger.LogWarning("AlertMonitor is configured to send critical SMS immediately for testing.");
            }
            if (_sendAlertSmsImmediately)
            {
                _logger.LogWarning("AlertMonitor is configured to send alert SMS immediately for testing.");
            }
        }

        public async Task ProcessMeasurementAsync(Guid monitoredId, double pulse, double temperature, double spo2, bool isFall)
        {
            var now = DateTime.UtcNow;
            var severity = EvaluateSeverity(pulse, temperature, spo2, isFall);

            _logger.LogInformation("Processing measurement for {MonitoredId}: pulse={Pulse}, temp={Temperature}, spo2={SpO2}, isFall={IsFall}, severity={Severity}.", monitoredId, pulse, temperature, spo2, isFall, severity);

            if (severity == AlertSeverity.Normal)
            {
                if (_alertStates.TryRemove(monitoredId, out _))
                {
                    _logger.LogInformation("Monitored {MonitoredId} returned to normal and alert state was cleared.", monitoredId);
                }
                // Reset last notification so a new episode can trigger after 2 min
                _lastNotificationSent.TryRemove(monitoredId, out _);
                return;
            }


            var state = _alertStates.GetOrAdd(monitoredId, _ => new AlertState
            {
                FirstDetected = now,
                Severity = severity
            });

            if (severity > state.Severity)
            {
                _logger.LogInformation("Monitored {MonitoredId} severity escalated from {OldSeverity} to {NewSeverity}.", monitoredId, state.Severity, severity);
                state.Severity = severity;
                state.FirstDetected = now;
                state.ConsecutiveCount = 1;
            }
            else
            {
                state.ConsecutiveCount++;
            }

            state.LastDetected = now;
            state.LastPulse = pulse;
            state.LastTemperature = temperature;
            state.LastSpO2 = spo2;
            state.IsFall = isFall;

            var elapsed = now - state.FirstDetected;

            // Send notification every 2 minutes while in critical state
            if (state.Severity == AlertSeverity.Critical)
            {
                if (!_sendCriticalSmsImmediately)
                {
                    if (elapsed < CriticalSmsThreshold)
                    {
                        _logger.LogDebug("Monitored {MonitoredId} is critical for {Elapsed}. Waiting for threshold {Threshold}.", monitoredId, elapsed, CriticalSmsThreshold);
                        return;
                    }
                    if (_lastNotificationSent.TryGetValue(monitoredId, out var lastSent))
                    {
                        if ((now - lastSent) < CriticalSmsThreshold)
                        {
                            _logger.LogDebug("Waiting for next 2-minute interval for monitored {MonitoredId}. Last sent {LastSent}.", monitoredId, lastSent);
                            return;
                        }
                    }
                    _lastNotificationSent[monitoredId] = now;
                    state.ConsecutiveCount = 0;
                    _logger.LogInformation("Triggering notification send for monitored {MonitoredId} with severity {Severity} (every 2 minutes while critical persists).", monitoredId, state.Severity);
                    _ = Task.Run(() => SendNotificationsAsync(monitoredId, state));
                    return;
                }
                // Test mode: send immediately, or send after 2 min if not already sent
                if (_sendCriticalSmsImmediately || elapsed >= CriticalSmsThreshold)
                {
                    if (_lastNotificationSent.TryGetValue(monitoredId, out var lastSentCritical) && (now - lastSentCritical) < CriticalSmsThreshold)
                    {
                        _logger.LogDebug("Notification cooldown active for monitored {MonitoredId}. Last sent {LastSent}, threshold {Threshold}.", monitoredId, lastSentCritical, CriticalSmsThreshold);
                        return;
                    }
                    _lastNotificationSent[monitoredId] = now;
                    state.ConsecutiveCount = 0;
                    _logger.LogInformation("Triggering notification send for monitored {MonitoredId} with severity {Severity} (every 2 minutes while critical persists, test mode).", monitoredId, state.Severity);
                    _ = Task.Run(() => SendNotificationsAsync(monitoredId, state));
                }
                return;
            }

            // For non-critical alerts, keep previous logic
            if (state.Severity != AlertSeverity.Critical)
            {
                if (elapsed < PersistenceThreshold || state.ConsecutiveCount < 2)
                {
                    if (!_sendAlertSmsImmediately)
                    {
                        _logger.LogDebug("Monitored {MonitoredId} is alert for {Elapsed} with count {Count}. Waiting for persistence threshold {Threshold}.", monitoredId, elapsed, state.ConsecutiveCount, PersistenceThreshold);
                        return;
                    }

                    _logger.LogInformation("Monitored {MonitoredId} is alert for {Elapsed}, but immediate alert SMS test mode is enabled. Sending now.", monitoredId, elapsed);
                }

                // Check cooldown
                if (_lastNotificationSent.TryGetValue(monitoredId, out var lastSentAlert) && (now - lastSentAlert) < NotificationCooldown)
                {
                    _logger.LogDebug("Notification cooldown active for monitored {MonitoredId}. Last sent {LastSent}, cooldown {Cooldown}.", monitoredId, lastSentAlert, NotificationCooldown);
                    return;
                }

                // Send notifications
                _lastNotificationSent[monitoredId] = now;
                state.ConsecutiveCount = 0;
                state.FirstDetected = now;

                _logger.LogInformation("Triggering notification send for monitored {MonitoredId} with severity {Severity}.", monitoredId, state.Severity);
                _ = Task.Run(() => SendNotificationsAsync(monitoredId, state));
            }
        }

        private async Task SendNotificationsAsync(Guid monitoredId, AlertState state)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.Context.LifeAlertPlusDbContext>();
                var emailService = scope.ServiceProvider.GetRequiredService<Application.IServices.IEmailService>();
                var twilioService = scope.ServiceProvider.GetService<Application.IServices.ITwilioService>();

                // Find which users are watching this monitored person
                var userMonitoreds = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .ToListAsync(
                        dbContext.UserMonitoreds
                            .Where(um => um.IdMonitored == monitoredId)
                            .Include(um => um.User));

                // Get monitored person name
                var monitored = await dbContext.Monitoreds.FindAsync(monitoredId);
                var patientName = monitored != null ? $"{monitored.FirstName} {monitored.LastName}".Trim() : "Unknown";


                // Determină limba utilizatorului (primul watcher cu limbă setată, altfel "ro")
                var userLang = userMonitoreds.Select(um => um.User?.Language).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "ro";
                // TODO: Integrare context AI dacă există (ex: string aiContext = ...)
                string? aiContext = null;

                // Save a notification record (mesaj detaliat pentru email)
                var notification = new Domain.Entities.Notification
                {
                    Id = Guid.NewGuid(),
                    IdMonitored = monitoredId,
                    NotificationType = state.Severity == AlertSeverity.Critical ? "Critical" : "Alert",
                    Message = BuildNotificationMessage(state, patientName, userLang, aiContext, false, false),
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.Notifications.Add(notification);
                await dbContext.SaveChangesAsync();

                _logger.LogInformation("SendNotificationsAsync starting for monitored {MonitoredId} with {UserCount} watchers.", monitoredId, userMonitoreds.Count);

                foreach (var um in userMonitoreds)
                {
                    var user = um.User;
                    if (user == null || user.DeletedAt.HasValue) continue;

                    _logger.LogInformation("Preparing notifications for user {UserId} ({Email}) monitoring monitored {MonitoredId}. NotifyByEmail={NotifyByEmail}, NotifyBySms={NotifyBySms}, NotifyByPush={NotifyByPush}.", user.Id, user.Email, monitoredId, user.NotifyByEmail, user.NotifyBySms, user.NotifyByPush);

                    // Email notification
                    if (user.NotifyByEmail)
                    {
                        try
                        {
                            await emailService.SendAlertNotificationEmailAsync(
                                user.Email,
                                $"{user.FirstName} {user.LastName}".Trim(),
                                patientName,
                                state.Severity == AlertSeverity.Critical ? "CRITICAL" : "ALERT",
                                BuildNotificationMessage(state, patientName, user.Language ?? "ro", aiContext, false, false));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to send alert email to {Email} for monitored {MonitoredId}", user.Email, monitoredId);
                        }
                    }

                    // SMS notification via Twilio
                    if (user.NotifyBySms)
                    {
                        if (twilioService == null)
                        {
                            _logger.LogWarning("Twilio service is not available. Cannot send SMS for user {UserId} monitoring {MonitoredId}.", user.Id, monitoredId);
                        }
                        else if (string.IsNullOrWhiteSpace(user.PhoneNumber))
                        {
                            _logger.LogWarning("User {UserId} has NotifyBySms enabled but no phone number configured.", user.Id);
                        }
                        else
                        {
                            try
                            {
                                await twilioService.SendSmsAsync(
                                    user.PhoneNumber,
                                    BuildNotificationMessage(state, patientName, user.Language ?? "ro", aiContext, true, false)
                                );
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to send SMS to {Phone} for monitored {MonitoredId}", user.PhoneNumber, monitoredId);
                            }
                        }
                    }

                    // Push notification (SignalR)
                    if (user.NotifyByPush)
                    {
                        try
                        {
                            var pushMsg = BuildNotificationMessage(state, patientName, user.Language ?? "ro", aiContext, false, true);
                            await _pushNotificationService.SendPushNotificationAsync(user.Id, pushMsg, state.Severity == AlertSeverity.Critical ? "Critical" : state.Severity.ToString());
                            _logger.LogInformation("Push notification sent for user {UserId} about monitored {MonitoredId}: {Severity}", user.Id, monitoredId, state.Severity);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to send push notification to user {UserId} for monitored {MonitoredId}", user.Id, monitoredId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process notifications for monitored {MonitoredId}", monitoredId);
            }
        }

        private static AlertSeverity EvaluateSeverity(double pulse, double temperature, double spo2, bool isFall)
        {
            if (isFall) return AlertSeverity.Critical;
            if (spo2 > 0 && spo2 < 90) return AlertSeverity.Critical;
            if (pulse > 150 || (pulse > 0 && pulse < 40)) return AlertSeverity.Critical;
            if (temperature > 39.5 || (temperature > 0 && temperature < 34.5)) return AlertSeverity.Critical;

            if (spo2 > 0 && spo2 < 95) return AlertSeverity.Alert;
            if (pulse > 120 || (pulse > 0 && pulse < 50)) return AlertSeverity.Alert;
            if (temperature > 38.5 || (temperature > 0 && temperature < 35.5)) return AlertSeverity.Alert;

            return AlertSeverity.Normal;
        }

        private static string BuildNotificationMessage(AlertState state, string patientName, string lang = "ro", string? aiContext = null, bool isSms = false, bool isPush = false)
        {
            // Compose values
            string spo2Str = state.LastSpO2 > 0 ? $"SpO2: {state.LastSpO2:F0}%" : string.Empty;
            string pulseStr = state.LastPulse > 0 ? $"Puls: {state.LastPulse:F0}" : string.Empty;
            string tempStr = state.LastTemperature > 0 ? $"Temp: {state.LastTemperature:F1}°C" : string.Empty;

            // SMS/Short format
            if (isSms)
            {
                string arrowSpo2 = state.LastSpO2 > 0 && state.LastSpO2 < 95 ? "↓" : "";
                string arrowPulse = state.LastPulse > 120 ? "↑" : "";
                string sms = $"🚨 CRITICAL: {patientName}\n";
                sms += $"{(spo2Str != null ? spo2Str + (arrowSpo2 != "" ? " " + arrowSpo2 : "") : "")}";
                if (pulseStr != null) sms += $" | {pulseStr}{(arrowPulse != "" ? " " + arrowPulse : "")}";
                if (tempStr != null) sms += $" | {tempStr}";
                sms += "\nVerificați IMEDIAT!";
                return sms;
            }

            // Push notification format
            if (isPush)
            {
                string push = $"🚨 {patientName} – stare CRITICĂ\n";
                var details = new List<string>();
                if (state.LastSpO2 > 0) details.Add($"SpO2 scăzută ({state.LastSpO2:F0}%)");
                if (state.LastPulse > 0) details.Add($"puls {state.LastPulse:F0} bpm");
                if (state.LastTemperature > 38.5) details.Add("febră");
                push += string.Join(", ", details);
                push += "\nVerifică acum";
                return push;
            }

            // Email/long format
            var lines = new List<string>();
            if (state.LastSpO2 > 0)
                lines.Add($"⚠️ SpO2: {state.LastSpO2:F0}% {(state.LastSpO2 < 95 ? "(low)" : "")}");
            if (state.LastPulse > 0)
                lines.Add($"⚠️ Pulse: {state.LastPulse:F0} bpm {(state.LastPulse > 120 ? "(high)" : "")}");
            if (state.LastTemperature > 0)
                lines.Add($"⚠️ Temperature: {state.LastTemperature:F1}°C {(state.LastTemperature > 38.5 ? "(fever)" : "")}");
            lines.Add("\nImmediate action recommended: Please check on the patient now. Contact emergency services if the condition worsens.");
            return string.Join("\n", lines);
        }

        private class AlertState
        {
            public DateTime FirstDetected { get; set; }
            public DateTime LastDetected { get; set; }
            public AlertSeverity Severity { get; set; }
            public int ConsecutiveCount { get; set; }
            public double LastPulse { get; set; }
            public double LastTemperature { get; set; }
            public double LastSpO2 { get; set; }
            public bool IsFall { get; set; }
        }

        private enum AlertSeverity
        {
            Normal = 0,
            Alert = 1,
            Critical = 2
        }
    }
}
