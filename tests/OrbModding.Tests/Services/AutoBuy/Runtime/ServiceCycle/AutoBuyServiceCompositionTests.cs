using System;
using System.Threading;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using OrbModding.Tests.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

/// <summary>
/// End-to-end wiring proof for Auto Buy's ServiceCycle composition: the shared pump drives the
/// typed definition (AB-SC-014).
/// </summary>
/// <remarks>
/// What runs where is the claim, and where the world comes from. The service is handed no world
/// source at all: the registry owns the one publication, the runtime pins it, and the worker's
/// projection turns it into candidates. Everything the service itself does — deciding to start and
/// carrying out purchases — stays on the main thread. See W50.
/// </remarks>
public sealed class AutoBuyServiceCompositionTests
{
    [Fact]
    public void DefinitionAttributesTheExactCandidateTypeListAndView()
    {
        var candidate = Guid.NewGuid();
        var list = Guid.NewGuid();
        var view = Guid.NewGuid();
        var definition = AutoBuyService.Define(new ActionPort(Thread.CurrentThread.ManagedThreadId));
        var action = new AutoBuyCycleAction(
            AutoBuyCandidateKind.Structure,
            candidate,
            collectedAtEpoch: 1,
            count: 1,
            belief: default,
            owningListId: list,
            owningViewId: view);

        var attribution = definition.DescribeAction(in action);

        Assert.Equal(candidate, attribution.CandidateId);
        Assert.Equal(ServiceActionNativeTypeId.StructureSO, attribution.NativeType);
        Assert.Equal(list, attribution.ListId);
        Assert.Equal(view, attribution.ViewId);
        Assert.Equal(ServiceActionRouteStatus.Resolved, attribution.RouteStatus);
    }

    [Fact]
    public void CommonPumpExecutesThePlainServiceThroughMainThreadPorts()
    {
        var ownerThread = Thread.CurrentThread.ManagedThreadId;
        var actions = new ActionPort(ownerThread);
        var definition = AutoBuyService.Define(actions);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(7));
        registry.ConfigurationPublication.Publish(Configuration());
        using var registration = registry.Register(definition);
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);

        ServiceRunnerTestWait.ForWorkerReady(registration);
        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        ServiceRunnerTestWait.ForResponse(registration);
        pump.PumpFrame(2);
        pump.PumpFrame(3);

        Assert.Equal(0, actions.ExecutionCount);
    }

    /// <summary>
    /// A world published into the registry reaches the worker's projection and becomes a purchase
    /// attempt, without the service ever being handed a publisher.
    /// </summary>
    /// <remarks>
    /// The composition compiles and pumps to completion against an empty world, so the test above
    /// cannot tell a wired world from an unwired one. This one can: nothing but the registry's
    /// publication can put a candidate in front of the action port.
    /// </remarks>
    [Fact]
    public void AWorldPublishedIntoTheRegistryReachesTheWorkersProjection()
    {
        var ownerThread = Thread.CurrentThread.ManagedThreadId;
        var actions = new ActionPort(ownerThread);
        var definition = AutoBuyService.Define(actions);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(7));
        registry.ConfigurationPublication.Publish(Configuration());
        using var registration = registry.Register(definition);
        // The one reading this composition ever gets, and the one that opens the gate: it is newer
        // than the seed the service was armed on, so the service may act, and it is the only world
        // that could have put a candidate in front of the action port.
        registry.WorldPublication.Publish(AffordableStructureWorld(), new WorldGeneration(2));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        ServiceRunnerTestWait.ForWorkerReady(registration);
        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        ServiceRunnerTestWait.ForResponse(registration);
        pump.PumpFrame(2);
        pump.PumpFrame(3);

        Assert.True(actions.ExecutionCount > 0);
        Assert.NotEqual(Guid.Empty, actions.LastCandidateId);
        Assert.NotEqual(Guid.Empty, actions.LastListId);
        Assert.NotEqual(Guid.Empty, actions.LastViewId);
    }

    [Fact]
    public void AffordabilitySkipWaitsForFreshWorldThenReplans()
    {
        var ownerThread = Thread.CurrentThread.ManagedThreadId;
        var actions = new SkipThenCommitActionPort(ownerThread);
        var definition = AutoBuyService.Define(actions);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(7));
        registry.ConfigurationPublication.Publish(Configuration());
        using var registration = registry.Register(definition);
        registry.WorldPublication.Publish(AffordableStructureWorld(), new WorldGeneration(2));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 10L;

        ServiceCyclePumpTestWait.PumpUntil(
            pump,
            ref frame,
            () => actions.ExecutionCount == 1);
        Assert.Equal(ServiceActionDisposition.Skipped, actions.FirstDisposition);

        for (var index = 0; index < 4; index++) pump.PumpFrame(frame++);
        Assert.Equal(1, actions.ExecutionCount);

        registry.WorldPublication.Publish(
            AffordableStructureWorld(),
            new WorldGeneration(checked((ulong)frame + 1)));
        ServiceCyclePumpTestWait.PumpUntil(
            pump,
            ref frame,
            () => actions.ExecutionCount == 2);

        Assert.Equal(ServiceActionDisposition.Committed, actions.LastDisposition);
    }

    /// <summary>One structure priced at one unit of a resource the player holds plenty of.</summary>
    private static GameWorldState AffordableStructureWorld()
    {
        var structureId = Guid.NewGuid();
        var structureTypeId = Guid.NewGuid();
        var listId = Guid.NewGuid();
        var viewId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var costs = new[] { new WorldPurchaseCost(structureId, resourceId, new BigDouble(1.0, 0)) };
        var relation = new WorldPurchaseViewRelation(
            structureId,
            WorldPurchaseCandidateKind.Structure,
            structureTypeId,
            1,
            WorldPurchaseViewRelationStatus.Resolved);
        return new GameWorldState
        {
            Structures = WorldTable.Create(
                new[]
                {
                    WorldStructureDeriver.Shared.Derive(
                        WorldSamples.Structure(structureId, structureTypeId: structureTypeId)),
                }),
            Views = WorldTable.Create(new[] { new WorldView(viewId, false, false, true) }),
            PurchaseViewRelations = PublicationTable<WorldPurchaseViewRelation>.Create(
                new[] { relation },
                1),
            PurchaseViewRoutes = PublicationTable<WorldPurchaseViewRoute>.Create(
                new[] { new WorldPurchaseViewRoute(structureId, listId, viewId) },
                1),
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
        new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoBuy = new AutoBuyConfiguration
            {
                Mode = AutoBuyOperationMode.Active,
                IncludeStructures = true,
                IncludeUpgrades = false,
            },
        };

    private sealed class ActionPort : IAutoBuyCycleActionPort
    {
        private readonly int _ownerThread;

        public ActionPort(int ownerThread) => _ownerThread = ownerThread;
        public int ExecutionCount { get; private set; }
        public Guid LastCandidateId { get; private set; }
        public Guid LastListId { get; private set; }
        public Guid LastViewId { get; private set; }

        public ServiceActionResult TryExecute(
            in AutoBuyCycleAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context)
        {
            Assert.Equal(_ownerThread, Thread.CurrentThread.ManagedThreadId);
            ExecutionCount++;
            LastCandidateId = action.Uuid;
            LastListId = action.OwningListId;
            LastViewId = action.OwningViewId;
            var call = new NativeMutationCallOutcome(1, 1, 1);
            return ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(NativeMutationOutcome.Verified, call));
        }
    }

    private sealed class SkipThenCommitActionPort : IAutoBuyCycleActionPort
    {
        private readonly int _ownerThread;

        internal SkipThenCommitActionPort(int ownerThread) => _ownerThread = ownerThread;

        internal int ExecutionCount { get; private set; }
        internal ServiceActionDisposition FirstDisposition { get; private set; }
        internal ServiceActionDisposition LastDisposition { get; private set; }

        public ServiceActionResult TryExecute(
            in AutoBuyCycleAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context)
        {
            Assert.Equal(_ownerThread, Thread.CurrentThread.ManagedThreadId);
            ExecutionCount++;
            var result = ExecutionCount == 1
                ? ServiceActionResult.Skipped(CommonActionResultCodes.Skipped)
                : ServiceActionResult.Committed(
                    CommonActionResultCodes.Committed,
                    ServiceNativeMutationEvidence.Observed(
                        NativeMutationOutcome.Verified,
                        new NativeMutationCallOutcome(1, 1, 1)));
            if (ExecutionCount == 1) FirstDisposition = result.Disposition;
            LastDisposition = result.Disposition;
            return result;
        }
    }
}
