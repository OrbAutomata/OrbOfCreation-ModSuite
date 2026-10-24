using System;
using System.Diagnostics;
using System.Threading;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;
using OrbModding.Common;
using OrbModding.TestSupport;
using OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Runtime.ServiceCycle;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AutoHarvestAllocationMeasurementCollection
{
    public const string Name = "Auto Harvest allocation measurement";
}

[Collection(AutoHarvestAllocationMeasurementCollection.Name)]
[Trait("Category", "PerformanceSimulation")]
public sealed class AutoHarvestServiceCyclePerformanceTests
{
    private const int MeasuredCycles = 64;
    private const long MaximumBytesPerCycle = 64;
    private const long NoGcRegionSize = 16 * 1024 * 1024;
    private static readonly MonotonicDuration Interval =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1));

    [Fact]
    public void WarmedFeatureCyclesStayWithinReviewedOwnerAndWorkerAllocationCeilings()
    {
        var clock = new ThreadSafeTestClock(100);
        var actions = new CommittingActions();
        var definition = AutoHarvestService.Define(actions);
        var world = AutoHarvestTestWorlds.Harvestable();
        using var registry = new ServiceCycleRegistry(
            1,
            clock,
            measureWorkerAllocations: true);
        registry.ConfigurationPublication.Publish(Configuration());
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(7));
        registry.Seal();
        var pump = new SuiteFramePump(registry);
        using var storage = new DecisionJournalRuntimeTestStorage();
        var journalStatus = new DecisionJournalStatusRegistry();
        var journalOptions = new AutomataDecisionJournalOptions(
            journalStatus,
            new JournalSource(storage),
            "journal");
        var journal = Assert.IsType<AutomataDecisionJournalController>(
            AutomataDecisionJournalController.TryCreate(
                pump,
                in journalOptions,
                new ManualLogSource()));
        using var featureStatus = new AutomataFeatureStatusReporter(
            new FeatureStatusRegistry(),
            new FeatureStatusSnapshot(
                new FeatureStatusKey(
                    PluginIds.SuiteGuid,
                    AutomataFeatureStatuses.AutoHarvestFeatureId),
                "Auto Harvest",
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(
                    FeatureStatusReasonCode.RegistryNotReady,
                    "waiting"),
                lifecycleGeneration: 7));
        using var diagnostics = new AutoHarvestServiceCycleDiagnosticsBridge(
            7,
            new ConfigGeneration(1),
            ownsActionFamily: true,
            runtimeDiagnostics: new RuntimeDiagnosticsRegistry(),
            featureStatus);
        // Past the world publication's own seed generation, as a real frame counter always is.
        var frame = 1L;

        try
        {
            Assert.True(SpinWait.SpinUntil(() =>
            {
                journal.Tick();
                return journalStatus.Status.State == DecisionJournalStatusState.Recording;
            }, ServiceCycleTestDeadline.Value));
            Assert.NotEqual(Environment.CurrentManagedThreadId, storage.ReconcileThreadId);
            // Armed only once the journal is recording, so warm-up latency cannot eat the measured window.
            var deadline = new ServiceCycleTestDeadline.Budget();
            RunCycle(pump, registry, world, journal, diagnostics, registration, clock, ref frame, deadline, out _);
            // A collection can charge this thread its unused allocation quantum inside the probe.
            Assert.True(GC.TryStartNoGCRegion(NoGcRegionSize, disallowFullBlockingGC: true));
            try
            {
                var workerBefore = registration.Runner.Snapshot;
                long ownerAllocated = 0;
                long workerAllocated = 0;
                for (var index = 0; index < MeasuredCycles; index++)
                {
                    var cycleOwnerAllocated = RunCycle(
                        pump,
                        registry,
                        world,
                        journal,
                        diagnostics,
                        registration,
                        clock,
                        ref frame,
                        deadline,
                        out var cycleWorkerAllocated);
                    Assert.InRange(cycleOwnerAllocated, 0, MaximumBytesPerCycle);
                    Assert.InRange(cycleWorkerAllocated, 0, MaximumBytesPerCycle);
                    ownerAllocated += cycleOwnerAllocated;
                    workerAllocated += cycleWorkerAllocated;
                }
                var workerAfter = registration.Runner.Snapshot;

                var workerCycles = workerAfter.MeasuredWorkerCycleCount - workerBefore.MeasuredWorkerCycleCount;
                Assert.Equal(MeasuredCycles, workerCycles);
                Assert.Equal(MeasuredCycles + 1, actions.ExecutionCount);
                Assert.InRange(ownerAllocated, 0, MeasuredCycles * MaximumBytesPerCycle);
                Assert.InRange(workerAllocated, 0, MeasuredCycles * MaximumBytesPerCycle);
            }
            finally
            {
                GC.EndNoGCRegion();
            }
        }
        finally
        {
            journal.DisposeWithPump();
        }
    }

    private static long RunCycle(
        SuiteFramePump pump,
        ServiceCycleRegistry registry,
        GameWorldState world,
        AutomataDecisionJournalController journal,
        AutoHarvestServiceCycleDiagnosticsBridge diagnostics,
        ServiceRegistration<
            AutoHarvestCycleState,
            AutoHarvestCycleAction> registration,
        ThreadSafeTestClock clock,
        ref long frame,
        ServiceCycleTestDeadline.Budget deadline,
        out long workerAllocated)
    {
        clock.Advance(Interval);
        // Collection runs every frame in the game, and the previous cycle's action means this one
        // does not start until a reading later than it arrives.
        TestWorldCollector.CollectedAt(registry, frame + 1, world);
        var expectedWorkerCycles = registration.Runner.Snapshot.MeasuredWorkerCycleCount + 1;
        var allocated = MeasurePump(pump, journal, diagnostics, ref frame, out var capture);
        Assert.Equal(1, capture.CyclesStarted);
        Assert.True(registration.WaitForResponseReady(deadline.Remaining));
        deadline.For(() => registration.Runner.Snapshot.MeasuredWorkerCycleCount >= expectedWorkerCycles, "Auto Harvest allocation publication");
        workerAllocated = registration.Runner.Snapshot.WorkerCycleAllocatedBytes;
        allocated += MeasurePump(pump, journal, diagnostics, ref frame, out var response);
        allocated += MeasurePump(pump, journal, diagnostics, ref frame, out var action);
        allocated += MeasurePump(pump, journal, diagnostics, ref frame, out var handback);
        deadline.For(() => !registration.Runner.Snapshot.Handoff.CleanupPending, "Auto Harvest worker cleanup");

        Assert.Equal(1, response.ResponsesAcquired);
        Assert.Equal(1, action.ActionsAttempted);
        Assert.Equal(0, handback.CyclesStarted);
        return allocated;
    }

    private static long MeasurePump(
        SuiteFramePump pump,
        AutomataDecisionJournalController journal,
        AutoHarvestServiceCycleDiagnosticsBridge diagnostics,
        ref long frame,
        out SuiteFramePumpReport report)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        journal.Tick();
        report = pump.PumpFrame(++frame);
        diagnostics.Observe(pump, in report, ownsActionFamily: true);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }


    private static SuiteRuntimeConfiguration Configuration() => AutoHarvestConfigurationFactory.Create(
        masterEnabled: true,
        emergencyDisabled: false,
        activeMode: true,
        fruitSelected: true,
        treasureSelected: true);

    private sealed class JournalSource : IAutomataDecisionJournalSource
    {
        private readonly DecisionJournalRuntimeTestStorage _storage;

        internal JournalSource(DecisionJournalRuntimeTestStorage storage) => _storage = storage;

        public AutomataDecisionJournalSpec Create() => new(
            _storage,
            new DecisionJournalRunId(1),
            maximumCommittedSegments: 4,
            blockCount: 10,
            new MonotonicDuration(long.MaxValue));
    }

    private sealed class CommittingActions : IAutoHarvestCycleActionPort
    {
        public int ExecutionCount { get; private set; }

        public ServiceActionResult TryExecute(
            in AutoHarvestCycleAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context)
        {
            ExecutionCount++;
            return ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.Verified,
                    new NativeMutationCallOutcome(1, 1, 1)));
        }
    }
}
