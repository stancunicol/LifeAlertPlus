using System.Collections.Concurrent;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LifeAlertPlus.API.Services
{
    public class ActivityProfileService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ActivityProfileService> _logger;

        // Last 15 minutes of activity readings per person: (timestamp, isMoving)
        private readonly ConcurrentDictionary<Guid, Queue<(DateTime Timestamp, bool IsMoving)>> _activityBuffers = new();
        private static readonly TimeSpan ActivityWindow = TimeSpan.FromMinutes(15);

        // Profile cache: avoid hitting DB on every measurement
        private readonly ConcurrentDictionary<Guid, (DateTime CachedAt, ActivityProfile?[] Hours)> _profileCache = new();
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(4);

        // Prevents firing multiple concurrent builds for the same person
        private readonly ConcurrentDictionary<Guid, byte> _buildInProgress = new();

        // Anomaly cooldown: don't repeat a behavioral notification within 30 minutes
        private readonly ConcurrentDictionary<Guid, DateTime> _anomalyCooldowns = new();
        private static readonly TimeSpan AnomalyCooldown = TimeSpan.FromMinutes(30);

        private const int MinDataPoints = 20;
        private const int MinInactiveReadings = 8;      // consecutive sedentary readings to trigger
        private const double ActiveHourThreshold = 0.65; // usually moving >65% of the time
        private const double SleepHourThreshold = 0.70;  // usually asleep >70% of the time

        private static readonly HashSet<string> SedentaryLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            "lying", "sleeping", "resting", "sedentary", "stationary", "sitting", "idle"
        };

        public ActivityProfileService(IServiceScopeFactory scopeFactory, ILogger<ActivityProfileService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        private static bool IsMoving(string activity) =>
            !string.IsNullOrWhiteSpace(activity) && !SedentaryLabels.Contains(activity.Trim());

        // Returns (hasAnomaly, messageRo, messageEn, anomalyType) — called on every new measurement
        public async Task<(bool HasAnomaly, string? MessageRo, string? MessageEn, string? AnomalyType)> CheckAnomalyAsync(
            Guid monitoredId, string activity, double pulse, DateTime now)
        {
            bool moving = IsMoving(activity);

            var buf = _activityBuffers.GetOrAdd(monitoredId, _ => new Queue<(DateTime, bool)>());
            buf.Enqueue((now, moving));
            while (buf.Count > 0 && (now - buf.Peek().Timestamp) > ActivityWindow)
                buf.Dequeue();

            if (_anomalyCooldowns.TryGetValue(monitoredId, out var lastAnomaly) &&
                (now - lastAnomaly) < AnomalyCooldown)
                return (false, null, null, null);

            var profile = await GetCachedProfileAsync(monitoredId, now);
            if (profile == null) return (false, null, null, null);

            var hourProfile = profile[now.Hour];
            if (hourProfile == null || hourProfile.DataPoints < MinDataPoints)
                return (false, null, null, null);

            // Person usually active at this hour but hasn't moved in 15 minutes
            if (hourProfile.MovementRate > ActiveHourThreshold)
            {
                var recentReadings = buf.Select(x => x.IsMoving).ToArray();
                if (recentReadings.Length >= MinInactiveReadings && recentReadings.All(m => !m))
                {
                    _anomalyCooldowns[monitoredId] = now;
                    int minutes = (int)ActivityWindow.TotalMinutes;
                    return (true,
                        $"Persoana nu s-a mișcat de {minutes} minute, deși la ora {now.Hour:00}:00 este de obicei activă ({hourProfile.MovementRate:P0} din timp).",
                        $"No movement detected for {minutes} minutes, even though the person is usually active at {now.Hour:00}:00 ({hourProfile.MovementRate:P0} of the time).",
                        "InactivityAnomaly");
                }
            }

            // Person usually asleep at this hour but is now moving
            if (hourProfile.SleepProbability > SleepHourThreshold && moving)
            {
                _anomalyCooldowns[monitoredId] = now;
                return (true,
                    $"Activitate detectată la ora {now.Hour:00}:00, oră la care persoana doarme de obicei ({hourProfile.SleepProbability:P0} din timp).",
                    $"Activity detected at {now.Hour:00}:00, an hour when the person is usually asleep ({hourProfile.SleepProbability:P0} of the time).",
                    "NightActivity");
            }

            return (false, null, null, null);
        }

        // Builds or rebuilds the hourly profile from the last 14 days of measurements
        public async Task BuildProfileAsync(Guid monitoredId)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
                var repo = scope.ServiceProvider.GetRequiredService<IActivityProfileRepository>();

                var cutoff = DateTime.UtcNow.AddDays(-14);
                var measurements = await db.Measurements
                    .Where(m => m.IdMonitored == monitoredId && m.CreatedAt >= cutoff)
                    .ToListAsync();

                if (measurements.Count == 0)
                {
                    _logger.LogInformation("No measurements found for activity profile build of {MonitoredId}.", monitoredId);
                    return;
                }

                var now = DateTime.UtcNow;
                var byHour = measurements.GroupBy(m => m.CreatedAt.Hour);

                foreach (var group in byHour)
                {
                    var list = group.ToList();
                    double movementRate = list.Count(m => IsMoving(m.Activity)) / (double)list.Count;
                    double sleepProbability = list.Count(m =>
                        !IsMoving(m.Activity) && m.Pulse > 0 && m.Pulse < 75) / (double)list.Count;
                    double avgPulse = list.Where(m => m.Pulse > 0).Select(m => m.Pulse).DefaultIfEmpty(0).Average();

                    await repo.UpsertAsync(new ActivityProfile
                    {
                        IdMonitored = monitoredId,
                        HourOfDay = group.Key,
                        AveragePulse = Math.Round(avgPulse, 1),
                        MovementRate = Math.Round(movementRate, 3),
                        SleepProbability = Math.Round(sleepProbability, 3),
                        DataPoints = list.Count,
                        LastUpdated = now
                    });
                }

                _profileCache.TryRemove(monitoredId, out _);
                _logger.LogInformation("Activity profile built for {MonitoredId}: {Hours} hours from {Count} measurements.",
                    monitoredId, byHour.Count(), measurements.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build activity profile for {MonitoredId}.", monitoredId);
            }
            finally
            {
                _buildInProgress.TryRemove(monitoredId, out _);
            }
        }

        public async Task<List<ActivityProfile>> GetProfileAsync(Guid monitoredId)
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IActivityProfileRepository>();
            return (await repo.GetByMonitoredIdAsync(monitoredId)).ToList();
        }

        private async Task<ActivityProfile?[]?> GetCachedProfileAsync(Guid monitoredId, DateTime now)
        {
            if (_profileCache.TryGetValue(monitoredId, out var cached) &&
                (now - cached.CachedAt) < CacheDuration)
                return cached.Hours;

            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IActivityProfileRepository>();
            var profiles = (await repo.GetByMonitoredIdAsync(monitoredId)).ToList();

            if (profiles.Count == 0)
            {
                // No profile yet — trigger a build if not already in progress
                if (_buildInProgress.TryAdd(monitoredId, 0))
                    _ = Task.Run(() => BuildProfileAsync(monitoredId));
                return null;
            }

            var arr = new ActivityProfile?[24];
            foreach (var p in profiles)
                arr[p.HourOfDay] = p;

            _profileCache[monitoredId] = (now, arr);
            return arr;
        }
    }
}
