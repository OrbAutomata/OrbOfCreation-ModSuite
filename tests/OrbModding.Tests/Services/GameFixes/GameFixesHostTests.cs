using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.GameFixes;

[Collection(GameFixesCollection.Name)]
public sealed class GameFixesHostTests
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
    public void EachFixFollowsOnlyItsSettingAndTheFeatureMode()
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

        harness.Publish(configuration);
        Assert.True(SoundPoolGuard.IsActive);
        Assert.True(MergeRepeatedSounds.IsActive);

        harness.Dispose();
        Assert.False(SoundPoolGuard.IsActive);
        Assert.False(MergeRepeatedSounds.IsActive);
    }

    /// <summary>
    /// The suite composed with <b>Automation enabled</b> off from launch, as the plugin composes it:
    /// the catalog's standing features, the configuration store publishing to them and the statuses,
    /// and STOP ALL pressed on the way. No automation runtime is composed or ticked.
    /// </summary>
    [Fact]
    public void AutomationOffFromLaunchAndStopAllLeaveTheFixesInstalledAndReportedRunning()
    {
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());
        configuration.Enabled.Value = false;
        AutomataFeatureStatuses? statuses = null;
        AutomataStandingFeatures? standing = null;
        var store = new AutomataConfigurationStore(configuration, (published, generation) =>
        {
            statuses!.ObserveConfiguration(published, generation);
            standing?.ObserveConfiguration(published, generation);
        });
        using (statuses = new AutomataFeatureStatuses(store.Current, 1, new FeatureStatusRegistry(), store.CurrentGeneration))
        using (standing = AutomataStandingFeatures.Compose(new AutomataStandingComposition
        {
            Harmony = new Harmony("game-fixes-launch-test"),
            Statuses = statuses,
            Configuration = store.Current,
            ConfigurationGeneration = store.CurrentGeneration,
            Warn = _ => { },
        }))
        {
            var status = statuses.Get(GameFixesFeatureContribution.FeatureId);
            Assert.False(store.Current.General.Enabled);
            Assert.True(SoundPoolGuard.IsActive);
            standing.Tick(false, 1);
            Assert.Equal(FeatureStatusState.NotReady, status.Current.State);

            standing.Tick(true, 1);
            Assert.Equal(FeatureStatusState.Operational, status.Current.State);

            new EmergencyStopControl(store, _ => { }).Activate();
            standing.Tick(true, 1);
            Assert.True(store.Current.Safety.EmergencyDisable);
            Assert.True(SoundPoolGuard.IsActive);
            Assert.Equal(FeatureStatusState.Operational, status.Current.State);

            statuses.ObserveServiceCycleUnavailable(store.Current, store.CurrentGeneration);
            Assert.Equal(FeatureStatusState.Operational, status.Current.State);
        }
        Assert.False(SoundPoolGuard.IsActive);
    }

    [Fact]
    public void AfterALifecycleChangeTheFixesAreReportedRunningOnceGameplayIsReadyAgain()
    {
        using var harness = new Harness(Running());

        harness.LifecycleNotReady(2);
        Assert.Equal(FeatureStatusState.NotReady, harness.Status.Current.State);
        Assert.True(SoundPoolGuard.IsActive);

        harness.Frame(2);
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
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

        for (var frame = 1; frame < GameFixesHost.ActivityPollFrames; frame++) harness.Frame();
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
        private readonly GameFixesHost _host;
        private SuiteRuntimeConfiguration _configuration;
        private ConfigGeneration _generation = new(1);
        private bool _disposed;

        internal Harness(SuiteRuntimeConfiguration configuration, IReadOnlyDictionary<GameFix, string>? failures = null)
        {
            _configuration = configuration;
            _statuses = new AutomataFeatureStatuses(configuration, 1, new FeatureStatusRegistry());
            _host = new GameFixesHost(GameFixCatalog.All, failures ?? new Dictionary<GameFix, string>(),
                _statuses.Get(GameFixesFeatureContribution.FeatureId), configuration, _generation);
            _host.Tick(true, 1);
        }

        internal AutomataFeatureStatusReporter Status => _statuses.Get(GameFixesFeatureContribution.FeatureId);

        internal void Publish(SuiteRuntimeConfiguration configuration)
        {
            _configuration = configuration;
            _generation = _generation.Next();
            _statuses.ObserveConfiguration(configuration, _generation);
            _host.ObserveConfiguration(configuration, _generation);
        }

        internal void LifecycleNotReady(long lifecycle)
        {
            _statuses.ObserveLifecycleNotReady(_configuration, lifecycle, _generation);
            _host.Tick(false, lifecycle);
        }

        internal void Frame(long lifecycle = 1) => _host.Tick(true, lifecycle);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _host.Dispose();
            _statuses.Dispose();
        }
    }
}
