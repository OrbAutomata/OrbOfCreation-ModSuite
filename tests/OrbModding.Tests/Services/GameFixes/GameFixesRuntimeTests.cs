using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using Xunit;

namespace OrbModding.Tests.Services.GameFixes;

[Collection(GameFixesCollection.Name)]
public sealed class GameFixesRuntimeTests
{
    private static readonly GameFix Guard = GameFixCatalog.All.OfType<SoundPoolGuard>().Single();
    private static readonly GameFix Merge = GameFixCatalog.All.OfType<MergeRepeatedSounds>().Single();

    [Fact]
    public void TheCatalogListsEveryGameFixAndEveryGameFixSettingIsPublished()
    {
        var declared = typeof(GameFix).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(GameFix)) && !type.IsAbstract)
            .OrderBy(type => type.Name);

        Assert.Equal(declared, GameFixCatalog.All.Select(fix => fix.GetType()).OrderBy(type => type.Name));
        Assert.Equal(
            GameFixCatalog.All.Select(fix => fix.Setting.Key),
            GameFixesSettings.Definition.Settings.Skip(1).Select(setting => setting.Key));
    }

    [Fact]
    public void ByDefaultTheGuardIsOnAndTheMergeIsOff()
    {
        using var harness = new Harness(Running());

        Assert.True(SoundPoolGuard.IsActive);
        Assert.False(MergeRepeatedSounds.IsActive);
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
    }

    [Fact]
    public void EachFixFollowsItsSettingTheFeatureModeAutomationAndTheEmergencyStop()
    {
        var configuration = Running(new GameFixesConfiguration { MergeRepeatedSounds = true });
        using var harness = new Harness(configuration);
        Assert.True(SoundPoolGuard.IsActive);
        Assert.True(MergeRepeatedSounds.IsActive);

        harness.Publish(configuration with { GameFixes = configuration.GameFixes with { SoundPoolGuard = false } });
        Assert.False(SoundPoolGuard.IsActive);
        Assert.True(MergeRepeatedSounds.IsActive);

        harness.Publish(configuration with { GameFixes = configuration.GameFixes with { Mode = GameFixesOperationMode.Disabled } });
        Assert.False(SoundPoolGuard.IsActive);
        Assert.False(MergeRepeatedSounds.IsActive);

        harness.Publish(configuration with { General = configuration.General with { Enabled = false } });
        Assert.False(SoundPoolGuard.IsActive);

        harness.Publish(configuration);
        Assert.True(SoundPoolGuard.IsActive);
        harness.Publish(configuration with { Safety = configuration.Safety with { EmergencyDisable = true } });
        Assert.False(SoundPoolGuard.IsActive);
        Assert.False(MergeRepeatedSounds.IsActive);
        harness.Publish(configuration);
        Assert.True(SoundPoolGuard.IsActive);

        harness.Pump.SetEmergencyStop(true, EmergencyStopReason.SafetyInterlock);
        harness.Frame();
        Assert.False(SoundPoolGuard.IsActive);
        Assert.False(MergeRepeatedSounds.IsActive);
        harness.Pump.SetEmergencyStop(false, EmergencyStopReason.SafetyInterlock);
        harness.Frame();
        Assert.True(SoundPoolGuard.IsActive);
        Assert.True(MergeRepeatedSounds.IsActive);

        harness.Dispose();
        Assert.False(SoundPoolGuard.IsActive);
        Assert.False(MergeRepeatedSounds.IsActive);
    }

    [Fact]
    public void ASelectedFixThatIsNotInstalledStaysOffAndTheFeatureSaysWhy()
    {
        var failures = new Dictionary<GameFix, string> { [Guard] = "SoundManager:GetAudioElement is unavailable." };
        using var harness = new Harness(Running(), failures);

        Assert.False(SoundPoolGuard.IsActive);
        Assert.Equal(FeatureStatusState.ContractUnavailable, harness.Status.Current.State);
        Assert.Equal(
            "Sound pool guard is not installed: SoundManager:GetAudioElement is unavailable.",
            harness.Status.Current.Reason.Summary);

        harness.Publish(Running(new GameFixesConfiguration { SoundPoolGuard = false, MergeRepeatedSounds = true }));
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
        Assert.True(MergeRepeatedSounds.IsActive);
    }

    [Fact]
    public void WhatTheFixesDidReachesTheFeatureDetailWithinAPollWindow()
    {
        using var harness = new Harness(Running());
        var manager = new SoundManager { audioElement = new AudioElement() };
        var fired = Guard.Fired;
        AudioElement? missing = null;
        SoundPoolGuard.GuardHandOut(manager, ref missing);

        for (var frame = 1; frame < GameFixesRuntime.ActivityPollFrames; frame++) harness.Frame();
        Assert.DoesNotContain(Guard.DescribeFired(fired + 1), harness.Status.Current.Detail);
        harness.Frame();

        Assert.StartsWith(Guard.DescribeFired(fired + 1), harness.Status.Current.Detail);
    }

    private static SuiteRuntimeConfiguration Running(GameFixesConfiguration? fixes = null) => new()
    {
        General = new SuiteGeneralConfiguration { Enabled = true },
        GameFixes = fixes ?? new GameFixesConfiguration(),
    };

    private sealed class Harness : IDisposable
    {
        private readonly AutomataFeatureStatuses _statuses;
        private readonly ServiceCycleRegistry _registry;
        private readonly LifecycleGameActions _actions = new();
        private readonly IAutomataServiceCycleFeatureRuntime _runtime;
        private long _frame;
        private bool _disposed;

        internal Harness(SuiteRuntimeConfiguration configuration, IReadOnlyDictionary<GameFix, string>? failures = null)
        {
            _statuses = new AutomataFeatureStatuses(configuration, 1, new FeatureStatusRegistry());
            _registry = new ServiceCycleRegistry(1, configuration, new ConfigGeneration(1), new LifecycleGeneration(1));
            var feature = new GameFixesServiceCycleFeature(GameFixCatalog.All,
                failures ?? new Dictionary<GameFix, string>(), _statuses.Get(GameFixesFeatureContribution.FeatureId));
            var context = new AutomataServiceCycleFeatureContext(_registry, new ConfigGeneration(1), 1, _actions);
            _runtime = feature.Register(in context);
            _registry.Seal();
            Pump = new SuiteFramePump(_registry);
            _runtime.ActivateDiagnostics();
        }

        internal SuiteFramePump Pump { get; }

        internal AutomataFeatureStatusReporter Status => _statuses.Get(GameFixesFeatureContribution.FeatureId);

        internal void Publish(SuiteRuntimeConfiguration configuration)
        {
            var generation = _registry.Configuration.Publish(configuration);
            _statuses.ObserveConfiguration(configuration, generation);
            _runtime.ObserveConfiguration(generation);
        }

        internal void Frame()
        {
            var report = Pump.PumpFrame(++_frame);
            _runtime.ObserveFrame(Pump, in report);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _runtime.DisposeDiagnostics();
            _runtime.DisposeRegistration();
            Pump.Dispose();
            _actions.Dispose();
            _registry.Dispose();
            _statuses.Dispose();
        }
    }
}
