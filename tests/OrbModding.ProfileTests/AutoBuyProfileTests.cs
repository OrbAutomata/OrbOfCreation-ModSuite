using System;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common;
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.World;
using Xunit;
using static OrbModding.ProfileTests.AutoHarvestProfileTestSupport;

namespace OrbModding.ProfileTests;

[Collection(NativeRegistryCollection.Name)]
[OrbModding.TestSupport.ResetGameStubs]
public sealed class AutoBuyProfileTests : IDisposable
{
    private const long PlannedEpoch = 7;

    public AutoBuyProfileTests() => ResetNativeState();

    public void Dispose() => ResetNativeState();

    [Fact]
    public void AManualPurchasePreservesTheQueueReadFaultAndItsLogReference()
    {
        var faults = new System.Collections.Generic.List<string>();
        GameActionFaultLog.ConfigureLog(faults.Add);
        var failure = new InvalidOperationException("private manual queue failure");
        global::ActionManager.RemainingRoomFault = failure;
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration((ulong)PlannedEpoch));
        var world = GameWorldFrameDeriver.Build(new GameWorldCycleFrame { CollectedAtEpoch = PlannedEpoch });
        registry.WorldPublication.Publish(world, new WorldGeneration(2));
        var probe = new ServiceCycleProfileProbe();
        var gameActions = new LifecycleGameActions();
        using var configuration = new ServiceConfigurationPublisher(Configuration());
        using var status = new AutomataFeatureStatusReporter(new FeatureStatusRegistry(),
            new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoBuyFeatureId),
                "Auto Buy", true, FeatureStatusState.NotReady,
                new FeatureStatusReason(FeatureStatusReasonCode.RegistryNotReady, "waiting"), PlannedEpoch));
        var feature = new AutoBuyServiceCycleFeature(new AutoBuyFeatureDependencies(
            () => PlannedEpoch, () => AutoBuyCandidateKinds.All, null, status,
            new AutoBuyRefusalBundleWriter(() => throw new InvalidOperationException("unused")),
            _ => { }, _ => { }, gameMcpOwnership: _ => true));
        var context = new AutomataServiceCycleFeatureContext(
            registry, new ConfigGeneration(1), PlannedEpoch, gameActions, probe);
        var runtime = feature.Register(in context);
        using var host = new AutomataServiceCycleHost(registry, () => 1, null, null, probe);
        try
        {
            var dispatcher = new GameMcpCommandDispatcher(
                host, configuration, gameActions, new[] { runtime });
            var command = new GameMcpCommand(
                1, GameMcpCommandKind.Purchase, PlannedEpoch, 1, "structure", Guid.NewGuid(), Guid.Empty,
                "StructureSO", 1, string.Empty, string.Empty, false);

            var result = dispatcher.ExecuteGameMcp(command);

            Assert.Equal("faulted", result.Status);
            Assert.Equal("adapter_fault", result.Code);
            Assert.StartsWith("The remaining queue room could not be read. Reference MCP-", result.Reason);
            Assert.DoesNotContain("private manual queue failure", result.Reason);
            var logged = Assert.Single(faults);
            Assert.Contains(failure.ToString(), logged);
            Assert.Contains(logged.Split(' ')[3], result.Reason);
        }
        finally
        {
            runtime.DisposeRegistration();
            GameActionFaultLog.ConfigureLog(null);
            global::ActionManager.RemainingRoomFault = null;
        }
    }

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
        Publish(structure);

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
                stableIdReads: 1),
            // Lifecycle bind already captured the authored category/list/view chain, so no list
            // entries appear on the warm action path.
            item => AssertStage(item, ServiceCycleProfileSpan.AutoBuyActionAdmissionRevalidation),
            item => AssertStage(item, ServiceCycleProfileSpan.AutoBuyActionNativeSubmission));
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
        Publish(structure);

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
        Publish(structure);
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
    /// The adapter is built through its production constructor on purpose. Handing it a resolver is
    /// what made the defect invisible: an injected resolver is not the singleton the running suite
    /// reads from, so a test that injects one cannot see the singleton being clobbered.
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
        Publish(structure);

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

    /// <summary>
    /// The press that switched Auto Buy off in a live run: <c>game_purchase</c> asked for Improved
    /// Brewing after it was already bought. The game refused it on its level cap, and the suite filed
    /// that refusal as a planner error and disabled Auto Buy. A manual press has no plan to be wrong
    /// about, so the game's no answers the caller alone; the same refusal of a planned purchase still
    /// reaches the breaker.
    /// </summary>
    [Fact]
    public void TheGameRefusingAManualPressNeverReachesThePlannerBreaker()
    {
        var operations = new AutomataProfileOperations(new ServiceCycleProfileProbe());
        global::ActionManager.RemainingRoom = 64;
        var skill = new global::ResourceSO
        {
            uuid = Guid.NewGuid().ToString(),
            quantity = new BigDouble(2.04, 8),
        };
        var control = new global::ResourceSO
        {
            uuid = Guid.NewGuid().ToString(),
            quantity = new BigDouble(1.35, 6),
        };
        var improvedBrewing = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            level = 1,
            maxLevel = 1,
            available = false,
        };
        improvedBrewing.purchaseCost.costs.Add(
            new global::ResourceTuple(skill, new BigDouble(2, 6)));
        improvedBrewing.purchaseCost.costs.Add(
            new global::ResourceTuple(control, new BigDouble(1, 4)));
        Publish(improvedBrewing);
        var uuid = Guid.Parse(improvedBrewing.uuid);
        var topology = CaptureTopology(out _, out _);
        var refusals = new RecordingRefusals();
        var adapter = new AutoBuyCycleActionAdapter(
            new AutoBuyNativePurchaseAdapter(operations, topology),
            new AutoBuyNativeQueueRoomAdapter(),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            operations,
            refusals,
            _ => true);

        var manual = adapter.TryExecuteGameMcp(
            new AutoBuyCycleAction(AutoBuyCandidateKind.Upgrade, uuid, PlannedEpoch),
            Configuration(),
            ActionContext());

        Assert.Equal(ServiceActionDisposition.Rejected, manual.Disposition);
        Assert.Equal(
            AutoBuyRefusalClassification.StructuralMismatch,
            adapter.LastSubmission.Diagnosis.Classification);
        Assert.Empty(refusals.Reports);

        adapter.TryExecute(
            new AutoBuyCycleAction(AutoBuyCandidateKind.Upgrade, uuid, PlannedEpoch),
            Configuration(),
            ActionContext());

        Assert.Equal(uuid, Assert.Single(refusals.Reports).Uuid);
        Assert.Equal(0, improvedBrewing.queuedLevels);
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
            out _,
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
    /// The game's own buy buttons never refuse an over-ask: they deliver what fits. So does this.
    /// So the press fills, and the levels the suite kept back ride on the submission for the
    /// settled answer's own line to explain.
    /// </summary>
    [Fact]
    public void AnExplicitOverAskFillsTheRoomAndCarriesWhatItWithheld()
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
            _ => true);

        var result = adapter.TryExecuteGameMcp(
            new AutoBuyCycleAction(
                AutoBuyCandidateKind.Upgrade, Guid.NewGuid(), PlannedEpoch, count: 1000),
            Configuration(),
            ActionContext());

        Assert.True(purchases.Submitted);
        Assert.Equal(3, purchases.LastCount);
        Assert.Equal(997, adapter.LastSubmission.WithheldBySuite);
        Assert.Equal(-1, adapter.LastSubmission.MaximumAmount);
    }

    /// <summary>
    /// The reserve is Auto Buy's courtesy to the player — slots kept free FOR manual actions — so
    /// charging it to the manual verb refused the very action it exists to protect.
    /// </summary>
    [Fact]
    public void TheManualVerbIgnoresTheQueueReserveTheAutomatedCycleKeeps()
    {
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(new CapturingMeasurementPort());
        var operations = new AutomataProfileOperations(probe);
        global::ActionManager.RemainingRoom = 3;

        var configuration = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoBuy = new AutoBuyConfiguration
            {
                Mode = AutoBuyOperationMode.Active,
                IncludeStructures = true,
                IncludeUpgrades = true,
                LeaveQueueSlots = 2,
            },
        };

        var manualPurchases = new RecordingCountPort();
        var manual = new AutoBuyCycleActionAdapter(
            manualPurchases,
            new AutoBuyNativeQueueRoomAdapter(),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            operations,
            IgnoreRefusals.Instance,
            _ => true);

        manual.TryExecuteGameMcp(
            new AutoBuyCycleAction(
                AutoBuyCandidateKind.Upgrade, Guid.NewGuid(), PlannedEpoch, count: 3),
            configuration,
            ActionContext());

        Assert.True(manualPurchases.Submitted);
        Assert.Equal(3, manualPurchases.LastCount);
        Assert.Equal(0, manual.LastSubmission.WithheldBySuite);

        var automatedPurchases = new RecordingCountPort();
        var automated = new AutoBuyCycleActionAdapter(
            automatedPurchases,
            new AutoBuyNativeQueueRoomAdapter(),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            operations,
            IgnoreRefusals.Instance);

        automated.TryExecute(
            new AutoBuyCycleAction(
                AutoBuyCandidateKind.Upgrade, Guid.NewGuid(), PlannedEpoch, count: 3),
            configuration,
            ActionContext());

        Assert.True(automatedPurchases.Submitted);
        Assert.Equal(1, automatedPurchases.LastCount);
    }

    /// <summary>
    /// The planner's count is clamped too, and silently: a plan that asks for what it hoped for and
    /// takes what fits is the plan working, and nobody is waiting on an answer to it. Both counts
    /// now fill the room — what still separates them is who is owed an explanation for the
    /// difference, and which of them the queue reserve applies to.
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
            return AutoBuyPurchaseSubmission.CandidateUnavailable();
        }
    }

    private sealed class RecordingRefusals : IAutoBuyRefusalResponsePort
    {
        internal System.Collections.Generic.List<AutoBuyRefusalReport> Reports { get; } = new();

        public ServiceActionResult Answer(in AutoBuyRefusalReport report, in ServiceActionResult refused)
        {
            Reports.Add(report);
            return refused;
        }
    }

    private sealed class IgnoreRefusals : IAutoBuyRefusalResponsePort
    {
        internal static IgnoreRefusals Instance { get; } = new();
        public ServiceActionResult Answer(in AutoBuyRefusalReport report, in ServiceActionResult refused) =>
            refused;
    }

    private static void AssertStage(
        in CapturedMeasurement item,
        ServiceCycleProfileSpan span,
        uint stableIdReads = 0,
        uint listEntries = 0)
    {
        Assert.Equal((int)span, item.Context.StageCode);
        Assert.Equal(1, item.Context.ServiceOrdinal);
        Assert.Equal((ulong)41, item.Context.Frame);
        Assert.Equal(ServiceCycleProfileTemperature.Warm, item.Context.Temperature);
        Assert.Equal(stableIdReads, item.Operations.StableIdReads);
        Assert.Equal(listEntries, item.Operations.ListEntries);
    }

    // The game registers every entity in IdScriptableObject.RuntimeLookup as well as its own list,
    // and the purchase boundary resolves a candidate through that registry.
    private static void Publish(global::StructureSO structure)
    {
        global::StructureSO.All.Add(structure);
        global::IdScriptableObject.RuntimeLookup[structure.GetGuid()] = structure;
    }

    private static void Publish(global::UpgradeSO upgrade)
    {
        global::UpgradeSO.All.Add(upgrade);
        global::IdScriptableObject.RuntimeLookup[upgrade.GetGuid()] = upgrade;
    }

    private static void ResetNativeState()
    {
        OrbModding.GameStubs.GameStubState.Reset();
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
        NativeMultiBuyScope.ResetForTests();
        NativePurchaseViewAdmissionResolver.ResetProductionForTests();
    }
}
