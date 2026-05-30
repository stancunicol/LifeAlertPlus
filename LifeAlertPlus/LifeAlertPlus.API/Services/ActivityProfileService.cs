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

        // Last 15 minutes of activity readings per person, kept in a synchronized
        // buffer so concurrent CheckAnomalyAsync calls for the same patient (e.g. a burst
        // of ESP packets) don't corrupt the underlying Queue<T>.
        private sealed class ActivityBuffer
        {
            public readonly object Sync = new();
            public Queue<(DateTime Timestamp, bool IsMoving)> Readings = new();
        }
        private readonly ConcurrentDictionary<Guid, ActivityBuffer> _activityBuffers = new();
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

            var buf = _activityBuffers.GetOrAdd(monitoredId, _ => new ActivityBuffer());
            bool[] recentReadings;
            lock (buf.Sync)
            {
                buf.Readings.Enqueue((now, moving));
                while (buf.Readings.Count > 0 && (now - buf.Readings.Peek().Timestamp) > ActivityWindow)
                    buf.Readings.Dequeue();
                // Snapshot under the lock so the rest of the method can read freely.
                recentReadings = buf.Readings.Select(x => x.IsMoving).ToArray();
            }

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

        // Sliding-window length used by BuildProfileAsync. The profile is recomputed
        // daily so that each new build "rolls forward" by one day.
        public static readonly TimeSpan ProfileWindow = TimeSpan.FromDays(7);

        // Builds or rebuilds the hourly profile from the last 7 days of measurements
        public async Task BuildProfileAsync(Guid monitoredId)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
                var repo = scope.ServiceProvider.GetRequiredService<IActivityProfileRepository>();

                var monitored = await db.Monitoreds
                    .Where(m => m.Id == monitoredId)
                    .Select(m => new { m.IsArchived })
                    .FirstOrDefaultAsync();
                if (monitored == null || monitored.IsArchived)
                {
                    _logger.LogDebug("BuildProfile skipped for {MonitoredId} — archived or missing", monitoredId);
                    return;
                }

                var cutoff = DateTime.UtcNow - ProfileWindow;
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

        // Rolls the 7-day profile window forward by rebuilding every non-archived
        // monitored person. Intended for the daily background job.
        public async Task<int> RebuildAllActiveAsync(CancellationToken ct = default)
        {
            int rebuilt = 0;
            try
            {
                List<Guid> ids;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<LifeAlertPlusDbContext>();
                    ids = await db.Monitoreds
                        .Where(m => !m.IsArchived && m.DeletedAt == null)
                        .Select(m => m.Id)
                        .ToListAsync(ct);
                }

                foreach (var id in ids)
                {
                    if (ct.IsCancellationRequested) break;
                    if (!_buildInProgress.TryAdd(id, 0)) continue; // skip if already building
                    await BuildProfileAsync(id);
                    rebuilt++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RebuildAllActiveAsync failed");
            }
            return rebuilt;
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
