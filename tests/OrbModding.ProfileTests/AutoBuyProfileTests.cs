using System;
using OrbAutomata;
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.World;
using Xunit;
using static OrbModding.ProfileTests.AutoHarvestProfileTestSupport;

namespace OrbModding.ProfileTests;

public sealed class AutoBuyProfileTests : IDisposable
{
    private const long PlannedEpoch = 7;

    public AutoBuyProfileTests() => ResetNativeState();

    public void Dispose() => ResetNativeState();

    [Fact]
    public void CommittedStructurePurchaseRoutesAllFourActionStages()
    {
        var measurement = new CapturingMeasurementPort();
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(measurement);
        var operations = new AutomataProfileOperations(probe);

        global::ActionManager.RemainingRoom = 64;
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 3,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(operations, AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid));

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(4, structure.queuedQuantity);
        Assert.Empty(measurement.Abandoned);
        Assert.Collection(
            measurement.Completed,
            item => AssertStage(item, ServiceCycleProfileSpan.AutoBuyActionQueueRoomRead),
            item => AssertStage(
                item,
                ServiceCycleProfileSpan.AutoBuyActionCandidateResolution,
                stableIdReads: 2,
                listEntries: 1),
            // Lifecycle bind already captured the authored category/list/view chain. The action
            // boundary reads only current view availability and the thin StructureSO.CanPurchase()
            // fold, so no topology fields or list entries appear on the warm action path.
            item => AssertStage(
                item,
                ServiceCycleProfileSpan.AutoBuyActionAdmissionRevalidation,
                methodCalls: 3),
            item => AssertStage(
                item,
                ServiceCycleProfileSpan.AutoBuyActionNativeSubmission,
                methodCalls: 3,
                invocationArgumentArrays: 1));
        Assert.Same(measurement, probe.Detach());
    }

    [Fact]
    public void PreflightRejectionStopsBeforeTheNativeSubmissionStage()
    {
        var measurement = new CapturingMeasurementPort();
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(measurement);
        var operations = new AutomataProfileOperations(probe);

        global::ActionManager.RemainingRoom = 64;
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = false,
            queuedQuantity = 3,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(operations, AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid));

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(3, structure.queuedQuantity);
        Assert.Equal(
            new[]
            {
                ServiceCycleProfileSpan.AutoBuyActionQueueRoomRead,
                ServiceCycleProfileSpan.AutoBuyActionCandidateResolution,
                ServiceCycleProfileSpan.AutoBuyActionAdmissionRevalidation,
            },
            Spans(measurement.Completed));
        Assert.Empty(measurement.Abandoned);
        Assert.Same(measurement, probe.Detach());
    }

    [Fact]
    public void LifecycleRouteDiagnosticNamesEveryRouteAndItsCurrentVisibility()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        global::StructureSO.All.Add(structure);
        var topology = CaptureTopology(out var relations, out var routes);

        Assert.Equal(1, relations.Count);
        Assert.Equal(1, routes.Count);
        var line = Assert.Single(topology.DescribeCaptured(PlannedEpoch));
        Assert.Contains(structure.GetGuid().ToString("D"), line);
        Assert.Contains(global::ViewSO.All[0].GetGuid().ToString("D"), line);
        Assert.Contains(global::ViewSO.All[0].relevantLists[0].GetGuid().ToString("D"), line);
        Assert.Contains("view-available=true", line);

        global::ViewSO.All[0].available = false;

        Assert.Contains(
            "view-available=false",
            Assert.Single(topology.DescribeCaptured(PlannedEpoch)));
    }

    /// <summary>
    /// A collector nobody composed for the session cannot take the live purchase topology away from
    /// the one that was.
    /// </summary>
    /// <remarks>
    /// This is the shape that cost a live save twenty-seven minutes of refused purchases: a
    /// verification pass allocated throwaway collectors, each took the process-wide owning-view
    /// resolver, and each restamped it at the epoch its own frame carried — zero. The adapter is
    /// built through its production constructor on purpose. Handing it a resolver is what made the
    /// defect invisible: an injected resolver is not the singleton the running suite reads from, so
    /// a test that injects one cannot see the singleton being clobbered.
    /// </remarks>
    [Fact]
    public void ThrowawayCollectorDoesNotUnreadTheSessionPurchaseTopology()
    {
        var operations = new AutomataProfileOperations(new ServiceCycleProfileProbe());
        global::ActionManager.RemainingRoom = 64;
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 3,
        };
        global::StructureSO.All.Add(structure);

        GameWorldCollector.ForSession().Collect(
            new GameWorldCycleFrame { CollectedAtEpoch = PlannedEpoch });
        new GameWorldCollector().Collect();

        var adapter = new AutoBuyCycleActionAdapter(
            new AutoBuyNativePurchaseAdapter(operations),
            new AutoBuyNativeQueueRoomAdapter(),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            operations,
            IgnoreRefusals.Instance);
        var result = adapter.TryExecute(
            new AutoBuyCycleAction(
                AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), PlannedEpoch),
            Configuration(),
            ActionContext());

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(4, structure.queuedQuantity);
    }

    private static ServiceActionResult Execute(
        AutomataProfileOperations operations,
        AutoBuyCandidateKind kind,
        Guid uuid)
    {
        var topology = CaptureTopology(out var relations, out var routes);
        Assert.Equal(1, relations.Count);
        Assert.Equal(1, routes.Count);
        var adapter = new AutoBuyCycleActionAdapter(
            new AutoBuyNativePurchaseAdapter(operations, topology),
            new AutoBuyNativeQueueRoomAdapter(),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            operations,
            IgnoreRefusals.Instance);
        return adapter.TryExecute(
            new AutoBuyCycleAction(kind, uuid, PlannedEpoch),
            Configuration(),
            ActionContext());
    }

    private static NativePurchaseViewAdmissionResolver CaptureTopology(
        out WorldRelationBuffer<WorldPurchaseViewRelation> relations,
        out WorldRelationBuffer<WorldPurchaseViewRoute> routes)
    {
        Assert.True(
            NativePurchaseViewAdmissionResolver.TryCreate(
                WorldNativeTypes.Resolve,
                out var topology,
                out var topologyFailure),
            topologyFailure);
        relations = new WorldRelationBuffer<WorldPurchaseViewRelation>();
        routes = new WorldRelationBuffer<WorldPurchaseViewRoute>();
        topology!.ReadAll(
            PlannedEpoch,
            relations,
            routes,
            new WorldRelationBuffer<WorldUpgradeListMembership>(),
            out var unresolved,
            out var skipped,
            out _);
        Assert.Equal(0, unresolved);
        Assert.Equal(0, skipped);
        return topology;
    }

    private static ServiceActionContext ActionContext()
    {
        var coordinates = new ServiceCycleProfileCoordinates(serviceOrdinal: 1, frameIdentity: 41);
        var identity = new ServiceCycleIdentity(
            new ServiceId("AutoBuy"),
            new LifecycleGeneration((ulong)PlannedEpoch),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new WorldGeneration(1),
            new CycleId(5));
        return new ServiceActionContext(
            identity,
            new BatchId(1),
            new ActionId(1),
            actionIndex: 0,
            new MonotonicTimestamp(10),
            in coordinates);
    }

    private static SuiteRuntimeConfiguration Configuration() => new()
    {
        General = new SuiteGeneralConfiguration { Enabled = true },
        AutoBuy = new AutoBuyConfiguration
        {
            Mode = AutoBuyOperationMode.Active,
            IncludeStructures = true,
            IncludeUpgrades = true,
        },
    };

    /// <summary>
    /// An explicit request names an amount, and the honest answer to an amount that does not fit is
    /// the one that does. Round 9 asked for a thousand levels against four levels of headroom, was
    /// quietly given one, and the answer was byte-identical to a satisfied <c>amount=1</c> — the
    /// clamp target was the live queue room rather than the headroom, so no correct next action was
    /// derivable from the response at all.
    /// </summary>
    [Fact]
    public void AnExplicitOverAskIsRefusedWithTheRoomInsteadOfClampedToIt()
    {
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(new CapturingMeasurementPort());
        var operations = new AutomataProfileOperations(probe);
        global::ActionManager.RemainingRoom = 3;

        var purchases = new RecordingCountPort();
        var adapter = new AutoBuyCycleActionAdapter(
            purchases,
            new AutoBuyNativeQueueRoomAdapter(),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            operations,
            IgnoreRefusals.Instance,
            null,
            _ => true);

        var result = adapter.TryExecuteGameMcp(
            new AutoBuyCycleAction(
                AutoBuyCandidateKind.Upgrade, Guid.NewGuid(), PlannedEpoch, count: 1000),
            Configuration(),
            ActionContext());

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoBuyActionResultCodes.QueueRoomBelowRequest, result.Code);
        Assert.Equal(3, adapter.LastSubmission.MaximumAmount);
        Assert.Contains("room for 3 more levels", adapter.LastSubmission.Reason);
        Assert.False(purchases.Submitted);
    }

    /// <summary>
    /// The planner's own count is still clamped, because a plan that asks for what it hoped for and
    /// takes what fits is the plan working. Only a caller-named amount is a promise.
    /// </summary>
    [Fact]
    public void APlannedBatchStillTakesWhateverRoomIsLeft()
    {
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(new CapturingMeasurementPort());
        var operations = new AutomataProfileOperations(probe);
        global::ActionManager.RemainingRoom = 3;

        var purchases = new RecordingCountPort();
        var adapter = new AutoBuyCycleActionAdapter(
            purchases,
            new AutoBuyNativeQueueRoomAdapter(),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            operations,
            IgnoreRefusals.Instance);

        adapter.TryExecute(
            new AutoBuyCycleAction(
                AutoBuyCandidateKind.Upgrade, Guid.NewGuid(), PlannedEpoch, count: 1000),
            Configuration(),
            ActionContext());

        Assert.True(purchases.Submitted);
        Assert.Equal(3, purchases.LastCount);
    }

    private sealed class RecordingCountPort : IAutoBuyNativePurchasePort
    {
        internal bool Submitted { get; private set; }
        internal int LastCount { get; private set; }

        public AutoBuyPurchaseSubmission Submit(
            AutoBuyCandidateKind kind,
            Guid uuid,
            int count,
            long lifecycleEpoch,
            in ServiceActionContext context)
        {
            Submitted = true;
            LastCount = count;
            return AutoBuyPurchaseSubmission.Rejected(
                AutoBuyPurchasePreflight.CandidateUnavailable);
        }
    }

    private sealed class IgnoreRefusals : IAutoBuyRefusalResponsePort
    {
        internal static IgnoreRefusals Instance { get; } = new();
        public void ObserveRefusal(in AutoBuyRefusalReport report)
        {
        }
    }

    private static void AssertStage(
        in CapturedMeasurement item,
        ServiceCycleProfileSpan span,
        uint fieldReads = 0,
        uint methodCalls = 0,
        uint stableIdReads = 0,
        uint listEntries = 0,
        uint invocationArgumentArrays = 0)
    {
        Assert.Equal((int)span, item.Context.StageCode);
        Assert.Equal(1, item.Context.ServiceOrdinal);
        Assert.Equal((ulong)41, item.Context.Frame);
        Assert.Equal(ServiceCycleProfileTemperature.Warm, item.Context.Temperature);
        Assert.Equal(fieldReads, item.Operations.ReflectedFieldReads);
        Assert.Equal(methodCalls, item.Operations.ReflectedMethodCalls);
        Assert.Equal(stableIdReads, item.Operations.StableIdReads);
        Assert.Equal(listEntries, item.Operations.ListEntries);
        Assert.Equal(invocationArgumentArrays, item.Operations.InvocationArgumentArrays);
        Assert.Equal((uint)0, item.Operations.RecordCopies);
    }

    private static void ResetNativeState()
    {
        global::StructureSO.All.Clear();
        global::UpgradeSO.All.Clear();
        global::ViewSO.All.Clear();
        global::ResourceSO.All.Clear();
        global::ValueModifierVariable.All.Clear();
        global::ActionManager.instance = new global::ActionManager();
        global::ActionManager.RemainingRoom = 0;
        global::GlobalVariables.MultiBuy = new global::IntVariable();
        global::Player.BulkDevelopment = new global::IntVariable();
        var structureList = new global::StructureListVariable
        {
            value = global::StructureSO.All,
        };
        var upgradeList = new global::UpgradeListVariable
        {
            value = global::UpgradeSO.All,
        };
        var owningView = new global::ViewSO { available = true };
        owningView.relevantLists.Add(structureList);
        owningView.relevantLists.Add(upgradeList);
        global::ViewSO.All.Add(owningView);
        NativeMultiBuyScope.ResetQuarantineForTests();
        NativePurchaseViewAdmissionResolver.ResetProductionForTests();
    }
}
