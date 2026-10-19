using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using OrbModding.Tests.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

/// <summary>
/// A purchase the game refuses backs its own candidate off on the shared retry gate and is tried
/// again when due, while Auto Buy keeps buying everything else and never writes the player's
/// configuration. Only the refusal the suite's model says cannot happen holds the service back, and
/// only until the next lifecycle.
/// </summary>
public sealed class AutoBuyRefusalRetryTests
{
    private static readonly Guid First = Guid.Parse("11111111-0000-0000-0000-000000000000");
    private static readonly Guid Second = Guid.Parse("22222222-0000-0000-0000-000000000000");
    private static readonly MonotonicDuration InitialBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250));

    [Fact]
    public void ALevelRefusalBacksTheCandidateOffWhileTheNextOneIsBoughtAndRetriesItWhenDue()
    {
        var purchases = new Purchases(First, LevelCapRefusal());
        using var harness = new Harness(purchases);

        harness.PumpWorldsUntil(() => purchases.Submissions(First) == 1);
        var entry = harness.RetryGate.Latest;
        harness.PumpWorldsUntil(() => purchases.Commits(Second) == 1);
        var firstAttemptsWhenSecondWasBought = purchases.Submissions(First);
        harness.PumpWorldsUntil(() => purchases.Submissions(First) == 2);

        Assert.Equal(First, entry.Target.Id);
        Assert.False(entry.IsQuiet);
        Assert.Contains("IsMaxQueuedLevel()", entry.Reason);
        Assert.Equal(1, firstAttemptsWhenSecondWasBought);
        Assert.True(
            purchases.SubmittedAt(First, 1) - purchases.SubmittedAt(First, 0) >= InitialBackoff,
            "The refused candidate was retried before its backoff was due.");
        Assert.False(harness.Latch.IsTripped);
        Assert.Empty(harness.Errors);
        Assert.Equal(FeatureStatusState.Degraded, harness.Status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.ContractMismatch, harness.Status.Current.Reason.Code);
        Assert.True(harness.Status.Current.ConfiguredEnabled);
    }

    [Fact]
    public void AnAvailabilityRefusalStaysQuietUntilAFresherWorldRefusesItAgain()
    {
        var purchases = new Purchases(First, AvailabilityRefusal());
        using var harness = new Harness(purchases);

        harness.PumpWorldsUntil(() => purchases.Submissions(First) == 1);
        var first = harness.RetryGate.Latest;
        harness.PumpWorldsUntil(() => purchases.Commits(Second) == 1);
        var quietStatus = harness.Status.Current.State;
        harness.PumpWorldsUntil(() => purchases.Submissions(First) == 2);
        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);

        Assert.True(first.IsQuiet);
        Assert.Equal(FeatureStatusState.Operational, quietStatus);
        Assert.False(harness.RetryGate.Latest.IsQuiet);
        Assert.Empty(harness.Errors);
    }

    /// <summary>
    /// The world reading the candidate as unavailable agrees with the game, so the disagreement is
    /// over: the episode ends and nothing keeps Auto Buy Degraded.
    /// </summary>
    [Fact]
    public void AWorldThatShowsTheRefusedCandidateUnavailableEndsItsEpisode()
    {
        var purchases = new Purchases(First, LevelCapRefusal());
        using var harness = new Harness(purchases);

        harness.PumpWorldsUntil(() => purchases.Submissions(First) == 1);
        var backingOff = harness.RetryGate.Count;
        harness.PumpWorldsUntil(
            () => harness.RetryGate.Count == 0 &&
                harness.Status.Current.State == FeatureStatusState.Operational,
            firstUnlocked: false);

        Assert.Equal(1, backingOff);
        Assert.Equal(1, purchases.Submissions(First));
    }

    /// <summary>
    /// A refusal every readable term says cannot happen holds Auto Buy back for the lifecycle, with
    /// the saved mode untouched; the next lifecycle clears it and probes the same candidate again.
    /// </summary>
    [Fact]
    public void AnImpossibleRefusalHoldsAutoBuyBackUntilTheNextLifecycle()
    {
        var purchases = new Purchases(First, ImpossibleRefusal());
        using var harness = new Harness(purchases);

        harness.PumpWorldsUntil(() => harness.Latch.IsTripped);
        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Faulted);
        var heldStatus = harness.Status.Current;
        for (var index = 0; index < 40; index++) harness.PumpWorld();
        var submissionsWhileHeld = purchases.TotalSubmissions;
        purchases.Refusing = false;
        harness.LoadSave();
        harness.PumpWorldsUntil(() => purchases.Commits(First) == 1);

        Assert.Equal(FeatureStatusReasonCode.InvariantViolation, heldStatus.Reason.Code);
        Assert.True(heldStatus.ConfiguredEnabled);
        Assert.Contains("holds off until the next load, reset or prestige", heldStatus.Reason.Summary);
        Assert.Equal(1, submissionsWhileHeld);
        Assert.Equal(heldStatus.Reason.Summary, Assert.Single(harness.Errors));
        Assert.False(harness.Latch.IsTripped);
    }

    private static AutoBuyAdmissionDiagnosis LevelCapRefusal() =>
        new(
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Refused,
            AutoBuyAdmissionTerm.Passed);

    private static AutoBuyAdmissionDiagnosis AvailabilityRefusal() =>
        new(
            AutoBuyAdmissionTerm.Refused,
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Passed);

    private static AutoBuyAdmissionDiagnosis ImpossibleRefusal() =>
        new(
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Passed);

    /// <summary>
    /// The shipping Automata runtime and host around the shipping Auto Buy feature runtime, with only
    /// the native purchase and queue ports, the clock and the world source standing in for the game.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        private readonly ServiceCycleRegistry _registry;
        private readonly ServiceRegistration<AutoBuyCycleState, AutoBuyCycleAction> _registration;
        private readonly AutomataServiceCycleRuntime _runtime;
        private readonly FeatureStatusRegistry _statuses = new();
        private readonly ThreadSafeTestClock _clock = new(1_000);
        private long _frame = 10;
        private long _epoch = 7;
        private ulong _generation = 2;

        internal Harness(Purchases purchases)
        {
            purchases.Clock = () => _clock.Now;
            Status = new AutomataFeatureStatusReporter(
                _statuses,
                new FeatureStatusSnapshot(
                    new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoBuyFeatureId),
                    "Auto Buy",
                    true,
                    FeatureStatusState.NotReady,
                    new FeatureStatusReason(FeatureStatusReasonCode.GameplayNotReady, "not ready"),
                    _epoch));
            var bundles = new Bundles();
            var responder = new AutoBuyRefusalResponder(Latch, bundles, Errors.Add);
            var actions = new AutoBuyCycleActionAdapter(
                purchases,
                new RoomyQueue(),
                () => _epoch,
                () => AutoBuyCandidateKinds.All,
                responder);
            _registry = new ServiceCycleRegistry(
                1,
                Configuration(),
                new ConfigGeneration(1),
                new LifecycleGeneration((ulong)_epoch),
                _clock);
            _registration = _registry.Register(AutoBuyService.Define(actions, Latch));
            _registry.WorldPublication.Publish(World(_epoch, firstUnlocked: true, firstAffordable: true), new WorldGeneration(_generation));
            var feature = new AutoBuyFeatureRuntime(
                new AutoBuyFeatureDependencies(
                    () => _epoch,
                    () => AutoBuyCandidateKinds.All,
                    runtimeDiagnostics: null,
                    Status,
                    bundles,
                    Errors.Add),
                _registration,
                _epoch,
                new ConfigGeneration(1),
                actions,
                Latch);
            _runtime = new AutomataServiceCycleRuntime(
                () => _epoch,
                _registry.Configuration,
                new AutomataServiceCycleHost(_registry, () => _frame++, pumpTiming: null, semanticTrace: null),
                new IAutomataServiceCycleFeatureRuntime[] { feature },
                new ConfigGeneration(1),
                new LifecycleGameActions());
            feature.ActivateDiagnostics();
        }

        internal AutomataFeatureStatusReporter Status { get; }
        internal AutoBuyInvariantLatch Latch { get; } = new();
        internal List<string> Errors { get; } = new();
        internal ServiceRetryGateSnapshot RetryGate => _registration.Runner.Snapshot.RetryGate;
        internal MonotonicTimestamp Now => _clock.Now;

        /// <summary>A save load, reset or prestige: the game reports a new lifecycle and the runtime moves to it.</summary>
        internal void LoadSave()
        {
            _epoch++;
            _runtime.InvalidateLifecycle();
        }

        internal void PumpWorldsUntil(Func<bool> condition, bool firstUnlocked = true, bool firstAffordable = true)
        {
            var deadline = Stopwatch.StartNew();
            while (!condition())
            {
                PumpWorld(firstUnlocked, firstAffordable);
                if (deadline.Elapsed > ServiceCycleTestDeadline.Value)
                    throw new TimeoutException("Auto Buy did not reach the expected state.");
                Thread.Yield();
            }
        }

        internal void PumpWorld(bool firstUnlocked = true, bool firstAffordable = true)
        {
            _registry.WorldPublication.Publish(World(_epoch, firstUnlocked, firstAffordable), new WorldGeneration(++_generation));
            _runtime.Tick(0);
            _clock.Advance(MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(16)));
        }

        public void Dispose()
        {
            _runtime.Dispose();
            _registry.Dispose();
            Status.Dispose();
        }
    }

    /// <summary>Refuses one candidate with the given diagnosis and buys every other one.</summary>
    private sealed class Purchases : IAutoBuyNativePurchasePort
    {
        private readonly Guid _refused;
        private readonly AutoBuyAdmissionDiagnosis _diagnosis;
        private readonly Dictionary<Guid, List<MonotonicTimestamp>> _submitted = new();
        private readonly Dictionary<Guid, int> _commits = new();

        internal Purchases(Guid refused, AutoBuyAdmissionDiagnosis diagnosis)
        {
            _refused = refused;
            _diagnosis = diagnosis;
        }

        internal Func<MonotonicTimestamp>? Clock { get; set; }
        internal bool Refusing { get; set; } = true;
        internal int TotalSubmissions { get; private set; }

        internal int Submissions(Guid uuid) => _submitted.TryGetValue(uuid, out var times) ? times.Count : 0;
        internal int Commits(Guid uuid) => _commits.TryGetValue(uuid, out var count) ? count : 0;
        internal MonotonicTimestamp SubmittedAt(Guid uuid, int index) => _submitted[uuid][index];

        public AutoBuyPurchaseSubmission Submit(
            AutoBuyCandidateKind kind,
            Guid uuid,
            int count,
            long lifecycleEpoch)
        {
            TotalSubmissions++;
            if (!_submitted.TryGetValue(uuid, out var times)) _submitted[uuid] = times = new List<MonotonicTimestamp>();
            times.Add(Clock?.Invoke() ?? default);
            if (Refusing && uuid == _refused) return AutoBuyPurchaseSubmission.NotAdmissible(in _diagnosis);
            _commits[uuid] = Commits(uuid) + 1;
            var queued = 0;
            var evidence = NativeMutationVerifier.Execute(
                "Auto Buy Structure",
                uuid.ToString(),
                "queued level +1",
                () => queued,
                () => queued++,
                static (before, after) => after == before + 1);
            return AutoBuyPurchaseSubmission.Attempted(evidence, requestedLevels: 1);
        }
    }

    private sealed class RoomyQueue : IAutoBuyQueueRoomPort
    {
        public bool TryReadRemainingRoom(out int remainingRoom, out string reason)
        {
            remainingRoom = 64;
            reason = string.Empty;
            return true;
        }
    }

    private sealed class Bundles : IAutoBuyRefusalBundlePort
    {
        public bool TryWrite(string contents, DateTime utcNow, out string path)
        {
            path = "diagnostics/autobuy-refusal.txt";
            return true;
        }
    }

    /// <summary>Two structures priced at one unit of a resource the player holds plenty of.</summary>
    private static GameWorldState World(long epoch, bool firstUnlocked, bool firstAffordable)
    {
        var structureTypeId = Guid.Parse("33333333-0000-0000-0000-000000000000");
        var listId = Guid.Parse("44444444-0000-0000-0000-000000000000");
        var viewId = Guid.Parse("55555555-0000-0000-0000-000000000000");
        var resourceId = Guid.Parse("66666666-0000-0000-0000-000000000000");
        var ids = new[] { First, Second };
        var costs = new WorldPurchaseCost[ids.Length];
        var relations = new WorldPurchaseViewRelation[ids.Length];
        var routes = new WorldPurchaseViewRoute[ids.Length];
        var structures = new WorldStructure[ids.Length];
        for (var index = 0; index < ids.Length; index++)
        {
            costs[index] = new WorldPurchaseCost(
                ids[index],
                resourceId,
                index == 0 && !firstAffordable ? new BigDouble(1.0, 9) : new BigDouble(1.0, 0));
            relations[index] = new WorldPurchaseViewRelation(
                ids[index],
                WorldPurchaseCandidateKind.Structure,
                structureTypeId,
                1,
                WorldPurchaseViewRelationStatus.Resolved);
            routes[index] = new WorldPurchaseViewRoute(ids[index], listId, viewId);
            structures[index] = WorldStructureDeriver.Shared.Derive(
                WorldSamples.Structure(
                    ids[index],
                    unlocked: index != 0 || firstUnlocked,
                    structureTypeId: structureTypeId));
        }
        return new GameWorldState
        {
            CollectedAtEpoch = epoch,
            Structures = WorldTable.Create(structures),
            Views = WorldTable.Create(new[] { new WorldView(viewId, false, false, true) }),
            PurchaseViewRelations = PublicationTable<WorldPurchaseViewRelation>.Create(relations, relations.Length),
            PurchaseViewRoutes = PublicationTable<WorldPurchaseViewRoute>.Create(routes, routes.Length),
            Resources = WorldTable.Create(
                new[]
                {
                    new WorldResourceDeriver(default).Derive(
                        WorldSamples.Resource(resourceId, new BigDouble(1.0, 6), new BigDouble(-1d))),
                }),
            PurchaseCosts = PublicationTable<WorldPurchaseCost>.Create(costs, costs.Length),
        };
    }

    private static SuiteRuntimeConfiguration Configuration() =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoBuy = new AutoBuyConfiguration
            {
                Mode = AutoBuyOperationMode.Active,
                IncludeStructures = true,
                IncludeUpgrades = false,
            },
        };
}
