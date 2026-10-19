using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.TestSupport;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using OrbModding.Tests.Runtime.World;
using Xunit;
using static OrbModding.Tests.Services.AutoHarvest.Runtime.ServiceCycle.AutoHarvestTestWorlds;

namespace OrbModding.Tests.Services.AutoHarvest.Runtime.ServiceCycle;

/// <summary>
/// A harvest that did not start backs that pair off on the retry gate, waiting a second and doubling
/// to ten, while the other pair keeps harvesting and the status names the pair and why. A commit or
/// a pair that stops being a candidate ends the episode. A contract mismatch the game proved holds
/// the pair for the lifecycle; a read that only threw is retried. The test clock moves only when a
/// test means to bring a wait due.
/// </summary>
[Collection(GameActionFaultLogCollection.Name)]
public sealed class AutoHarvestRetryRuntimeTests : IDisposable
{
    private const string FailedToCollect = "Auto Harvest failed to collect ";
    private static readonly MonotonicDuration PairFirstWait = MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1));
    private static readonly MonotonicDuration PastTheServiceWait = MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(300));
    private static readonly MonotonicDuration PastThePairWait = MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(1_100));
    private static readonly MonotonicDuration PastEveryWait = MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(30));
    private readonly List<string> _faultLog = new();

    public AutoHarvestRetryRuntimeTests() => GameActionFaultLog.ConfigureLog(_faultLog.Add);

    public void Dispose() => GameActionFaultLog.ConfigureLog(null);

    [Fact]
    public void AHarvestThatDidNotStartBacksThatPairOffWhileTheOtherKeepsHarvesting()
    {
        using var game = AutoHarvestTestGame.Load();
        game.Queue.SuppressMutationOf = game.Action(AutoHarvestPair.FruitTree);
        using var harness = new Harness(treasure: true);

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        var faulted = harness.RetryGate.Latest;
        harness.Advance(PastTheServiceWait);
        harness.PumpWorldsUntil(() => game.IsRunning(AutoHarvestPair.TreasureTree));
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(AutoHarvestServicePolicies.RetryTarget(AutoHarvestPair.FruitTree), faulted.Target);
        Assert.Equal(PairFirstWait, faulted.Backoff);
        Assert.True(harness.RetryGate.TryFind(
            AutoHarvestServicePolicies.RetryTarget(AutoHarvestPair.FruitTree), out var waiting));
        Assert.Equal(1, waiting.ConsecutiveFailures);
        Assert.False(game.IsRunning(AutoHarvestPair.FruitTree));
        var status = harness.Status.Current;
        Assert.Equal(FeatureStatusState.Degraded, status.State);
        Assert.Equal(FeatureStatusReasonCode.NativeMutationFailed, status.Reason.Code);
        Assert.Contains(
            "is backing off collecting " + EntityIdentityFormatter.Format(new Guid(AutoHarvestKnownIds.FruitTreePlot)) +
            " after 1 failed attempt(s)",
            status.Reason.Summary);
        Assert.Contains("asked the game to start the harvest, and it is not running", status.Reason.Summary);
    }

    [Fact]
    public void AHarvestThatCommitsWhenItIsDueEndsThePairsEpisode()
    {
        using var game = AutoHarvestTestGame.Load();
        game.Queue.SuppressMutationOf = game.Action(AutoHarvestPair.FruitTree);
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        game.Queue.SuppressMutationOf = null;
        harness.Advance(PastThePairWait);
        harness.PumpWorldsUntil(() => game.IsRunning(AutoHarvestPair.FruitTree));
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(0, harness.RetryGate.Count);
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
    }

    [Fact]
    public void APairThatStopsBeingACandidateWhileItBacksOffEndsItsEpisodeBeforeItIsDue()
    {
        using var game = AutoHarvestTestGame.Load();
        game.Queue.SuppressMutationOf = game.Action(AutoHarvestPair.FruitTree);
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        game.Plot(AutoHarvestPair.FruitTree).visible = false;
        harness.Advance(PastTheServiceWait);
        harness.PumpWorldsUntil(() => harness.RetryGate.Count == 0);
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.NotEqual(FeatureStatusState.Degraded, harness.Status.Current.State);
    }

    [Fact]
    public void AnAddThatLandsBeforeTheGameThrowsCommits()
    {
        using var game = AutoHarvestTestGame.Load();
        game.Queue.BreakAfterMutation = true;
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => game.IsRunning(AutoHarvestPair.FruitTree));
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(0, harness.RetryGate.Count);
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
        Assert.DoesNotContain(harness.Warnings, IsFaultWarning);
    }

    [Fact]
    public void AnAddThatThrowsWithoutLandingBacksThePairOff()
    {
        using var game = AutoHarvestTestGame.Load();
        game.Queue.SuppressMutationOf = game.Action(AutoHarvestPair.FruitTree);
        game.Queue.BreakAfterMutation = true;
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);

        Assert.Equal(AutoHarvestServicePolicies.RetryTarget(AutoHarvestPair.FruitTree), harness.RetryGate.Latest.Target);
        Assert.False(game.IsRunning(AutoHarvestPair.FruitTree));
    }

    [Fact]
    public void AThrowThatEscapesOnePairStillLetsTheOtherPairHarvest()
    {
        using var game = AutoHarvestTestGame.Load();
        game.Queue.EscapeFromAddOf = game.Action(AutoHarvestPair.FruitTree);
        using var harness = new Harness(treasure: true);

        harness.PumpWorldsUntil(() => harness.RetryGate.Count == 1);
        for (var wait = 0; wait < 4 && !game.IsRunning(AutoHarvestPair.TreasureTree); wait++)
        {
            harness.Advance(PastThePairWait);
            harness.PumpUntilTheNewestWorldIsEvaluated();
        }

        Assert.True(game.IsRunning(AutoHarvestPair.TreasureTree));
        Assert.False(game.IsRunning(AutoHarvestPair.FruitTree));
    }

    [Fact]
    public void ARegistryReadThatThrowsIsRetriedAndItsCauseStaysInTheLog()
    {
        const string cause = "injected registry outage";
        using var game = AutoHarvestTestGame.Load();
        var outage = true;
        using var harness = new Harness(readRegistry: () => outage
            ? throw new InvalidOperationException(cause)
            : TypedRegistrySourceSnapshot.Ready(IdScriptableObject.RuntimeLookup));

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        var status = harness.Status.Current;
        outage = false;
        harness.Advance(PastThePairWait);
        harness.PumpWorldsUntil(() => game.IsRunning(AutoHarvestPair.FruitTree));
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Contains("runtime registry source failed.", status.Reason.Summary);
        Assert.DoesNotContain(cause, status.Reason.Summary);
        Assert.Contains(_faultLog, entry => entry.Contains(cause, StringComparison.Ordinal));
        Assert.Equal(0, harness.RetryGate.Count);
    }

    [Fact]
    public void AWrongTypeUnderThePlotHoldsThePairForTheLifecycleAndTheNextOneProbesAgain()
    {
        using var game = AutoHarvestTestGame.Load();
        var plotId = new Guid(AutoHarvestKnownIds.FruitTreePlot);
        var impostor = new TreasurePoolSO();
        impostor.SetGuid(plotId);
        IdScriptableObject.RuntimeLookup[plotId] = impostor;
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => harness.RetryGate.Count == 1);
        harness.Advance(PastTheServiceWait);
        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.ContractUnavailable);
        IdScriptableObject.RuntimeLookup[plotId] = game.Plot(AutoHarvestPair.FruitTree);
        harness.Advance(PastEveryWait);
        harness.PumpUntilTheNewestWorldIsEvaluated();
        var heldAfterTheWaits = harness.Status.Current.State;
        var runningBeforeTheNewLifecycle = game.IsRunning(AutoHarvestPair.FruitTree);
        harness.ReplaceLifecycle();
        harness.PumpWorldsUntil(() => game.IsRunning(AutoHarvestPair.FruitTree));
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(FeatureStatusState.ContractUnavailable, heldAfterTheWaits);
        Assert.False(runningBeforeTheNewLifecycle);
        Assert.Equal(0, harness.RetryGate.Count);
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
    }

    [Fact]
    public void TheSameFaultWarnsOnceInALifecycleAndAgainInTheNext()
    {
        using var game = AutoHarvestTestGame.Load();
        game.Queue.SuppressMutationOf = game.Action(AutoHarvestPair.FruitTree);
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => harness.RetryGate.Count == 1);
        harness.Advance(PastThePairWait);
        harness.PumpWorldsUntil(() => harness.RetryGate.Latest.ConsecutiveFailures == 2);
        var warnedInTheFirstLifecycle = FaultWarnings(harness);
        harness.ReplaceLifecycle();
        harness.PumpWorldsUntil(() => FaultWarnings(harness) == 2);

        Assert.Equal(1, warnedInTheFirstLifecycle);
        Assert.All(
            harness.Warnings.FindAll(IsFaultWarning),
            warning => Assert.StartsWith(
                FailedToCollect + EntityIdentityFormatter.Format(new Guid(AutoHarvestKnownIds.FruitTreePlot)),
                warning));
    }

    private static bool IsFaultWarning(string warning) => warning.StartsWith(FailedToCollect, StringComparison.Ordinal);

    private static int FaultWarnings(Harness harness) => harness.Warnings.FindAll(IsFaultWarning).Count;

    private sealed class Harness : IDisposable
    {
        private readonly ServiceCycleRegistry _registry;
        private readonly AutomataServiceCycleRuntime _runtime;
        private readonly ThreadSafeTestClock _clock = new(1_000);
        private readonly AutomataServiceCycleHost _host;
        private readonly List<string> _warnings = new();
        private long _epoch = 1;
        private long _frame = 10;
        private ulong _generation = 2;

        internal Harness(bool treasure = false, Func<TypedRegistrySourceSnapshot>? readRegistry = null)
        {
            Status = new AutomataFeatureStatusReporter(
                new FeatureStatusRegistry(),
                new FeatureStatusSnapshot(
                    new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoHarvestFeatureId),
                    "Auto Harvest",
                    true,
                    FeatureStatusState.NotReady,
                    new FeatureStatusReason(FeatureStatusReasonCode.RegistryNotReady, "waiting"),
                    _epoch));
            _registry = new ServiceCycleRegistry(
                1,
                AutoHarvestConfigurationFactory.Create(
                    masterEnabled: true,
                    emergencyDisabled: false,
                    activeMode: true,
                    fruitSelected: true,
                    treasureSelected: treasure),
                new ConfigGeneration(1),
                new LifecycleGeneration((ulong)_epoch),
                _clock);
            var resolver = new TypedRegistryResolver(
                () => _epoch,
                readRegistry ?? (static () => TypedRegistrySourceSnapshot.Ready(IdScriptableObject.RuntimeLookup)),
                static value => ((IdScriptableObject)value).GetGuid());
            var feature = new AutoHarvestServiceCycleFeature(
                new AutoHarvestFeatureDependencies(
                    resolver,
                    static () => true,
                    static () => true,
                    runtimeDiagnostics: null,
                    Status,
                    Warn)).Register(
                new AutomataServiceCycleFeatureContext(
                    _registry, new ConfigGeneration(1), _epoch, new LifecycleGameActions()));
            _registry.WorldPublication.Publish(TestWorlds.FromLoadedRegistries(), new WorldGeneration(_generation));
            _host = new AutomataServiceCycleHost(_registry, () => _frame++, pumpTiming: null, semanticTrace: null);
            _runtime = new AutomataServiceCycleRuntime(
                () => _epoch,
                _registry.Configuration,
                _host,
                new IAutomataServiceCycleFeatureRuntime[] { feature },
                new ConfigGeneration(1),
                new LifecycleGameActions());
            feature.ActivateDiagnostics();
        }

        internal AutomataFeatureStatusReporter Status { get; }

        internal List<string> Warnings
        {
            get
            {
                lock (_warnings) return new List<string>(_warnings);
            }
        }

        internal ServiceRetryGateSnapshot RetryGate => Service.RetryGate;

        private ServiceCycleServiceDiagnosticsSnapshot Service =>
            AvailableServiceDiagnostics.Await(_host.Pump, AutoHarvestServicePolicies.ServiceId);

        internal void Advance(MonotonicDuration duration) => _clock.Advance(duration);

        internal void ReplaceLifecycle()
        {
            _epoch++;
            _runtime.InvalidateLifecycle();
        }

        internal void PumpWorldsUntil(Func<bool> condition)
        {
            var deadline = Stopwatch.StartNew();
            while (!condition())
            {
                PumpWorld();
                if (deadline.Elapsed > ServiceCycleTestDeadline.Value)
                    throw new TimeoutException("Auto Harvest did not reach the expected state.");
                Thread.Yield();
            }
        }

        internal void PumpUntilTheNewestWorldIsEvaluated()
        {
            PumpWorld();
            var newest = new WorldGeneration(_generation);
            var deadline = Stopwatch.StartNew();
            while (!EvaluatedAndDrained(Service, newest))
            {
                _runtime.Tick(0);
                if (deadline.Elapsed > ServiceCycleTestDeadline.Value)
                    throw new TimeoutException("Auto Harvest did not evaluate the newest world.");
                Thread.Yield();
            }
            _runtime.Tick(0);
        }

        public void Dispose()
        {
            _runtime.Dispose();
            _registry.Dispose();
            Status.Dispose();
        }

        private static bool EvaluatedAndDrained(ServiceCycleServiceDiagnosticsSnapshot service, WorldGeneration world) =>
            service.LatestProjection.Context.Cycle.World == world && !service.ActiveBatch.IsPresent;

        private void Warn(string message)
        {
            lock (_warnings) _warnings.Add(message);
        }

        private void PumpWorld()
        {
            _generation = Math.Max(_generation + 1, checked((ulong)_frame));
            _registry.WorldPublication.Publish(TestWorlds.FromLoadedRegistries(), new WorldGeneration(_generation));
            _runtime.Tick(0);
        }
    }
}
