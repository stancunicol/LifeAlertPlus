using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using LifeAlertPlus.Shared.DTOs.Responses.Monitoring;

namespace LifeAlertPlus.API.Services
{
    public class AlertMonitorService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AlertMonitorService> _logger;
        private readonly bool _sendCriticalSmsImmediately;
        private readonly bool _sendAlertSmsImmediately;
        private readonly ConcurrentDictionary<Guid, AlertState> _alertStates = new();

        // Cooldown: don't send another notification for the same monitored person within 10 minutes
        private readonly ConcurrentDictionary<Guid, DateTime> _lastNotificationSent = new();

        private static readonly TimeSpan PersistenceThreshold = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan CriticalSmsThreshold = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan NotificationCooldown = TimeSpan.FromMinutes(10);

        private readonly IPushNotificationService _pushNotificationService;
        private readonly ActivityProfileService _activityProfileService;
        private readonly ConditionRuleEngine _conditionRuleEngine;
        private readonly NearestHospitalService _nearestHospitalService;

        // Per-person threshold cache: patient-specific HR and temperature limits
        private readonly ConcurrentDictionary<Guid, (DateTime CachedAt, int? MaxHr, int? MinHr, double? MaxTemp, double? MinTemp)> _thresholdCache = new();
        private static readonly TimeSpan ThresholdCacheDuration = TimeSpan.FromHours(4);

        // Per-person metric buffer for last 120 seconds (2 minutes)
        private static readonly TimeSpan BufferWindow = TimeSpan.FromSeconds(120);
        private const int MaxBufferSize = 100;
        private readonly ConcurrentDictionary<Guid, MetricBuffer> _metricBuffers = new();

        private class MetricBuffer
        {
            public Queue<(DateTime Timestamp, double Value)> Pulse = new();
            public Queue<(DateTime Timestamp, double Value)> Temp = new();
            public Queue<(DateTime Timestamp, double Value)> SpO2 = new();
        }

        public AlertMonitorService(IServiceScopeFactory scopeFactory, ILogger<AlertMonitorService> logger, IConfiguration configuration, IPushNotificationService pushNotificationService, ActivityProfileService activityProfileService, ConditionRuleEngine conditionRuleEngine, NearestHospitalService nearestHospitalService)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _pushNotificationService = pushNotificationService;
            _activityProfileService = activityProfileService;
            _conditionRuleEngine = conditionRuleEngine;
            _nearestHospitalService = nearestHospitalService;
            _sendCriticalSmsImmediately = bool.TryParse(configuration["AlertMonitor:SendCriticalSmsImmediately"], out var immediate) && immediate;
            _sendAlertSmsImmediately = bool.TryParse(configuration["AlertMonitor:SendAlertSmsImmediately"], out var immediateAlert) && immediateAlert;

            if (_sendCriticalSmsImmediately)
                _logger.LogWarning("AlertMonitor is configured to send critical SMS immediately for testing.");
            if (_sendAlertSmsImmediately)
                _logger.LogWarning("AlertMonitor is configured to send alert SMS immediately for testing.");
        }

        public void InvalidateThresholdCache(Guid monitoredId) => _thresholdCache.TryRemove(monitoredId, out _);

        private async Task<(int? MaxHr, int? MinHr, double? MaxTemp, double? MinTemp)> GetPatientThresholdsAsync(Guid monitoredId)
        {
            if (_thresholdCache.TryGetValue(monitoredId, out var cached) &&
                (DateTime.UtcNow - cached.CachedAt) < ThresholdCacheDuration)
                return (cached.MaxHr, cached.MinHr, cached.MaxTemp, cached.MinTemp);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Context.LifeAlertPlusDbContext>();
                var monitored = await db.Monitoreds.FindAsync(monitoredId);
                var t = monitored != null
                    ? (monitored.MaxHeartRate, monitored.MinHeartRate, monitored.MaxTemperature, monitored.MinTemperature)
                    : ((int?)null, (int?)null, (double?)null, (double?)null);
                _thresholdCache[monitoredId] = (DateTime.UtcNow, t.Item1, t.Item2, t.Item3, t.Item4);
                return t;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load thresholds for {MonitoredId}. Using global defaults.", monitoredId);
                return (null, null, null, null);
            }
        }

        private void UpdateMetricBuffer(Guid id, DateTime now, double pulse, double temp, double spo2)
        {
            var buf = _metricBuffers.GetOrAdd(id, _ => new MetricBuffer());
            void Enqueue(Queue<(DateTime, double)> q, double v)
            {
                q.Enqueue((now, v));
                while (q.Count > 0 && (now - q.Peek().Item1) > BufferWindow) q.Dequeue();
                while (q.Count > MaxBufferSize) q.Dequeue();
            }
            Enqueue(buf.Pulse, pulse);
            Enqueue(buf.Temp, temp);
            Enqueue(buf.SpO2, spo2);
        }

        private static (double avg, double slope) ComputeStats(Queue<(DateTime Timestamp, double Value)> q)
        {
            if (q.Count < 2) return (q.Count == 1 ? q.Peek().Value : 0, 0);
            var arr = q.ToArray();
            double avg = arr.Average(x => x.Value);
            double dt = (arr.Last().Timestamp - arr.First().Timestamp).TotalSeconds;
            double slope = dt > 0 ? (arr.Last().Value - arr.First().Value) / dt : 0;
            return (avg, slope);
        }

        public async Task ProcessMeasurementAsync(Guid monitoredId, double pulse, double temperature, double spo2, bool isFall, string activity = "", string coordinates = "")
        {
            var now = DateTime.UtcNow;
            UpdateMetricBuffer(monitoredId, now, pulse, temperature, spo2);

            if (!string.IsNullOrWhiteSpace(activity))
                _ = Task.Run(() => CheckBehavioralAnomalyAsync(monitoredId, activity, pulse, now));

            var buf = _metricBuffers.GetOrAdd(monitoredId, _ => new MetricBuffer());
            var (pulseAvg, pulseSlope) = ComputeStats(buf.Pulse);
            var (tempAvg, tempSlope) = ComputeStats(buf.Temp);
            var (spo2Avg, spo2Slope) = ComputeStats(buf.SpO2);

            bool pulseRising = pulseSlope > 0.05 && pulseAvg > 100;
            bool tempRising = tempSlope > 0.01 && tempAvg > 37.5;
            bool spo2Dropping = spo2Slope < -0.02 && spo2Avg < 95;

            var (maxHr, minHr, maxTemp, minTemp) = await GetPatientThresholdsAsync(monitoredId);
            var severity = EvaluateSeverity(pulse, temperature, spo2, isFall, maxHr, minHr, maxTemp, minTemp);
            if (pulseRising || tempRising || spo2Dropping)
            {
                _logger.LogWarning("Trend alert for {MonitoredId}: pulseRising={PulseRising}, tempRising={TempRising}, spo2Dropping={Spo2Dropping}", monitoredId, pulseRising, tempRising, spo2Dropping);
                if (severity < AlertSeverity.Alert)
                    severity = AlertSeverity.Alert;
            }

            var (adjSeverity, conditionRecommendations, immediateAction) = await _conditionRuleEngine.EvaluateAsync(
                monitoredId, pulse, temperature, spo2, isFall, severity);
            severity = adjSeverity;

            _logger.LogInformation("Processing measurement for {MonitoredId}: pulse={Pulse}, temp={Temperature}, spo2={SpO2}, isFall={IsFall}, severity={Severity}.", monitoredId, pulse, temperature, spo2, isFall, severity);

            if (severity == AlertSeverity.Normal)
            {
                if (_alertStates.TryRemove(monitoredId, out _))
                    _logger.LogInformation("Monitored {MonitoredId} returned to normal and alert state was cleared.", monitoredId);
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
            state.ConditionRecommendations = conditionRecommendations;
            state.ImmediateAction = immediateAction;
            if (!string.IsNullOrWhiteSpace(coordinates))
                state.LastCoordinates = coordinates;

            if (immediateAction && severity == AlertSeverity.Critical)
            {
                _lastNotificationSent[monitoredId] = now;
                state.ConsecutiveCount = 0;
                _logger.LogInformation("ImmediateAction triggered for {MonitoredId} (condition-based fall detection). Bypassing all cooldowns.", monitoredId);
                _ = Task.Run(() => SendNotificationsAsync(monitoredId, state));
                return;
            }

            var elapsed = now - state.FirstDetected;

            if (state.Severity == AlertSeverity.Critical)
            {
                if (!_sendCriticalSmsImmediately)
                {
                    if (elapsed < CriticalSmsThreshold)
                    {
                        _logger.LogDebug("Monitored {MonitoredId} is critical for {Elapsed}. Waiting for threshold {Threshold}.", monitoredId, elapsed, CriticalSmsThreshold);
                        return;
                    }
                    if (_lastNotificationSent.TryGetValue(monitoredId, out var lastSent) && (now - lastSent) < CriticalSmsThreshold)
                    {
                        _logger.LogDebug("Waiting for next 2-minute interval for monitored {MonitoredId}. Last sent {LastSent}.", monitoredId, lastSent);
                        return;
                    }
                    _lastNotificationSent[monitoredId] = now;
                    state.ConsecutiveCount = 0;
                    _logger.LogInformation("Triggering notification send for monitored {MonitoredId} with severity {Severity} (every 2 minutes while critical persists).", monitoredId, state.Severity);
                    _ = Task.Run(() => SendNotificationsAsync(monitoredId, state));
                    return;
                }
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

                if (_lastNotificationSent.TryGetValue(monitoredId, out var lastSentAlert) && (now - lastSentAlert) < NotificationCooldown)
                {
                    _logger.LogDebug("Notification cooldown active for monitored {MonitoredId}. Last sent {LastSent}, cooldown {Cooldown}.", monitoredId, lastSentAlert, NotificationCooldown);
                    return;
                }

                _lastNotificationSent[monitoredId] = now;
                state.ConsecutiveCount = 0;
                state.FirstDetected = now;

                _logger.LogInformation("Triggering notification send for monitored {MonitoredId} with severity {Severity}.", monitoredId, state.Severity);
                _ = Task.Run(() => SendNotificationsAsync(monitoredId, state));
            }
        }

        public TrendPredictionResponseDTO GetTrendPredictions(Guid monitoredId)
        {
            var result = new TrendPredictionResponseDTO { GeneratedAt = DateTime.UtcNow };

            if (!_metricBuffers.TryGetValue(monitoredId, out var buf))
                return result;

            var tempArr = buf.Temp.ToArray();
            var pulseArr = buf.Pulse.ToArray();
            var spo2Arr = buf.SpO2.ToArray();

            result.BufferDataPoints = tempArr.Length;
            if (tempArr.Length > 1)
                result.BufferDurationSeconds = (tempArr.Last().Timestamp - tempArr.First().Timestamp).TotalSeconds;

            if (tempArr.Length < 3 && pulseArr.Length < 3 && spo2Arr.Length < 3)
                return result;

            // Temperature
            if (tempArr.Length >= 3)
            {
                var (tempAvg, tempSlope) = ComputeStats(buf.Temp);
                double lastTemp = tempArr.Last().Value;

                if (tempSlope > 0.005 && tempAvg >= 36.5)
                {
                    const double feverThreshold = 38.5;
                    int? secs = tempSlope > 0 && lastTemp < feverThreshold
                        ? (int)Math.Min(3600, Math.Max(0, (feverThreshold - lastTemp) / tempSlope))
                        : null;

                    result.Predictions.Add(new TrendPredictionItemDTO
                    {
                        Metric = "temperature",
                        Direction = "rising",
                        Label = tempAvg > 38.0 ? "Febră în evoluție" : "Posibilă febră",
                        Severity = tempAvg > 38.0 ? "danger" : "warning",
                        CurrentValue = Math.Round(lastTemp, 1),
                        AverageValue = Math.Round(tempAvg, 1),
                        ChangeRatePerMinute = Math.Round(tempSlope * 60, 2),
                        SecondsToThreshold = secs,
                        ThresholdDescription = "38.5°C (febră)"
                    });
                }
                else if (tempSlope < -0.005 && tempAvg <= 37.0)
                {
                    const double hypothermiaThreshold = 35.5;
                    int? secs = tempSlope < 0 && lastTemp > hypothermiaThreshold
                        ? (int)Math.Min(3600, Math.Max(0, (lastTemp - hypothermiaThreshold) / Math.Abs(tempSlope)))
                        : null;

                    result.Predictions.Add(new TrendPredictionItemDTO
                    {
                        Metric = "temperature",
                        Direction = "falling",
                        Label = tempAvg < 36.0 ? "Hipotermie în evoluție" : "Posibilă hipotermie",
                        Severity = tempAvg < 36.0 ? "danger" : "warning",
                        CurrentValue = Math.Round(lastTemp, 1),
                        AverageValue = Math.Round(tempAvg, 1),
                        ChangeRatePerMinute = Math.Round(tempSlope * 60, 2),
                        SecondsToThreshold = secs,
                        ThresholdDescription = "35.5°C (hipotermie)"
                    });
                }
            }

            // Pulse
            if (pulseArr.Length >= 3)
            {
                var (pulseAvg, pulseSlope) = ComputeStats(buf.Pulse);
                double lastPulse = pulseArr.Last().Value;

                if (pulseSlope > 0.03 && pulseAvg > 85)
                {
                    const double tachyThreshold = 120;
                    int? secs = pulseSlope > 0 && lastPulse < tachyThreshold
                        ? (int)Math.Min(3600, Math.Max(0, (tachyThreshold - lastPulse) / pulseSlope))
                        : null;

                    result.Predictions.Add(new TrendPredictionItemDTO
                    {
                        Metric = "pulse",
                        Direction = "rising",
                        Label = pulseAvg > 110 ? "Tahicardie în evoluție" : "Posibilă tahicardie",
                        Severity = pulseAvg > 110 ? "danger" : "warning",
                        CurrentValue = Math.Round(lastPulse),
                        AverageValue = Math.Round(pulseAvg),
                        ChangeRatePerMinute = Math.Round(pulseSlope * 60, 1),
                        SecondsToThreshold = secs,
                        ThresholdDescription = "120 bpm (tahicardie)"
                    });
                }
                else if (pulseSlope < -0.03 && pulseAvg < 75)
                {
                    const double bradyThreshold = 50;
                    int? secs = pulseSlope < 0 && lastPulse > bradyThreshold
                        ? (int)Math.Min(3600, Math.Max(0, (lastPulse - bradyThreshold) / Math.Abs(pulseSlope)))
                        : null;

                    result.Predictions.Add(new TrendPredictionItemDTO
                    {
                        Metric = "pulse",
                        Direction = "falling",
                        Label = pulseAvg < 60 ? "Bradicardie în evoluție" : "Posibilă bradicardie",
                        Severity = pulseAvg < 60 ? "danger" : "warning",
                        CurrentValue = Math.Round(lastPulse),
                        AverageValue = Math.Round(pulseAvg),
                        ChangeRatePerMinute = Math.Round(pulseSlope * 60, 1),
                        SecondsToThreshold = secs,
                        ThresholdDescription = "50 bpm (bradicardie)"
                    });
                }
            }

            // SpO2
            if (spo2Arr.Length >= 3)
            {
                var (spo2Avg, spo2Slope) = ComputeStats(buf.SpO2);
                double lastSpo2 = spo2Arr.Last().Value;

                if (spo2Slope < -0.01 && spo2Avg < 98 && spo2Avg > 0)
                {
                    const double hypoxiaThreshold = 95;
                    int? secs = spo2Slope < 0 && lastSpo2 > hypoxiaThreshold
                        ? (int)Math.Min(3600, Math.Max(0, (lastSpo2 - hypoxiaThreshold) / Math.Abs(spo2Slope)))
                        : null;

                    result.Predictions.Add(new TrendPredictionItemDTO
                    {
                        Metric = "spo2",
                        Direction = "falling",
                        Label = spo2Avg < 95 ? "Hipoxie în evoluție" : "Risc de hipoxie",
                        Severity = spo2Avg < 95 ? "danger" : "warning",
                        CurrentValue = Math.Round(lastSpo2, 1),
                        AverageValue = Math.Round(spo2Avg, 1),
                        ChangeRatePerMinute = Math.Round(spo2Slope * 60, 2),
                        SecondsToThreshold = secs,
                        ThresholdDescription = "95% SpO2 (hipoxie)"
                    });
                }
            }

            return result;
        }

        private async Task SendNotificationsAsync(Guid monitoredId, AlertState state)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.Context.LifeAlertPlusDbContext>();
                var emailService = scope.ServiceProvider.GetRequiredService<Application.IServices.IEmailService>();
                var twilioService = scope.ServiceProvider.GetService<Application.IServices.ITwilioService>();

                var userMonitoreds = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .ToListAsync(
                        dbContext.UserMonitoreds
                            .Where(um => um.IdMonitored == monitoredId)
                            .Include(um => um.User));

                var monitored = await dbContext.Monitoreds.FindAsync(monitoredId);
                var patientName = monitored != null ? $"{monitored.FirstName} {monitored.LastName}".Trim() : "Unknown";

                // Find nearest hospital when fall or critical and coordinates are available
                if ((state.IsFall || state.Severity == AlertSeverity.Critical)
                    && state.NearestHospital == null
                    && !string.IsNullOrWhiteSpace(state.LastCoordinates))
                {
                    var (lat, lon) = ParseCoordinates(state.LastCoordinates);
                    if (lat != 0 || lon != 0)
                        state.NearestHospital = _nearestHospitalService.FindNearest(lat, lon);
                }

                string? aiContext = null;
                string notifType = state.Severity == AlertSeverity.Critical ? "Critical" : "Alert";
                var createdAt = DateTime.UtcNow;

                _logger.LogInformation("SendNotificationsAsync starting for monitored {MonitoredId} with {UserCount} watchers.", monitoredId, userMonitoreds.Count);

                foreach (var um in userMonitoreds)
                {
                    var user = um.User;
                    if (user == null || user.DeletedAt.HasValue) continue;

                    var notification = new Domain.Entities.Notification
                    {
                        Id = Guid.NewGuid(),
                        IdUser = user.Id,
                        IdMonitored = monitoredId,
                        NotificationType = notifType,
                        Message = BuildNotificationMessage(state, patientName, user.Language ?? "en", aiContext, false, false),
                        CreatedAt = createdAt
                    };
                    dbContext.Notifications.Add(notification);

                    _logger.LogInformation("Preparing notifications for user {UserId} ({Email}) monitoring monitored {MonitoredId}.", user.Id, user.Email, monitoredId);

                    if (user.NotifyByEmail)
                    {
                        try
                        {
                            var userLang = user.Language ?? "en";
                            await emailService.SendAlertNotificationEmailAsync(
                                user.Email,
                                $"{user.FirstName} {user.LastName}".Trim(),
                                patientName,
                                state.Severity == AlertSeverity.Critical ? "CRITICAL" : "ALERT",
                                BuildNotificationMessage(state, patientName, userLang, aiContext, false, false),
                                userLang);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to send alert email to {Email} for monitored {MonitoredId}", user.Email, monitoredId);
                        }
                    }

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
                                    BuildNotificationMessage(state, patientName, user.Language ?? "en", aiContext, true, false));
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to send SMS to {Phone} for monitored {MonitoredId}", user.PhoneNumber, monitoredId);
                            }
                        }
                    }

                    if (user.NotifyByPush)
                    {
                        try
                        {
                            var pushMsg = BuildNotificationMessage(state, patientName, user.Language ?? "en", aiContext, false, true);
                            await _pushNotificationService.SendPushNotificationAsync(user.Id, pushMsg, state.Severity == AlertSeverity.Critical ? "Critical" : state.Severity.ToString());
                            _logger.LogInformation("Push notification sent for user {UserId} about monitored {MonitoredId}: {Severity}", user.Id, monitoredId, state.Severity);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to send push notification to user {UserId} for monitored {MonitoredId}", user.Id, monitoredId);
                        }
                    }
                }

                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process notifications for monitored {MonitoredId}", monitoredId);
            }
        }

        private static AlertSeverity EvaluateSeverity(
            double pulse, double temperature, double spo2, bool isFall,
            int? maxHr = null, int? minHr = null, double? maxTemp = null, double? minTemp = null)
        {
            // Global defaults (clinical evidence-based)
            int critMaxHr   = maxHr   ?? 150;
            int critMinHr   = minHr   ?? 40;
            double critMaxT = maxTemp ?? 39.5;
            double critMinT = minTemp ?? 34.5;

            // Alert thresholds: 85% of critical HR range, 1°C below critical temperature
            int alertMaxHr   = (int)(critMaxHr * 0.80);   // 150→120, custom 100→80
            int alertMinHr   = (int)(critMinHr * 1.25);   //  40→50,  custom  55→69
            double alertMaxT = critMaxT - 1.0;            // 39.5→38.5
            double alertMinT = critMinT + 1.0;            // 34.5→35.5

            if (isFall) return AlertSeverity.Critical;
            if (spo2 > 0 && spo2 < 90) return AlertSeverity.Critical;
            if (pulse > critMaxHr || (pulse > 0 && pulse < critMinHr)) return AlertSeverity.Critical;
            if (temperature > critMaxT || (temperature > 0 && temperature < critMinT)) return AlertSeverity.Critical;

            if (spo2 > 0 && spo2 < 95) return AlertSeverity.Alert;
            if (pulse > alertMaxHr || (pulse > 0 && pulse < alertMinHr)) return AlertSeverity.Alert;
            if (temperature > alertMaxT || (temperature > 0 && temperature < alertMinT)) return AlertSeverity.Alert;

            return AlertSeverity.Normal;
        }

        private static string BuildNotificationMessage(AlertState state, string patientName, string lang = "ro", string? aiContext = null, bool isSms = false, bool isPush = false)
        {
            bool isRo = !string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase);
            var h = state.NearestHospital;

            if (isSms)
            {
                string arrowSpo2  = state.LastSpO2  > 0 && state.LastSpO2  < 95 ? "↓" : "";
                string arrowPulse = state.LastPulse > 120 ? "↑" : "";
                string prefix = state.Severity == AlertSeverity.Critical
                    ? (isRo ? "🚨 CRITIC" : "🚨 CRITICAL")
                    : (isRo ? "⚠️ ALERTĂ" : "⚠️ ALERT");
                string sms = $"{prefix}: {patientName}\n";
                if (state.LastSpO2        > 0) sms += $"SpO2: {state.LastSpO2:F0}%{(arrowSpo2 != "" ? " " + arrowSpo2 : "")}";
                if (state.LastPulse       > 0) sms += $" | {(isRo ? "Puls" : "HR")}: {state.LastPulse:F0}{(arrowPulse != "" ? " " + arrowPulse : "")}";
                if (state.LastTemperature > 0) sms += $" | Temp: {state.LastTemperature:F1}°C";
                sms += isRo ? "\nVerificați IMEDIAT!" : "\nCheck IMMEDIATELY!";
                if (state.ConditionRecommendations.Count > 0)
                    sms += $"\n{state.ConditionRecommendations[0]}";
                if (h != null)
                    sms += $"\n🏥 {h.HospitalName}, {h.City} (~{h.EstimatedMinutes} min)";
                return sms;
            }

            if (isPush)
            {
                string prefix = state.Severity == AlertSeverity.Critical ? "🚨" : "⚠️";
                string statusLabel = state.Severity == AlertSeverity.Critical
                    ? (isRo ? "stare CRITICĂ" : "CRITICAL state")
                    : (isRo ? "alertă" : "alert");
                string push = $"{prefix} {patientName} – {statusLabel}\n";
                var details = new List<string>();
                if (state.LastSpO2        > 0)    details.Add(isRo ? $"SpO2 scăzută ({state.LastSpO2:F0}%)" : $"low SpO2 ({state.LastSpO2:F0}%)");
                if (state.LastPulse       > 0)    details.Add(isRo ? $"puls {state.LastPulse:F0} bpm" : $"HR {state.LastPulse:F0} bpm");
                if (state.LastTemperature > 38.5) details.Add(isRo ? "febră" : "fever");
                if (state.IsFall)                 details.Add(isRo ? "cădere detectată" : "fall detected");
                push += string.Join(", ", details);
                if (state.ConditionRecommendations.Count > 0)
                    push += $"\n{state.ConditionRecommendations[0]}";
                else
                    push += isRo ? "\nVerifică acum" : "\nCheck now";
                if (h != null)
                    push += $"\n🏥 {h.HospitalName} – ~{h.EstimatedMinutes} min";
                return push;
            }

            // ── Full notification (email / in-app) ──────────────────────────────
            var lines = new List<string>();
            string severityLabel = state.Severity == AlertSeverity.Critical
                ? (isRo ? "CRITIC" : "CRITICAL")
                : (isRo ? "ALERTĂ" : "ALERT");
            lines.Add(isRo
                ? $"Pacient: {patientName} | Severitate: {severityLabel}"
                : $"Patient: {patientName} | Severity: {severityLabel}");
            lines.Add("");
            if (state.LastSpO2 > 0)
                lines.Add(isRo
                    ? $"⚠️ SpO2: {state.LastSpO2:F0}%{(state.LastSpO2 < 95 ? " (scăzut)" : "")}"
                    : $"⚠️ SpO2: {state.LastSpO2:F0}%{(state.LastSpO2 < 95 ? " (low)" : "")}");
            if (state.LastPulse > 0)
                lines.Add(isRo
                    ? $"⚠️ Puls: {state.LastPulse:F0} bpm{(state.LastPulse > 120 ? " (crescut)" : state.LastPulse < 50 ? " (scăzut)" : "")}"
                    : $"⚠️ Heart rate: {state.LastPulse:F0} bpm{(state.LastPulse > 120 ? " (high)" : state.LastPulse < 50 ? " (low)" : "")}");
            if (state.LastTemperature > 0)
                lines.Add(isRo
                    ? $"⚠️ Temperatură: {state.LastTemperature:F1}°C{(state.LastTemperature > 38.5 ? " (febră)" : state.LastTemperature < 35.5 ? " (hipotermie)" : "")}"
                    : $"⚠️ Temperature: {state.LastTemperature:F1}°C{(state.LastTemperature > 38.5 ? " (fever)" : state.LastTemperature < 35.5 ? " (hypothermia)" : "")}");
            if (state.IsFall)
                lines.Add(isRo ? "⚠️ Cădere detectată!" : "⚠️ Fall detected!");

            if (state.ConditionRecommendations.Count > 0)
            {
                lines.Add("");
                lines.Add(isRo ? "📋 Recomandări medicale:" : "📋 Medical recommendations:");
                foreach (var rec in state.ConditionRecommendations)
                    lines.Add($"  • {rec}");
            }

            if (h != null)
            {
                lines.Add("");
                lines.Add(isRo ? "🏥 Cel mai apropiat spital de urgență:" : "🏥 Nearest emergency hospital:");
                lines.Add($"  {h.HospitalName}, {h.City} (~{h.EstimatedMinutes} min)");
                if (h.Route.Count > 0)
                    lines.Add(isRo
                        ? $"  Rută: {string.Join(" → ", h.Route)}"
                        : $"  Route: {string.Join(" → ", h.Route)}");
            }

            lines.Add("");
            lines.Add(isRo
                ? "Acțiune imediată recomandată: Verificați pacientul acum. Contactați serviciile de urgență dacă situația se agravează."
                : "Immediate action recommended: Check the patient now. Contact emergency services if the situation worsens.");
            return string.Join("\n", lines);
        }

        private static (double Lat, double Lon) ParseCoordinates(string coordinates)
        {
            if (string.IsNullOrWhiteSpace(coordinates)) return (0, 0);
            var sep = coordinates.Contains(',') ? ',' : ' ';
            var parts = coordinates.Split(sep, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return (0, 0);
            if (double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var lat) &&
                double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var lon))
                return (lat, lon);
            return (0, 0);
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
            public List<string> ConditionRecommendations { get; set; } = new();
            public bool ImmediateAction { get; set; }
            public string LastCoordinates { get; set; } = string.Empty;
            public HospitalRouteResult? NearestHospital { get; set; }
        }

        private async Task CheckBehavioralAnomalyAsync(Guid monitoredId, string activity, double pulse, DateTime now)
        {
            try
            {
                var (hasAnomaly, messageRo, messageEn, type) = await _activityProfileService.CheckAnomalyAsync(monitoredId, activity, pulse, now);
                if (hasAnomaly && !string.IsNullOrEmpty(messageRo))
                {
                    _logger.LogInformation("Behavioral anomaly for {MonitoredId} [{Type}]: {Message}", monitoredId, type, messageRo);
                    await SendBehavioralNotificationsAsync(monitoredId, messageRo, messageEn!, now);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Behavioral anomaly check failed for {MonitoredId}.", monitoredId);
            }
        }

        private async Task SendBehavioralNotificationsAsync(Guid monitoredId, string messageRo, string messageEn, DateTime now)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.Context.LifeAlertPlusDbContext>();

                var monitored = await dbContext.Monitoreds.FindAsync(monitoredId);
                var patientName = monitored != null ? $"{monitored.FirstName} {monitored.LastName}".Trim() : "Unknown";

                var userMonitoreds = await dbContext.UserMonitoreds
                    .Where(um => um.IdMonitored == monitoredId)
                    .Include(um => um.User)
                    .ToListAsync();

                foreach (var um in userMonitoreds)
                {
                    var user = um.User;
                    if (user == null || user.DeletedAt.HasValue) continue;

                    bool isEn = string.Equals(user.Language, "en", StringComparison.OrdinalIgnoreCase);
                    string message = isEn ? messageEn : messageRo;

                    dbContext.Notifications.Add(new Domain.Entities.Notification
                    {
                        Id = Guid.NewGuid(),
                        IdUser = user.Id,
                        IdMonitored = monitoredId,
                        NotificationType = "Alert",
                        Message = message,
                        CreatedAt = now
                    });

                    if (user.NotifyByPush)
                    {
                        try
                        {
                            await _pushNotificationService.SendPushNotificationAsync(user.Id, $"⚠️ {patientName} – {message}", "Alert");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to send behavioral push notification to user {UserId}.", user.Id);
                        }
                    }
                }

                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send behavioral notifications for {MonitoredId}.", monitoredId);
            }
        }

    }
}
