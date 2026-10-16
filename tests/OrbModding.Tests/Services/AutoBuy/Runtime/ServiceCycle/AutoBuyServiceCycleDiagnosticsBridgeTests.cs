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
            status);

        Assert.True(status.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.NotReady, status.Current.State);

        bridge.ObserveConfiguration(new ConfigGeneration(2));

        Assert.True(status.Current.ConfiguredEnabled);
        Assert.NotEqual(FeatureStatusState.ConfigurationDisabled, status.Current.State);
    }

    /// <summary>
    /// The refusal responder writes the mode off and publishes why. The configuration publication one
    /// frame later must carry that account forward rather than flatten it to "disabled by
    /// configuration".
    /// </summary>
    [Fact]
    public void ARefusalStandDownSurvivesTheConfigurationPublicationItCaused()
    {
        const string summary =
            "Auto Buy planned a purchase the game would not take. Auto Buy disabled itself.";
        var registry = new FeatureStatusRegistry();
        using var status = Reporter(registry);
        var bridge = new AutoBuyServiceCycleDiagnosticsBridge(
            Lifecycle,
            new ConfigGeneration(1),
            AutoBuyCandidateKinds.All,
            status);

        status.Observe(
            false,
            FeatureStatusState.ConfigurationDisabled,
            FeatureStatusReasonCode.InvariantViolation,
            summary);
        bridge.ObserveConfiguration(new ConfigGeneration(2));

        Assert.Equal(FeatureStatusState.ConfigurationDisabled, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.InvariantViolation, status.Current.Reason.Code);
        Assert.Equal(summary, status.Current.Reason.Summary);
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
            status);

        Assert.True(status.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.ActionFamilyConflict, status.Current.Reason.Code);
    }

    /// <summary>The cycle's own evidence, not the configuration, is what turns waiting into running.</summary>
    [Fact]
    public void AnEvaluatedCycleReportsOperational()
    {
        var configuration = Configuration(active: true);
        var definition = AutoBuyService.Define(new CommittingActions());
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
            status);

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
            destinationFull: 0);
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
            destinationFull: 0);
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
