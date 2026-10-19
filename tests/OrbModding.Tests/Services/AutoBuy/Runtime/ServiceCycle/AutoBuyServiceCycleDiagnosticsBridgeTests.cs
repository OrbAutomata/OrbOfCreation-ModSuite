using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

public sealed class AutoBuyServiceCycleDiagnosticsBridgeTests
{
    private const long Lifecycle = 7;

    /// <summary>
    /// Saved intent belongs to the central configuration projection. A service bridge may refresh
    /// runtime health, but observing another configuration cannot rewrite that intent.
    /// </summary>
    [Fact]
    public void ConfigurationGenerationResetCannotMoveConfiguredIntent()
    {
        var registry = new FeatureStatusRegistry();
        using var status = Reporter(registry);
        var bridge = new AutoBuyServiceCycleDiagnosticsBridge(
            Lifecycle,
            new ConfigGeneration(1),
            AutoBuyCandidateKinds.All,
            status,
            new AutoBuyInvariantLatch(),
            static _ => { });

        Assert.True(status.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.NotReady, status.Current.State);

        bridge.ObserveConfiguration(new ConfigGeneration(2));

        Assert.True(status.Current.ConfiguredEnabled);
        Assert.NotEqual(FeatureStatusState.ConfigurationDisabled, status.Current.State);
    }

    /// <summary>
    /// The invariant holds Auto Buy back without touching the saved mode: the feature stays configured
    /// on, reports the fault through a configuration publication, and the next lifecycle clears it.
    /// </summary>
    [Fact]
    public void ATrippedInvariantFaultsTheFeatureUntilTheNextLifecycle()
    {
        const string summary = "Auto Buy planned a purchase the game would not take.";
        var registry = new FeatureStatusRegistry();
        using var status = Reporter(registry);
        var latch = new AutoBuyInvariantLatch();
        latch.TryTrip(summary);
        var bridge = new AutoBuyServiceCycleDiagnosticsBridge(
            Lifecycle,
            new ConfigGeneration(1),
            AutoBuyCandidateKinds.All,
            status,
            latch,
            static _ => { });

        bridge.ObserveConfiguration(new ConfigGeneration(2));
        var tripped = status.Current;
        latch.Clear();
        bridge.ObserveLifecycle(Lifecycle + 1, new ConfigGeneration(2), AutoBuyCandidateKinds.All);

        Assert.True(tripped.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.Faulted, tripped.State);
        Assert.Equal(FeatureStatusReasonCode.InvariantViolation, tripped.Reason.Code);
        Assert.Equal(summary, tripped.Reason.Summary);
        Assert.Equal(FeatureStatusState.NotReady, status.Current.State);
    }

    [Fact]
    public void LosingPurchaseOwnershipBlocksAnEnabledFeature()
    {
        var registry = new FeatureStatusRegistry();
        using var status = Reporter(registry);
        var bridge = new AutoBuyServiceCycleDiagnosticsBridge(
            Lifecycle,
            new ConfigGeneration(1),
            AutoBuyCandidateKinds.None,
            status,
            new AutoBuyInvariantLatch(),
            static _ => { });

        Assert.True(status.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.ActionFamilyConflict, status.Current.Reason.Code);
    }

    /// <summary>The cycle's own evidence, not the configuration, is what turns waiting into running.</summary>
    [Fact]
    public void AnEvaluatedCycleReportsOperational()
    {
        var configuration = Configuration(active: true);
        var definition = AutoBuyService.Define(new CommittingActions(), new AutoBuyInvariantLatch());
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1));
        registry.ConfigurationPublication.Publish(configuration);
        using var registration = registry.Register(definition);
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var featureRegistry = new FeatureStatusRegistry();
        using var status = Reporter(featureRegistry, lifecycle: 1);
        var bridge = new AutoBuyServiceCycleDiagnosticsBridge(
            1,
            new ConfigGeneration(1),
            AutoBuyCandidateKinds.All,
            status,
            new AutoBuyInvariantLatch(),
            static _ => { });

        Assert.Equal(FeatureStatusState.NotReady, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.Initializing, status.Current.Reason.Code);

        ServiceRunnerTestWait.ForWorkerReady(registration);
        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        ServiceRunnerTestWait.ForResponse(registration);
        var response = pump.PumpFrame(2);
        bridge.Observe(pump, in response, AutoBuyCandidateKinds.All);
        var quiet = pump.PumpFrame(3);
        bridge.Observe(pump, in quiet, AutoBuyCandidateKinds.All);

        Assert.Equal(FeatureStatusState.Operational, status.Current.State);

        bridge.ObserveLifecycle(
            2,
            new ConfigGeneration(1),
            AutoBuyCandidateKinds.All);

        Assert.Equal(FeatureStatusState.NotReady, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.Initializing, status.Current.Reason.Code);
    }

    [Theory]
    [InlineData(2, 0, 0, 0, (int)AutoBuyDecisionBlockReason.OwningViewUnavailable)]
    [InlineData(0, 2, 0, 0, (int)AutoBuyDecisionBlockReason.OwningViewRelationMissing)]
    [InlineData(0, 0, 2, 0, (int)AutoBuyDecisionBlockReason.OwningViewRelationUnreadable)]
    [InlineData(0, 0, 0, 2, (int)AutoBuyDecisionBlockReason.OwningViewRelationContradictory)]
    public void AUniformTotalRelationExclusionBecomesTheExactRuntimeBlock(
        int unavailable,
        int missing,
        int unreadable,
        int contradictory,
        int expected)
    {
        var exclusions = new AutoBuyExclusionHistogram(
            kindNotSelected: 0,
            unavailable: 0,
            requirementsUnmet: 0,
            terminal: 0,
            unaffordable: 0,
            unpriceable: 0,
            owningViewUnavailable: unavailable,
            owningViewRelationMissing: missing,
            owningViewRelationUnreadable: unreadable,
            owningViewRelationContradictory: contradictory,
            destinationFull: 0,
            backingOff: 0);
        var state = AutoBuyCycleState.Create(new LifecycleGeneration(1));
        state.RecordDecision(new AutoBuyDecisionMetrics(2, 0, 0, 0, 0, in exclusions));

        Assert.Equal(
            (AutoBuyDecisionBlockReason)expected,
            AutoBuyServiceCycleDiagnosticsBridge.TotalRelationBlock(Projection(in state)));
    }

    [Fact]
    public void MixedRelationExclusionsStillCannotReportOperational()
    {
        var exclusions = new AutoBuyExclusionHistogram(
            kindNotSelected: 0,
            unavailable: 0,
            requirementsUnmet: 0,
            terminal: 0,
            unaffordable: 0,
            unpriceable: 0,
            owningViewUnavailable: 1,
            owningViewRelationMissing: 1,
            owningViewRelationUnreadable: 0,
            owningViewRelationContradictory: 0,
            destinationFull: 0,
            backingOff: 0);
        var state = AutoBuyCycleState.Create(new LifecycleGeneration(1));
        state.RecordDecision(new AutoBuyDecisionMetrics(2, 0, 0, 0, 0, in exclusions));

        Assert.Equal(
            AutoBuyDecisionBlockReason.MixedPurchaseViewRelations,
            AutoBuyServiceCycleDiagnosticsBridge.TotalRelationBlock(Projection(in state)));
    }

    private static ServiceStateProjectionSnapshot Projection(in AutoBuyCycleState state)
    {
        var buffer = new ServiceStateProjectionWriteBuffer(
            ServiceStateProjectionSnapshot.MaximumEntryCount);
        var builder = new ServiceStateProjectionBuilder(buffer);
        AutoBuyServiceProjection.Write(in state, builder);
        return builder.CaptureSnapshot();
    }

    private static AutomataFeatureStatusReporter Reporter(
        FeatureStatusRegistry registry,
        long lifecycle = Lifecycle) =>
        new(
            registry,
            new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoBuyFeatureId),
                "Auto Buy",
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(
                    FeatureStatusReasonCode.GameplayNotReady,
                    "Gameplay lifecycle is not ready."),
                lifecycle));

    private static SuiteRuntimeConfiguration Configuration(bool active) =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoBuy = new AutoBuyConfiguration
            {
                Mode = active ? AutoBuyOperationMode.Active : AutoBuyOperationMode.Disabled,
                IncludeStructures = true,
                IncludeUpgrades = true,
            },
        };

    private sealed class CommittingActions : IAutoBuyCycleActionPort
    {
        public ServiceActionResult TryExecute(
            in AutoBuyCycleAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context) =>
            ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.Verified,
                    new NativeMutationCallOutcome(1, 1, 1)));
    }
}
