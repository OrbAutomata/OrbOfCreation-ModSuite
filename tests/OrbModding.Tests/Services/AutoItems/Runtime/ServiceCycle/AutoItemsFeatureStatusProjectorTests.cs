using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
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
            health,
            ServiceRetryGateSnapshot.Empty);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.TemporarySafetyBlock, status.Reason);
        Assert.Equal(submission.Reason, status.Summary);
    }

    [Fact]
    public void AnItemBackingOffDegradesTheFeatureWithItsRetryReason()
    {
        var itemId = Guid.NewGuid();
        var gate = new ServiceTargetRetryGate(AutoItemsServicePolicies.FaultRecoveryPolicy);
        gate.Record(
            AutoItemsServicePolicies.RetryTarget(itemId),
            "Relic did not enter native preparation.",
            new MonotonicTimestamp(10));
        var health = new AutoItemsActionHealth();
        var fault = NativeActionOutcome.Fault(
            AutoItemsActionResultCodes.UseNotVerified,
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(1, 1, 0),
            "Relic did not enter native preparation.");
        health.Observe(in fault);

        var status = AutoItemsFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: true,
            ownershipReason: string.Empty,
            bindingsAvailable: true,
            bindingFailure: string.Empty,
            cycleObserved: true,
            AutoItemsDecisionKind.Relic,
            health,
            gate.Snapshot);

        Assert.False(health.HasFailure);
        Assert.Equal(FeatureStatusState.Degraded, status.State);
        Assert.Equal(FeatureStatusReasonCode.NativeMutationFailed, status.Reason);
        Assert.Contains(itemId.ToString("D"), status.Summary);
        Assert.Contains("Relic did not enter native preparation.", status.Summary);
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
            health,
            ServiceRetryGateSnapshot.Empty);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.TargetingInProgress, status.Reason);
        Assert.Equal(submission.Reason, status.Summary);
    }
}
