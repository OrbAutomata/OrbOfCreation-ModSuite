#if SERVICE_CYCLE_PROFILE
using OrbAutomata.GameMcp;
#endif
using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime;
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataServiceCycleActivationTests
{
    [Fact]
    public void WaitsForReadinessThenForwardsTheFirstTickAndLifecycle()
    {
        var ready = false;
        var capturedGeneration = default(ConfigGeneration);
        var attempts = 0;
        var runtime = new RecordingRuntime();
        var snapshot = Configuration();
        var activation = new AutomataServiceCycleActivation(
            () => ready,
            (_, generation) =>
            {
                attempts++;
                capturedGeneration = generation;
                return runtime;
            },
            snapshot,
            new ConfigGeneration(1));

        activation.Tick(0.125f);
        activation.PublishSavedConfiguration(snapshot, new ConfigGeneration(1));
        activation.InvalidateLifecycle();
        activation.CancelPreparedWork();

        Assert.Equal(0, attempts);
        Assert.Null(activation.ReadStrategistBulletin());
        Assert.Equal("Waiting for the automation runtime to start.", activation.UnavailableReason);
        Assert.Equal(0, runtime.BulletinReads);
        Assert.Empty(runtime.TickDurations);
        Assert.Equal(0, runtime.Publications);
        Assert.Equal(0, runtime.Invalidations);
        Assert.Equal(0, runtime.Cancellations);

        ready = true;
        activation.Tick(0.25f);
        activation.Tick(0.5f);
        activation.PublishSavedConfiguration(snapshot, new ConfigGeneration(2));
        activation.InvalidateLifecycle();
        activation.CancelPreparedWork();

        Assert.Equal(1, attempts);
        Assert.Equal(new ConfigGeneration(1), capturedGeneration);
        Assert.Equal(new[] { 0.25f, 0.5f }, runtime.TickDurations);
        Assert.Equal(1, runtime.Publications);
        Assert.Equal(1, runtime.Invalidations);
        Assert.Equal(1, runtime.Cancellations);
        Assert.Null(activation.ReadStrategistBulletin());
        Assert.Equal(string.Empty, activation.UnavailableReason);
        Assert.Equal(1, runtime.BulletinReads);

        activation.Dispose();
        activation.Tick(1f);
        activation.PublishSavedConfiguration(snapshot, new ConfigGeneration(3));

        Assert.Equal(1, runtime.Disposals);
        Assert.Null(activation.ReadStrategistBulletin());
        Assert.Equal("The automation runtime has shut down.", activation.UnavailableReason);
        Assert.Equal(1, runtime.BulletinReads);
        Assert.Equal(2, runtime.TickDurations.Count);
        Assert.Equal(1, runtime.Publications);
    }

    [Fact]
    public void ConfigurationPublishedBeforeActivationIsUsedForConstructionWithoutReplay()
    {
        var ready = false;
        var runtime = new RecordingRuntime();
        var initial = Configuration();
        var latest = Configuration();
        SuiteRuntimeConfiguration? constructedFrom = null;
        var constructedGeneration = default(ConfigGeneration);
        var activation = new AutomataServiceCycleActivation(
            () => ready,
            (configuration, generation) =>
            {
                constructedFrom = configuration;
                constructedGeneration = generation;
                return runtime;
            },
            initial,
            new ConfigGeneration(1));

        activation.PublishSavedConfiguration(latest, new ConfigGeneration(2));
        ready = true;
        activation.Tick(0.25f);

        Assert.Same(latest, constructedFrom);
        Assert.Equal(new ConfigGeneration(2), constructedGeneration);
        Assert.Equal(0, runtime.Publications);
    }

    [Fact]
    public void FailedActivationKeepsPublishingLatestConfigurationAfterBoundedRetry()
    {
        var attempts = 0;
        var observations = 0;
        var activation = new AutomataServiceCycleActivation(
            () => true,
            (_, _) =>
            {
                attempts++;
                return null;
            },
            Configuration(),
            new ConfigGeneration(1),
            observeHostUnavailable: (_, _) => observations++);

        activation.Tick(0.25f);
        activation.PublishSavedConfiguration(Configuration(), new ConfigGeneration(2));
        activation.Tick(0.5f);
        activation.PublishSavedConfiguration(Configuration(), new ConfigGeneration(3));
        activation.Tick(0.75f);

        Assert.Equal(2, attempts);
        Assert.Equal(4, observations);
        Assert.Null(activation.ReadStrategistBulletin());
        Assert.Equal("The automation runtime could not start.", activation.UnavailableReason);
    }

    [Fact]
    public void RecoverableActivationFailureRetriesOnceAndReplaysLatestConfiguration()
    {
        var attempts = 0;
        var unavailable = 0;
        var constructedGeneration = default(ConfigGeneration);
        var runtime = new RecordingRuntime();
        var activation = new AutomataServiceCycleActivation(
            () => true,
            (_, generation) =>
            {
                constructedGeneration = generation;
                return ++attempts == 1 ? null : runtime;
            },
            Configuration(),
            new ConfigGeneration(1),
            observeHostUnavailable: (_, _) => unavailable++);

        activation.Tick(0.25f);
        activation.PublishSavedConfiguration(Configuration(), new ConfigGeneration(2));
        activation.Tick(0.5f);
        activation.Tick(0.75f);

        Assert.Equal(2, attempts);
        Assert.Equal(2, unavailable);
        Assert.Equal(new ConfigGeneration(2), constructedGeneration);
        Assert.Equal(0, runtime.Publications);
        Assert.Equal(new[] { 0.5f, 0.75f }, runtime.TickDurations);
    }

    [Fact]
    public void DisposalBeforeReadinessPreventsActivation()
    {
        var attempts = 0;
        var ready = false;
        var activation = new AutomataServiceCycleActivation(
            () => ready,
            (_, _) =>
            {
                attempts++;
                return new RecordingRuntime();
            },
            Configuration(),
            new ConfigGeneration(1));

        activation.Dispose();
        ready = true;
        activation.Tick(0.25f);

        Assert.Equal(0, attempts);
    }

    private sealed class RecordingRuntime : IAutomataServiceCycleRuntime
    {
        public List<float> TickDurations { get; } = new();
        public int Publications { get; private set; }
        public int Invalidations { get; private set; }
        public int Cancellations { get; private set; }
        public int Disposals { get; private set; }

        public void Tick(float unscaledDeltaTime) => TickDurations.Add(unscaledDeltaTime);
        public void PublishSavedConfiguration(
            SuiteRuntimeConfiguration configuration,
            ConfigGeneration configurationGeneration) =>
            Publications++;
        public void InvalidateLifecycle() => Invalidations++;
        public void CancelPreparedWork() => Cancellations++;
        public int BulletinReads { get; private set; }
        public string UnavailableReason => string.Empty;
        public OrbModding.Common.Runtime.Strategy.StrategistBulletin? ReadStrategistBulletin()
        {
            BulletinReads++;
            return null;
        }
        public AutomataDiagnosticsRuntimeEvidence CaptureDiagnostics() =>
            AutomataDiagnosticsRuntimeEvidence.Unavailable("test runtime");
#if SERVICE_CYCLE_PROFILE
        public OrbAutomata.AutomataRuntimeFrameFacts CaptureFrameFacts(bool includeServices) =>
            throw new InvalidOperationException("This activation fixture never reads frame facts.");
        public OrbAutomata.GameMcp.GameMcpCommandDispatcher GameMcp =>
            throw new InvalidOperationException("This activation fixture never dispatches MCP commands.");
#endif
        public void Dispose() => Disposals++;
    }

    private static SuiteRuntimeConfiguration Configuration() => AutoHarvestConfigurationFactory.Create(
        false,
        false,
        false,
        true,
        true);
}
