using FluentAssertions;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace LifeAlertPlus.Tests.Unit.API;

public class ConditionRuleEngineTests
{
    // Builds an engine whose condition repository always returns the given keys.
    private static ConditionRuleEngine BuildEngine(params string[] conditionKeys)
    {
        var conditionRepo = new Mock<IMonitoredConditionRepository>();
        conditionRepo
            .Setup(r => r.GetByMonitoredIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync(conditionKeys.Select(k => new MonitoredCondition { ConditionKey = k }).ToList());

        var sp = new Mock<IServiceProvider>();
        sp.Setup(x => x.GetService(typeof(IMonitoredConditionRepository)))
          .Returns(conditionRepo.Object);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(sp.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        return new ConditionRuleEngine(scopeFactory.Object, TestDataFactory.CreateLogger<ConditionRuleEngine>());
    }

    // Builds an engine with no conditions, meaning no escalation ever happens.
    private static ConditionRuleEngine BuildEmptyEngine() => BuildEngine();

    // ── No conditions ────────────────────────────────────────────────────────

    [Fact]
    public async Task Evaluate_ReturnsBaseSeverity_WhenNoConditions()
    {
        var engine = BuildEmptyEngine();
        var (severity, recs, immediate) = await engine.EvaluateAsync(
            Guid.NewGuid(), 75, 37.0, 98, false, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Normal);
        recs.Should().BeEmpty();
        immediate.Should().BeFalse();
    }

    // ── Hypertension ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Hypertension_EscalatesToCritical_WhenPulseOver135()
    {
        var engine = BuildEngine("hypertension");
        var (severity, recs, _) = await engine.EvaluateAsync(
            Guid.NewGuid(), 140, 37.0, 98, false, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Critical);
        recs.Should().ContainMatch("*HTA*");
    }

    [Fact]
    public async Task Hypertension_EscalatesToAlert_WhenPulseOver100()
    {
        var engine = BuildEngine("hypertension");
        var (severity, recs, _) = await engine.EvaluateAsync(
            Guid.NewGuid(), 105, 37.0, 98, false, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Alert);
        recs.Should().ContainMatch("*HTA*");
    }

    [Fact]
    public async Task Hypertension_RemainsNormal_ForNormalVitals()
    {
        var engine = BuildEngine("hypertension");
        var (severity, recs, _) = await engine.EvaluateAsync(
            Guid.NewGuid(), 75, 37.0, 98, false, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Normal);
        recs.Should().BeEmpty();
    }

    // ── Arrhythmia ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Arrhythmia_EscalatesToCritical_WhenPulseOver140()
    {
        var engine = BuildEngine("arrhythmia");
        var (severity, _, _) = await engine.EvaluateAsync(
            Guid.NewGuid(), 145, 37.0, 98, false, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Critical);
    }

    [Fact]
    public async Task Arrhythmia_EscalatesToCritical_WhenPulseBelowThreshold()
    {
        var engine = BuildEngine("arrhythmia");
        var (severity, _, _) = await engine.EvaluateAsync(
            Guid.NewGuid(), 40, 37.0, 98, false, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Critical);
    }

    // ── Heart failure ────────────────────────────────────────────────────────

    [Fact]
    public async Task HeartFailure_EscalatesToCritical_WhenLowSpO2AndHighPulse()
    {
        var engine = BuildEngine("heart_failure");
        var (severity, recs, _) = await engine.EvaluateAsync(
            Guid.NewGuid(), 105, 37.0, 88, false, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Critical);
        recs.Should().ContainMatch("*Insuf. cardiacă*");
    }

    [Fact]
    public async Task HeartFailure_EscalatesToAlert_WhenSpO2Between90And93()
    {
        var engine = BuildEngine("heart_failure");
        var (severity, _, _) = await engine.EvaluateAsync(
            Guid.NewGuid(), 80, 37.0, 91, false, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Alert);
    }

    // ── Parkinson ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Parkinson_EscalatesToCritical_WhenFallDetected()
    {
        var engine = BuildEngine("parkinson");
        var (severity, recs, immediate) = await engine.EvaluateAsync(
            Guid.NewGuid(), 80, 37.0, 98, isFall: true, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Critical);
        immediate.Should().BeTrue();
        recs.Should().ContainMatch("*Parkinson*");
    }

    [Fact]
    public async Task Parkinson_RemainsNormal_WhenNoFall()
    {
        var engine = BuildEngine("parkinson");
        var (severity, _, _) = await engine.EvaluateAsync(
            Guid.NewGuid(), 80, 37.0, 98, isFall: false, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Normal);
    }

    // ── Epilepsy ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Epilepsy_EscalatesToCritical_WhenFallDetected()
    {
        var engine = BuildEngine("epilepsy");
        var (severity, recs, immediate) = await engine.EvaluateAsync(
            Guid.NewGuid(), 80, 37.0, 98, isFall: true, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Critical);
        immediate.Should().BeTrue();
        recs.Should().ContainMatch("*Epilepsie*");
    }

    // ── Asthma ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Asthma_EscalatesToCritical_WhenSpO2Below88()
    {
        var engine = BuildEngine("asthma");
        var (severity, recs, _) = await engine.EvaluateAsync(
            Guid.NewGuid(), 80, 37.0, 85, false, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Critical);
        recs.Should().ContainMatch("*Astm*");
    }

    [Fact]
    public async Task Asthma_EscalatesToAlert_WhenSpO2Between88And93()
    {
        var engine = BuildEngine("asthma");
        var (severity, _, _) = await engine.EvaluateAsync(
            Guid.NewGuid(), 80, 37.0, 90, false, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Alert);
    }

    // ── COPD ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Copd_EscalatesToCritical_WhenSpO2Below86()
    {
        var engine = BuildEngine("copd");
        var (severity, recs, _) = await engine.EvaluateAsync(
            Guid.NewGuid(), 80, 37.0, 84, false, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Critical);
        recs.Should().ContainMatch("*BPOC*");
    }

    // ── Diabetes ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Diabetes_EscalatesToAlert_WhenHighPulseAndLowTemp()
    {
        var engine = BuildEngine("diabetes");
        var (severity, recs, _) = await engine.EvaluateAsync(
            Guid.NewGuid(), 105, 36.0, 98, false, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Alert);
        recs.Should().ContainMatch("*Diabet*");
    }

    // ── Cache invalidation ───────────────────────────────────────────────────

    [Fact]
    public void InvalidateCache_DoesNotThrow_ForUnknownId()
    {
        var engine = BuildEmptyEngine();
        var act = () => engine.InvalidateCache(Guid.NewGuid());
        act.Should().NotThrow();
    }

    // ── Multiple conditions ──────────────────────────────────────────────────

    [Fact]
    public async Task MultipleConditions_ReturnsHighestSeverity()
    {
        // parkinson (critical on fall) + hypertension (alert on pulse 110)
        var engine = BuildEngine("parkinson", "hypertension");
        var (severity, recs, immediate) = await engine.EvaluateAsync(
            Guid.NewGuid(), 110, 37.0, 98, isFall: true, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Critical);
        immediate.Should().BeTrue();
        recs.Should().HaveCountGreaterThan(1); // both conditions produce recommendations
    }

    // ── Repo failure graceful fallback ────────────────────────────────────────

    [Fact]
    public async Task Evaluate_ReturnsBaseSeverity_WhenRepoThrows()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Throws<InvalidOperationException>();

        var engine = new ConditionRuleEngine(
            scopeFactory.Object,
            TestDataFactory.CreateLogger<ConditionRuleEngine>());

        var (severity, recs, _) = await engine.EvaluateAsync(
            Guid.NewGuid(), 75, 37.0, 98, false, AlertSeverity.Alert);

        severity.Should().Be(AlertSeverity.Alert);
        recs.Should().BeEmpty();
    }
}
