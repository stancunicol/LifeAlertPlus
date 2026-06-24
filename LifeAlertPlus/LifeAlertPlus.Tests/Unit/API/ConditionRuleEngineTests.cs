using FluentAssertions;
using LifeAlertPlus.API.Services;
using LifeAlertPlus.Domain.Entities;
using LifeAlertPlus.Domain.IRepositories;
using LifeAlertPlus.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace LifeAlertPlus.Tests.Unit.API;

// Teste pentru ConditionRuleEngine — motorul de reguli care escaladează severitatea unei alerte
// (Normal/Alert/Critical) în funcție de afecțiunile diagnosticate ale pacientului (hipertensiune, aritmie,
// insuficiență cardiacă, Parkinson, epilepsie, astm, BPOC, diabet). Fiecare boală are propriile praguri
// HR/Temp/SpO2/cădere, suplimentare față de pragurile generale din ConditionThresholdAdjuster.
public class ConditionRuleEngineTests
{
    // Construiește un engine al cărui repository de afecțiuni returnează mereu cheile date (mock, fără DB reală)
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

    // Construiește un engine fără afecțiuni — niciodată nu se escaladează severitatea (caz de bază, pacient fără diagnostic)
    private static ConditionRuleEngine BuildEmptyEngine() => BuildEngine();

    // ── Fără afecțiuni ────────────────────────────────────────────────────────

    // Fără afecțiuni diagnosticate, severitatea de bază (calculată anterior din praguri generale) nu se modifică
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

    // ── Hipertensiune ─────────────────────────────────────────────────────────

    // Puls > 135 bpm la un pacient hipertensiv → risc cardiovascular sever, escaladare directă la Critical
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

    // ── Aritmie ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Arrhythmia_EscalatesToCritical_WhenPulseOver140()
    {
        var engine = BuildEngine("arrhythmia");
        var (severity, _, _) = await engine.EvaluateAsync(
            Guid.NewGuid(), 145, 37.0, 98, false, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Critical);
    }

    // Pulsul anormal de scăzut e la fel de periculos ca cel ridicat pentru un pacient cu aritmie — ambele praguri escaladează la Critical
    [Fact]
    public async Task Arrhythmia_EscalatesToCritical_WhenPulseBelowThreshold()
    {
        var engine = BuildEngine("arrhythmia");
        var (severity, _, _) = await engine.EvaluateAsync(
            Guid.NewGuid(), 40, 37.0, 98, false, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Critical);
    }

    // ── Insuficiență cardiacă ────────────────────────────────────────────────

    // Combinația SpO2 scăzut + puls ridicat e tipică pentru decompensare cardiacă — escaladare la Critical
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

    // O cădere la un pacient cu Parkinson e mereu critică și necesită notificare imediată (immediate=true),
    // nu doar escaladarea severității — risc ridicat de fractură/leziune la pacienții cu instabilitate posturală
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

    // ── Epilepsie ─────────────────────────────────────────────────────────────

    // O cădere la un pacient epileptic poate indica o criză convulsivă în desfășurare → Critical + notificare imediată
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

    // ── Astm ───────────────────────────────────────────────────────────────

    // Prag SpO2 mai permisiv decât BPOC (vezi mai jos) — astmul nu reduce cronic oxigenarea bazală la fel de mult
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

    // ── BPOC ─────────────────────────────────────────────────────────────────

    // Prag SpO2 mai jos decât la astm (86 vs 88) — pacienții BPOC au de obicei o saturație bazală cronic mai joasă,
    // deci pragul de alarmă trebuie ajustat ca să nu genereze alerte false constante
    [Fact]
    public async Task Copd_EscalatesToCritical_WhenSpO2Below86()
    {
        var engine = BuildEngine("copd");
        var (severity, recs, _) = await engine.EvaluateAsync(
            Guid.NewGuid(), 80, 37.0, 84, false, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Critical);
        recs.Should().ContainMatch("*BPOC*");
    }

    // ── Diabet ─────────────────────────────────────────────────────────────

    // Puls ridicat + temperatură scăzută poate indica hipoglicemie (răspuns simpatic + extremități reci) — escaladare la Alert
    [Fact]
    public async Task Diabetes_EscalatesToAlert_WhenHighPulseAndLowTemp()
    {
        var engine = BuildEngine("diabetes");
        var (severity, recs, _) = await engine.EvaluateAsync(
            Guid.NewGuid(), 105, 36.0, 98, false, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Alert);
        recs.Should().ContainMatch("*Diabet*");
    }

    // ── Invalidare cache ───────────────────────────────────────────────────

    // ConditionRuleEngine cache-uiește afecțiunile per pacient — invalidarea unui ID necunoscut nu trebuie să crape
    [Fact]
    public void InvalidateCache_DoesNotThrow_ForUnknownId()
    {
        var engine = BuildEmptyEngine();
        var act = () => engine.InvalidateCache(Guid.NewGuid());
        act.Should().NotThrow();
    }

    // ── Afecțiuni multiple ──────────────────────────────────────────────────

    // Cu mai multe afecțiuni, severitatea finală e maximul rezultat din toate regulile aplicabile, nu doar prima
    [Fact]
    public async Task MultipleConditions_ReturnsHighestSeverity()
    {
        // parkinson (critical la cădere) + hipertensiune (alert la puls 110)
        var engine = BuildEngine("parkinson", "hypertension");
        var (severity, recs, immediate) = await engine.EvaluateAsync(
            Guid.NewGuid(), 110, 37.0, 98, isFall: true, AlertSeverity.Normal);

        severity.Should().Be(AlertSeverity.Critical);
        immediate.Should().BeTrue();
        recs.Should().HaveCountGreaterThan(1); // ambele afecțiuni generează propriile recomandări
    }

    // ── Fallback grațios la eroarea repository-ului ────────────────────────────────────────

    // Dacă citirea afecțiunilor din DB eșuează (ex: conexiune picată), motorul nu trebuie să blocheze alertarea —
    // se păstrează severitatea de bază deja calculată, fără recomandări suplimentare specifice afecțiunilor
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
