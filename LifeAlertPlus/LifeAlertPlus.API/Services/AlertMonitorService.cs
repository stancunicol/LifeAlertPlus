using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.API.Services
{
    public class AlertMonitorService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AlertMonitorService> _logger;

        // Track alert state per monitored person: monitoredId -> AlertState
        private readonly ConcurrentDictionary<Guid, AlertState> _alertStates = new();

        // Cooldown: don't send another notification for the same monitored person within 10 minutes
        private readonly ConcurrentDictionary<Guid, DateTime> _lastNotificationSent = new();

        private static readonly TimeSpan PersistenceThreshold = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan NotificationCooldown = TimeSpan.FromMinutes(10);

        public AlertMonitorService(IServiceScopeFactory scopeFactory, ILogger<AlertMonitorService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task ProcessMeasurementAsync(Guid monitoredId, double pulse, double temperature, double spo2, bool isFall)
        {
            var now = DateTime.UtcNow;
            var severity = EvaluateSeverity(pulse, temperature, spo2, isFall);

            if (severity == AlertSeverity.Normal)
            {
                // Values returned to normal — clear the alert state
                _alertStates.TryRemove(monitoredId, out _);
                return;
            }

            var state = _alertStates.GetOrAdd(monitoredId, _ => new AlertState
            {
                FirstDetected = now,
                Severity = severity
            });

            state.LastDetected = now;
            state.LastPulse = pulse;
            state.LastTemperature = temperature;
            state.LastSpO2 = spo2;
            state.IsFall = isFall;
            state.ConsecutiveCount++;

            // If severity escalated, reset the clock
            if (severity > state.Severity)
            {
                state.Severity = severity;
                state.FirstDetected = now;
                state.ConsecutiveCount = 1;
            }

            // Check if alert has persisted long enough
            var elapsed = now - state.FirstDetected;
            if (elapsed < PersistenceThreshold || state.ConsecutiveCount < 2)
                return;

            // Check cooldown
            if (_lastNotificationSent.TryGetValue(monitoredId, out var lastSent) && (now - lastSent) < NotificationCooldown)
                return;

            // Send notifications
            _lastNotificationSent[monitoredId] = now;
            state.ConsecutiveCount = 0;
            state.FirstDetected = now;

            _ = Task.Run(() => SendNotificationsAsync(monitoredId, state));
        }

        private async Task SendNotificationsAsync(Guid monitoredId, AlertState state)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.Context.LifeAlertPlusDbContext>();
                var emailService = scope.ServiceProvider.GetRequiredService<Application.IServices.IEmailService>();

                // Find which users are watching this monitored person
                var userMonitoreds = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .ToListAsync(
                        dbContext.UserMonitoreds
                            .Where(um => um.IdMonitored == monitoredId)
                            .Include(um => um.User));

                // Get monitored person name
                var monitored = await dbContext.Monitoreds.FindAsync(monitoredId);
                var patientName = monitored != null ? $"{monitored.FirstName} {monitored.LastName}".Trim() : "Unknown";

                // Save a notification record
                var notification = new Domain.Entities.Notification
                {
                    Id = Guid.NewGuid(),
                    IdMonitored = monitoredId,
                    NotificationType = state.Severity == AlertSeverity.Critical ? "Critical" : "Alert",
                    Message = BuildNotificationMessage(state, patientName),
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.Notifications.Add(notification);
                await dbContext.SaveChangesAsync();

                foreach (var um in userMonitoreds)
                {
                    var user = um.User;
                    if (user == null || user.DeletedAt.HasValue) continue;

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
                                BuildNotificationMessage(state, patientName));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to send alert email to {Email} for monitored {MonitoredId}", user.Email, monitoredId);
                        }
                    }

                    // Push notification (stored in DB, client polls)
                    if (user.NotifyByPush)
                    {
                        _logger.LogInformation("Push notification queued for user {UserId} about monitored {MonitoredId}: {Severity}",
                            user.Id, monitoredId, state.Severity);
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

        private static string BuildNotificationMessage(AlertState state, string patientName)
        {
            var parts = new List<string>();

            if (state.IsFall)
                parts.Add("Fall detected");
            if (state.LastSpO2 > 0 && state.LastSpO2 < 95)
                parts.Add($"SpO2: {state.LastSpO2:F0}%");
            if (state.LastPulse > 120 || (state.LastPulse > 0 && state.LastPulse < 50))
                parts.Add($"Pulse: {state.LastPulse:F0} bpm");
            if (state.LastTemperature > 38.5 || (state.LastTemperature > 0 && state.LastTemperature < 35.5))
                parts.Add($"Temperature: {state.LastTemperature:F1}°C");

            if (parts.Count == 0)
                parts.Add("Abnormal vital signs detected");

            return $"{patientName}: {string.Join(", ", parts)}";
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
