using System;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems.Runtime.ServiceCycle;

public sealed class AutoItemsFeatureStatusProjectorTests
{
    [Fact]
    public void TargetFailureRetainsItsExactExplanationInFeatureHealth()
    {
        var health = new AutoItemsActionHealth();
        var submission = NativeActionOutcome.Reject(
            AutoItemsActionResultCodes.TargetUnavailable,
            "The live Scroll target selector found no valid structure target at level 4.");
        health.Observe(in submission);

        var status = AutoItemsFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: true,
            ownershipReason: string.Empty,
            bindingsAvailable: true,
            bindingFailure: string.Empty,
            cycleObserved: true,
            AutoItemsDecisionKind.Scroll,
            Guid.Empty,
            AutoItemsTemporaryQuarantineCause.None,
            health);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.TemporarySafetyBlock, status.Reason);
        Assert.Equal(submission.Reason, status.Summary);
    }

    [Fact]
    public void QuarantineIsAFeatureFaultWithTheExactMutationEvidence()
    {
        var health = new AutoItemsActionHealth();
        var submission = NativeActionOutcome.FaultBeforeCall(
            AutoItemsActionResultCodes.Quarantined,
            "Auto Items is quarantined for this lifecycle after item abc had no queue delta.");
        health.Observe(in submission);

        var status = AutoItemsFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: true,
            ownershipReason: string.Empty,
            bindingsAvailable: true,
            bindingFailure: string.Empty,
            cycleObserved: true,
            AutoItemsDecisionKind.Relic,
            Guid.Empty,
            AutoItemsTemporaryQuarantineCause.None,
            health);

        Assert.Equal(FeatureStatusState.Faulted, status.State);
        Assert.Equal(FeatureStatusReasonCode.MutationQuarantined, status.Reason);
        Assert.Equal(submission.Reason, status.Summary);
    }

    [Fact]
    public void TargetingRefusalIsATemporaryTargetingBlock()
    {
        var health = new AutoItemsActionHealth();
        var submission = NativeActionOutcome.Reject(
            AutoItemsActionResultCodes.TargetingInProgress,
            "TargetingManager.IsTargeting() reported an active native targeting interaction.");
        health.Observe(in submission);

        var status = AutoItemsFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: true,
            ownershipReason: string.Empty,
            bindingsAvailable: true,
            bindingFailure: string.Empty,
            cycleObserved: true,
            AutoItemsDecisionKind.Scroll,
            Guid.Empty,
            AutoItemsTemporaryQuarantineCause.None,
            health);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.TargetingInProgress, status.Reason);
        Assert.Equal(submission.Reason, status.Summary);
    }

    [Fact]
    public void TemporaryFollowUpQuarantineNamesTheExactItemAndCause()
    {
        var itemId = Guid.Parse("00000000-0000-0000-0000-000000000777");

        var status = AutoItemsFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: true,
            ownershipReason: string.Empty,
            bindingsAvailable: true,
            bindingFailure: string.Empty,
            cycleObserved: true,
            AutoItemsDecisionKind.TemporaryItemQuarantined,
            itemId,
            AutoItemsTemporaryQuarantineCause.MultipleUsages,
            new AutoItemsActionHealth());

        Assert.Equal(FeatureStatusState.Faulted, status.State);
        Assert.Equal(FeatureStatusReasonCode.MutationQuarantined, status.Reason);
        Assert.Contains(itemId.ToString("D"), status.Summary);
        Assert.Contains("more than one native usage", status.Summary);
    }
}
