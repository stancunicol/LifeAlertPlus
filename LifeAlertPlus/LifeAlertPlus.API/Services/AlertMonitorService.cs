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

        // Per-person threshold cache: patient-specific HR, temperature and SpO2 limits
        private readonly ConcurrentDictionary<Guid, (DateTime CachedAt, int? MaxHr, int? MinHr, double? MaxTemp, double? MinTemp, int? MinSpO2, int? MaxSpO2)> _thresholdCache = new();
        private static readonly TimeSpan ThresholdCacheDuration = TimeSpan.FromHours(4);

        // Per-person metric buffer for last 120 seconds (2 minutes)
        private static readonly TimeSpan BufferWindow = TimeSpan.FromSeconds(120);
        private const int MaxBufferSize = 100;
        private readonly ConcurrentDictionary<Guid, MetricBuffer> _metricBuffers = new();

        // Throttle for the orphan-feedback recovery sweep (handles the case where
        // _alertStates was lost — e.g. backend restart mid-alert — so notifications
        // sent in a previous process never got their FeedbackRequestedAt set).
        private readonly ConcurrentDictionary<Guid, DateTime> _lastFeedbackSweep = new();
        private static readonly TimeSpan FeedbackSweepThrottle = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan FeedbackSweepLookback = TimeSpan.FromHours(6);

        // Per-monitored short-window buffer of recent vital readings.
        // Concurrent ProcessMeasurementAsync calls for the same patient (e.g. simulation
        // overlapping with real ESP ingest, or a burst of ESP packets) would otherwise
        // race on the internal Queue<T> arrays. All access goes through the Sync object.
        private class MetricBuffer
        {
            public readonly object Sync = new();
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

        // Cached "is the monitored person archived?" check — archived persons are skipped
        // by all alert/measurement flows. State changes rarely so a short cache is fine.
        private readonly ConcurrentDictionary<Guid, (DateTime CachedAt, bool IsArchived)> _archivedCache = new();
        private static readonly TimeSpan ArchivedCacheDuration = TimeSpan.FromMinutes(5);

        public void InvalidateArchivedCache(Guid monitoredId) => _archivedCache.TryRemove(monitoredId, out _);

        // Battery low notification: fire at most once per 6 hours per device serial.
        private static readonly TimeSpan BatteryNotifCooldown = TimeSpan.FromHours(6);
        private static readonly double BatteryLowThreshold = 20.0;
        private readonly ConcurrentDictionary<string, DateTime> _lastBatteryNotif = new(StringComparer.OrdinalIgnoreCase);

        public async Task CheckBatteryAsync(Guid monitoredId, string serial, double battery)
        {
            if (battery >= BatteryLowThreshold) return;
            if (_lastBatteryNotif.TryGetValue(serial, out var last) && (DateTime.UtcNow - last) < BatteryNotifCooldown) return;
            _lastBatteryNotif[serial] = DateTime.UtcNow;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Context.LifeAlertPlusDbContext>();
                var monitored = await db.Monitoreds.FindAsync(monitoredId);
                if (monitored == null) return;
                var patientName = $"{monitored.FirstName} {monitored.LastName}".Trim();
                var messageRo = $"Bateria dispozitivului lui {patientName} este la {battery:F0}%. Încărcați dispozitivul.";
                var messageEn = $"Battery for {patientName}'s device is at {battery:F0}%. Please charge the device.";

                var links = await db.UserMonitoreds.Where(um => um.IdMonitored == monitoredId).Include(um => um.User).ToListAsync();
                foreach (var um in links)
                {
                    var user = um.User;
                    if (user == null || user.DeletedAt.HasValue) continue;
                    var msg = string.Equals(user.Language, "en", StringComparison.OrdinalIgnoreCase) ? messageEn : messageRo;
                    db.Notifications.Add(new Domain.Entities.Notification { Id = Guid.NewGuid(), IdUser = user.Id, IdMonitored = monitoredId, NotificationType = "Info", Message = msg, CreatedAt = DateTime.UtcNow });
                    if (user.NotifyByPush)
                        FireAndForget(async () => await _pushNotificationService.SendPushNotificationAsync(user.Id, $"🔋 {msg}", "Info"), "BatteryLowPush", monitoredId);
                }
                await db.SaveChangesAsync();
                _logger.LogInformation("Battery low notification sent for {Serial} ({Battery}%)", serial, battery);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Battery low notification failed for {Serial}", serial); }
        }

        // Per-serial ingest rate limiter: max 4 requests per 60 s window.
        private const int IngestMaxPerWindow = 4;
        private static readonly TimeSpan IngestWindow = TimeSpan.FromSeconds(60);
        private readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _ingestRates = new(StringComparer.OrdinalIgnoreCase);

        public bool IsIngestAllowed(string serial)
        {
            var now = DateTime.UtcNow;
            var entry = _ingestRates.GetOrAdd(serial, _ => (0, now));
            if ((now - entry.WindowStart) >= IngestWindow)
            {
                _ingestRates[serial] = (1, now);
                return true;
            }
            if (entry.Count >= IngestMaxPerWindow)
                return false;
            _ingestRates[serial] = (entry.Count + 1, entry.WindowStart);
            return true;
        }

        private async Task<bool> IsArchivedAsync(Guid monitoredId)
        {
            if (_archivedCache.TryGetValue(monitoredId, out var cached) &&
                (DateTime.UtcNow - cached.CachedAt) < ArchivedCacheDuration)
                return cached.IsArchived;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Context.LifeAlertPlusDbContext>();
                var isArchived = await db.Monitoreds
                    .Where(m => m.Id == monitoredId)
                    .Select(m => (bool?)m.IsArchived)
                    .FirstOrDefaultAsync() ?? false;
                _archivedCache[monitoredId] = (DateTime.UtcNow, isArchived);
                return isArchived;
            }
            catch
            {
                return false;
            }
        }

        private async Task<(int? MaxHr, int? MinHr, double? MaxTemp, double? MinTemp, int? MinSpO2, int? MaxSpO2)> GetPatientThresholdsAsync(Guid monitoredId)
        {
            if (_thresholdCache.TryGetValue(monitoredId, out var cached) &&
                (DateTime.UtcNow - cached.CachedAt) < ThresholdCacheDuration)
                return (cached.MaxHr, cached.MinHr, cached.MaxTemp, cached.MinTemp, cached.MinSpO2, cached.MaxSpO2);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Context.LifeAlertPlusDbContext>();
                var monitored = await db.Monitoreds.FindAsync(monitoredId);
                int? maxHr    = monitored?.MaxHeartRate;
                int? minHr    = monitored?.MinHeartRate;
                double? maxT  = monitored?.MaxTemperature;
                double? minT  = monitored?.MinTemperature;
                int? minSpO2  = monitored?.MinSpO2;
                int? maxSpO2  = monitored?.MaxSpO2;
                _thresholdCache[monitoredId] = (DateTime.UtcNow, maxHr, minHr, maxT, minT, minSpO2, maxSpO2);
                return (maxHr, minHr, maxT, minT, minSpO2, maxSpO2);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load thresholds for {MonitoredId}. Using global defaults.", monitoredId);
                return (null, null, null, null, null, null);
            }
        }

        private void UpdateMetricBuffer(Guid id, DateTime now, double pulse, double temp, double spo2)
        {
            var buf = _metricBuffers.GetOrAdd(id, _ => new MetricBuffer());
            lock (buf.Sync)
            {
                static void EnqueueAndTrim(Queue<(DateTime Timestamp, double Value)> q, DateTime now, double v)
                {
                    q.Enqueue((now, v));
                    while (q.Count > 0 && (now - q.Peek().Timestamp) > BufferWindow) q.Dequeue();
                    while (q.Count > MaxBufferSize) q.Dequeue();
                }
                EnqueueAndTrim(buf.Pulse, now, pulse);
                EnqueueAndTrim(buf.Temp, now, temp);
                EnqueueAndTrim(buf.SpO2, now, spo2);
            }
        }

        // Returns immutable arrays so the rest of ProcessMeasurementAsync can read
        // without holding the buffer lock.
        private static ((DateTime Timestamp, double Value)[] Pulse, (DateTime Timestamp, double Value)[] Temp, (DateTime Timestamp, double Value)[] SpO2) SnapshotBuffer(MetricBuffer buf)
        {
            lock (buf.Sync)
            {
                return (buf.Pulse.ToArray(), buf.Temp.ToArray(), buf.SpO2.ToArray());
            }
        }

        private static (double avg, double slope) ComputeStatsFromArray((DateTime Timestamp, double Value)[] arr)
        {
            if (arr.Length < 2) return (arr.Length == 1 ? arr[0].Value : 0, 0);
            double avg = arr.Average(x => x.Value);
            double dt = (arr[^1].Timestamp - arr[0].Timestamp).TotalSeconds;
            double slope = dt > 0 ? (arr[^1].Value - arr[0].Value) / dt : 0;
            return (avg, slope);
        }

        // Wraps fire-and-forget work so background failures are logged instead of
        // disappearing into a forgotten Task. Used for behavioral checks, notification
        // sends, hospital lookups, etc.
        private void FireAndForget(Func<Task> work, string operation, Guid? monitoredId = null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await work();
                }
                catch (Exception ex)
                {
                    if (monitoredId.HasValue)
                        _logger.LogError(ex, "Fire-and-forget {Operation} failed for monitored {MonitoredId}", operation, monitoredId.Value);
                    else
                        _logger.LogError(ex, "Fire-and-forget {Operation} failed", operation);
                }
            });
        }

        public async Task ProcessMeasurementAsync(Guid monitoredId, double pulse, double temperature, double spo2, bool isFall, string activity = "", string coordinates = "")
        {
            if (await IsArchivedAsync(monitoredId))
            {
                _logger.LogDebug("ProcessMeasurement skipped — monitored {MonitoredId} is archived", monitoredId);
                return;
            }

            var now = DateTime.UtcNow;
            UpdateMetricBuffer(monitoredId, now, pulse, temperature, spo2);

            if (!string.IsNullOrWhiteSpace(activity))
                FireAndForget(() => CheckBehavioralAnomalyAsync(monitoredId, activity, pulse, now),
                              "CheckBehavioralAnomalyAsync", monitoredId);

            var buf = _metricBuffers.GetOrAdd(monitoredId, _ => new MetricBuffer());
            var (pulseArr, tempArr, spo2Arr) = SnapshotBuffer(buf);
            var (pulseAvg, pulseSlope) = ComputeStatsFromArray(pulseArr);
            var (tempAvg, tempSlope) = ComputeStatsFromArray(tempArr);
            var (spo2Avg, spo2Slope) = ComputeStatsFromArray(spo2Arr);

            var (maxHr, minHr, maxTemp, minTemp, minSpO2, maxSpO2) = await GetPatientThresholdsAsync(monitoredId);
            int effectiveMinSpO2 = minSpO2 ?? 95;

            bool pulseRising  = pulseSlope > 0.05 && pulseAvg > 100;
            bool tempRising   = tempSlope  > 0.01 && tempAvg  > 37.5;
            bool spo2Dropping = spo2Slope  < -0.02 && spo2Avg < effectiveMinSpO2;

            var severity = EvaluateSeverity(pulse, temperature, spo2, isFall, maxHr, minHr, maxTemp, minTemp, minSpO2);
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
                if (_alertStates.TryRemove(monitoredId, out var resolvedState))
                {
                    // Happy path: this process started the alert, so we know exactly
                    // which episode just resolved and can run the sweep right away.
                    _logger.LogInformation("Monitored {MonitoredId} returned to normal and alert state was cleared.", monitoredId);
                    _lastFeedbackSweep[monitoredId] = now;
                    FireAndForget(() => MarkFeedbackRequestedAsync(monitoredId, resolvedState.FirstDetected),
                                  "MarkFeedbackRequested(resolved)", monitoredId);
                }
                else if (!_lastFeedbackSweep.TryGetValue(monitoredId, out var lastSweep)
                         || (now - lastSweep) > FeedbackSweepThrottle)
                {
                    // Recovery sweep: covers the case where the backend was restarted
                    // (or the process otherwise lost _alertStates) while the patient
                    // was in alert/critical state. Without this, notifications sent
                    // before the restart would stay with FeedbackRequestedAt == null
                    // forever and the user-facing popup would never appear on next login.
                    // Throttled per-monitored so we don't query on every Normal sample.
                    _lastFeedbackSweep[monitoredId] = now;
                    FireAndForget(() => MarkFeedbackRequestedAsync(monitoredId, now - FeedbackSweepLookback),
                                  "MarkFeedbackRequested(sweep)", monitoredId);
                }
                _lastNotificationSent.TryRemove(monitoredId, out _);
                return;
            }

            var state = _alertStates.GetOrAdd(monitoredId, _ => new AlertState
            {
                FirstDetected = now,
                Severity = severity
            });

            // All decisions + mutations on this patient's AlertState happen under one
            // lock so concurrent ProcessMeasurementAsync calls (same patient, different
            // threads) can't shred ConsecutiveCount, escalate Severity inconsistently,
            // or double-fire notifications.
            lock (state.Sync)
            {
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
                state.MinSpO2 = minSpO2;
                state.ConditionRecommendations = conditionRecommendations;
                state.ImmediateAction = immediateAction;
                if (!string.IsNullOrWhiteSpace(coordinates))
                {
                    state.LastCoordinates = coordinates;
                    // Pre-fetch the nearest hospital in background as soon as alert/critical starts.
                    // This gives the Overpass API the full alert persistence window (~1-2 min) to respond
                    // and cache the result before the notification is actually sent.
                    if (severity >= AlertSeverity.Alert && state.NearestHospital == null)
                    {
                        var (hLat, hLon) = ParseCoordinates(coordinates);
                        if (hLat != 0 || hLon != 0)
                            FireAndForget(async () =>
                            {
                                var hosp = await _nearestHospitalService.FindNearestAsync(hLat, hLon);
                                lock (state.Sync) { state.NearestHospital = hosp; }
                            }, "NearestHospitalLookup", monitoredId);
                    }
                }

                if (immediateAction && severity == AlertSeverity.Critical)
                {
                    _lastNotificationSent[monitoredId] = now;
                    state.ConsecutiveCount = 0;
                    _logger.LogInformation("ImmediateAction triggered for {MonitoredId} (condition-based fall detection). Bypassing all cooldowns.", monitoredId);
                    FireAndForget(() => SendNotificationsAsync(monitoredId, state), "SendNotifications(immediate)", monitoredId);
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
                        FireAndForget(() => SendNotificationsAsync(monitoredId, state), "SendNotifications(critical)", monitoredId);
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
                        FireAndForget(() => SendNotificationsAsync(monitoredId, state), "SendNotifications(critical-immediate)", monitoredId);
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
                    FireAndForget(() => SendNotificationsAsync(monitoredId, state), "SendNotifications(alert)", monitoredId);
                }
            } // lock(state.Sync)
        }

        public TrendPredictionResponseDTO GetTrendPredictions(Guid monitoredId)
        {
            var result = new TrendPredictionResponseDTO { GeneratedAt = DateTime.UtcNow };

            if (!_metricBuffers.TryGetValue(monitoredId, out var buf))
                return result;

            // Read the buffer atomically — concurrent UpdateMetricBuffer may be
            // enqueuing/trimming on the same Queue<T>.
            var (pulseArr, tempArr, spo2Arr) = SnapshotBuffer(buf);

            result.BufferDataPoints = tempArr.Length;
            if (tempArr.Length > 1)
                result.BufferDurationSeconds = (tempArr.Last().Timestamp - tempArr.First().Timestamp).TotalSeconds;

            if (tempArr.Length < 3 && pulseArr.Length < 3 && spo2Arr.Length < 3)
                return result;

            // Temperature
            if (tempArr.Length >= 3)
            {
                var (tempAvg, tempSlope) = ComputeStatsFromArray(tempArr);
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
                var (pulseAvg, pulseSlope) = ComputeStatsFromArray(pulseArr);
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
                var (spo2Avg, spo2Slope) = ComputeStatsFromArray(spo2Arr);
                double lastSpo2 = spo2Arr.Last().Value;

                int? cachedMinSpO2 = null;
                if (_thresholdCache.TryGetValue(monitoredId, out var cachedT))
                    cachedMinSpO2 = cachedT.MinSpO2;
                double hypoxiaThreshold = cachedMinSpO2 ?? 95;

                if (spo2Slope < -0.01 && spo2Avg < hypoxiaThreshold + 3 && spo2Avg > 0)
                {
                    int? secs = spo2Slope < 0 && lastSpo2 > hypoxiaThreshold
                        ? (int)Math.Min(3600, Math.Max(0, (lastSpo2 - hypoxiaThreshold) / Math.Abs(spo2Slope)))
                        : null;

                    result.Predictions.Add(new TrendPredictionItemDTO
                    {
                        Metric = "spo2",
                        Direction = "falling",
                        Label = spo2Avg < hypoxiaThreshold ? "Hipoxie în evoluție" : "Risc de hipoxie",
                        Severity = spo2Avg < hypoxiaThreshold ? "danger" : "warning",
                        CurrentValue = Math.Round(lastSpo2, 1),
                        AverageValue = Math.Round(spo2Avg, 1),
                        ChangeRatePerMinute = Math.Round(spo2Slope * 60, 2),
                        SecondsToThreshold = secs,
                        ThresholdDescription = $"{hypoxiaThreshold}% SpO2 (hipoxie)"
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

                // Nearest hospital: should already be in state from the background pre-fetch started at
                // alert onset. This fallback covers the rare case where the pre-fetch didn't complete.
                if (state.NearestHospital == null && !string.IsNullOrWhiteSpace(state.LastCoordinates))
                {
                    var (lat, lon) = ParseCoordinates(state.LastCoordinates);
                    if (lat != 0 || lon != 0)
                    {
                        try
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                            state.NearestHospital = await _nearestHospitalService.FindNearestAsync(lat, lon, cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogWarning("NearestHospital fallback lookup timed out for monitored {MonitoredId} — sending notification without hospital info.", monitoredId);
                        }
                    }
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
                            _logger.LogError(ex, "Failed to send alert email to {Email} for monitored {MonitoredId}", user.Email, monitoredId);
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
                                _logger.LogError(ex, "Failed to send SMS to {Phone} for monitored {MonitoredId}", user.PhoneNumber, monitoredId);
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
            int? maxHr = null, int? minHr = null, double? maxTemp = null, double? minTemp = null,
            int? minSpO2 = null)
        {
            // Global defaults (clinical evidence-based)
            int critMaxHr   = maxHr   ?? 150;
            int critMinHr   = minHr   ?? 40;
            double critMaxT = maxTemp ?? 39.5;
            double critMinT = minTemp ?? 34.5;

            // SpO2: alert at patient's min, critical 5pp below that
            int alertSpO2 = minSpO2 ?? 95;
            int critSpO2  = Math.Max(70, alertSpO2 - 5);

            // Alert thresholds: 85% of critical HR range, 1°C below critical temperature
            int alertMaxHr   = (int)(critMaxHr * 0.80);
            int alertMinHr   = (int)(critMinHr * 1.25);
            double alertMaxT = critMaxT - 1.0;
            double alertMinT = critMinT + 1.0;

            if (isFall) return AlertSeverity.Critical;
            if (spo2 > 0 && spo2 < critSpO2) return AlertSeverity.Critical;
            if (pulse > critMaxHr || (pulse > 0 && pulse < critMinHr)) return AlertSeverity.Critical;
            if (temperature > critMaxT || (temperature > 0 && temperature < critMinT)) return AlertSeverity.Critical;

            if (spo2 > 0 && spo2 < alertSpO2) return AlertSeverity.Alert;
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
                int spo2AlertThreshold = state.MinSpO2 ?? 95;
                string arrowSpo2  = state.LastSpO2  > 0 && state.LastSpO2  < spo2AlertThreshold ? "↓" : "";
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
                    sms += $"\n🏥 {h.HospitalName} (~{h.EstimatedMinutes} min, {h.DistanceKm} km)";
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
                    ? $"⚠️ SpO2: {state.LastSpO2:F0}%{(state.LastSpO2 < (state.MinSpO2 ?? 95) ? " (scăzut)" : "")}"
                    : $"⚠️ SpO2: {state.LastSpO2:F0}%{(state.LastSpO2 < (state.MinSpO2 ?? 95) ? " (low)" : "")}");
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
                lines.Add($"  {h.HospitalName} (~{h.EstimatedMinutes} min, {h.DistanceKm} km)");
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
            // Sync root for serializing concurrent same-patient mutations.
            // ConcurrentDictionary protects the dict, not the value object.
            public readonly object Sync = new();
            public DateTime FirstDetected { get; set; }
            public DateTime LastDetected { get; set; }
            public AlertSeverity Severity { get; set; }
            public int ConsecutiveCount { get; set; }
            public double LastPulse { get; set; }
            public double LastTemperature { get; set; }
            public double LastSpO2 { get; set; }
            public bool IsFall { get; set; }
            public int? MinSpO2 { get; set; }
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
                var emailService = scope.ServiceProvider.GetService<Application.IServices.IEmailService>();

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
                    string lang = isEn ? "en" : "ro";

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

                    // Email: best-effort, fired separately so a slow SMTP doesn't block other watchers.
                    if (user.NotifyByEmail && !string.IsNullOrWhiteSpace(user.Email) && emailService != null)
                    {
                        string severity = isEn ? "Behavioral anomaly" : "Anomalie comportamentală";
                        try
                        {
                            await emailService.SendAlertNotificationEmailAsync(
                                user.Email,
                                user.FirstName,
                                patientName,
                                severity,
                                message,
                                lang);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to send behavioral email to user {UserId}.", user.Id);
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

        // After an alert episode resolves to Normal, flag the most recent Alert/Critical
        // notification for each watcher so the false-alarm popup appears on next visit.
        private async Task MarkFeedbackRequestedAsync(Guid monitoredId, DateTime episodeStartedAt)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Context.LifeAlertPlusDbContext>();
                var now = DateTime.UtcNow;

                var candidates = await db.Notifications
                    .Where(n => n.IdMonitored == monitoredId
                        && n.IdUser != null
                        && n.CreatedAt >= episodeStartedAt
                        && n.FeedbackRequestedAt == null
                        && n.DeletedAt == null
                        && (n.NotificationType == "Alert" || n.NotificationType == "Critical"))
                    .ToListAsync();

                var latestPerUser = candidates
                    .GroupBy(n => n.IdUser!.Value)
                    .Select(g => g.OrderByDescending(n => n.CreatedAt).First())
                    .ToList();

                foreach (var n in latestPerUser)
                    n.FeedbackRequestedAt = now;

                if (latestPerUser.Count > 0)
                    await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MarkFeedbackRequestedAsync failed for {MonitoredId}", monitoredId);
            }
        }

        public async Task TriggerPanicAlertAsync(Guid monitoredId, string? coordinates = null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.Context.LifeAlertPlusDbContext>();
                var emailService = scope.ServiceProvider.GetRequiredService<Application.IServices.IEmailService>();
                var twilioService = scope.ServiceProvider.GetService<Application.IServices.ITwilioService>();

                var monitored = await dbContext.Monitoreds.FindAsync(monitoredId);
                if (monitored == null) return;

                if (monitored.IsArchived)
                {
                    _logger.LogInformation("Panic alert ignored for archived monitored {MonitoredId}", monitoredId);
                    return;
                }

                var patientName = $"{monitored.FirstName} {monitored.LastName}".Trim();

                HospitalRouteResult? hospital = null;
                if (!string.IsNullOrWhiteSpace(coordinates))
                {
                    var (lat, lon) = ParseCoordinates(coordinates);
                    if (lat != 0 || lon != 0)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                        hospital = await _nearestHospitalService.FindNearestAsync(lat, lon, cts.Token);
                    }
                }

                var userMonitoreds = await dbContext.UserMonitoreds
                    .Where(um => um.IdMonitored == monitoredId)
                    .Include(um => um.User)
                    .ToListAsync();

                var createdAt = DateTime.UtcNow;

                foreach (var um in userMonitoreds)
                {
                    var user = um.User;
                    if (user == null || user.DeletedAt.HasValue) continue;

                    var lang = user.Language ?? "en";
                    var fullMsg = BuildPanicMessage(patientName, lang, hospital, isSms: false, isPush: false);

                    dbContext.Notifications.Add(new Domain.Entities.Notification
                    {
                        Id = Guid.NewGuid(),
                        IdUser = user.Id,
                        IdMonitored = monitoredId,
                        NotificationType = "Critical",
                        Message = fullMsg,
                        CreatedAt = createdAt
                    });

                    if (user.NotifyByPush)
                    {
                        try
                        {
                            var pushMsg = BuildPanicMessage(patientName, lang, hospital, isSms: false, isPush: true);
                            await _pushNotificationService.SendPushNotificationAsync(user.Id, pushMsg, "Critical");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to send panic push notification to user {UserId} for monitored {MonitoredId}", user.Id, monitoredId);
                        }
                    }

                    if (user.NotifyByEmail)
                    {
                        try
                        {
                            await emailService.SendAlertNotificationEmailAsync(
                                user.Email,
                                $"{user.FirstName} {user.LastName}".Trim(),
                                patientName,
                                lang == "ro" ? "CRITIC" : "CRITICAL",
                                fullMsg,
                                lang);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to send panic email to {Email} for monitored {MonitoredId}", user.Email, monitoredId);
                        }
                    }

                    if (user.NotifyBySms)
                    {
                        if (twilioService == null)
                        {
                            _logger.LogWarning("Twilio service unavailable. Cannot send panic SMS for user {UserId} monitoring {MonitoredId}.", user.Id, monitoredId);
                        }
                        else if (string.IsNullOrWhiteSpace(user.PhoneNumber))
                        {
                            _logger.LogWarning("User {UserId} has NotifyBySms enabled but no phone number configured.", user.Id);
                        }
                        else
                        {
                            try
                            {
                                var smsMsg = BuildPanicMessage(patientName, lang, hospital, isSms: true, isPush: false);
                                await twilioService.SendSmsAsync(user.PhoneNumber, smsMsg);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to send panic SMS to {Phone} for monitored {MonitoredId}", user.PhoneNumber, monitoredId);
                            }
                        }
                    }
                }

                await dbContext.SaveChangesAsync();

                _logger.LogWarning("Panic alert dispatched for monitored {MonitoredId} ({Name}) to {Count} watcher(s)", monitoredId, patientName, userMonitoreds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TriggerPanicAlertAsync failed for monitored {MonitoredId}", monitoredId);
            }
        }

        private static string BuildPanicMessage(string patientName, string lang, HospitalRouteResult? hospital, bool isSms, bool isPush)
        {
            bool isRo = !string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase);

            if (isSms)
            {
                string sms = isRo
                    ? $"🆘 PANICĂ: {patientName} nu se simte în siguranță și a cerut ajutor."
                    : $"🆘 PANIC: {patientName} does not feel safe and is asking for help.";
                if (hospital != null)
                    sms += $"\n🏥 {hospital.HospitalName} (~{hospital.EstimatedMinutes} min, {hospital.DistanceKm} km)";
                sms += isRo ? "\nContactați-l ACUM!" : "\nContact them NOW!";
                return sms;
            }

            if (isPush)
            {
                string push = isRo
                    ? $"🆘 {patientName} nu se simte în siguranță"
                    : $"🆘 {patientName} does not feel safe";
                if (hospital != null)
                    push += $"\n🏥 {hospital.HospitalName} – ~{hospital.EstimatedMinutes} min";
                push += isRo ? "\nVerifică acum" : "\nCheck now";
                return push;
            }

            var lines = new List<string>();
            lines.Add(isRo
                ? $"🆘 ALERTĂ DE PANICĂ — {patientName} a apăsat butonul de panică și semnalează că nu se simte în siguranță."
                : $"🆘 PANIC ALERT — {patientName} pressed the panic button and is signaling that they do not feel safe.");
            lines.Add("");
            lines.Add(isRo
                ? "Vârstnicul a cerut ajutor manual. Contactați-l imediat pentru a verifica situația."
                : "The elderly person has manually requested help. Contact them immediately to check on the situation.");

            if (hospital != null)
            {
                lines.Add("");
                lines.Add(isRo ? "🏥 Cel mai apropiat spital de urgență:" : "🏥 Nearest emergency hospital:");
                lines.Add($"  {hospital.HospitalName} (~{hospital.EstimatedMinutes} min, {hospital.DistanceKm} km)");
            }

            lines.Add("");
            lines.Add(isRo
                ? "Dacă nu răspunde sau pare în pericol, contactați serviciile de urgență."
                : "If they do not respond or seem in danger, contact emergency services.");
            return string.Join("\n", lines);
        }

    }
}
