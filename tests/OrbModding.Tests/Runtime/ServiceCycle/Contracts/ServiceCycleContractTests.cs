using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;
using RuntimeLifecycleGeneration = OrbModding.Common.Runtime.LifecycleGeneration;
using RuntimeStrategyGeneration = OrbModding.Common.Runtime.StrategyGeneration;

namespace OrbModding.Tests.Runtime.ServiceCycle.Contracts;

public sealed class ServiceCycleContractTests
{
    [Fact]
    public void IdentitiesAndGenerationsAreDistinctValidatedTypes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CycleId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BatchId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActionId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConfigGeneration(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureSequence(0));
        Assert.Throws<ArgumentException>(() => new ServiceId("  "));

        Assert.NotEqual(typeof(CycleId), typeof(BatchId));
        Assert.NotEqual(typeof(RuntimeLifecycleGeneration), typeof(ConfigGeneration));
        Assert.NotEqual(typeof(ConfigGeneration), typeof(RuntimeStrategyGeneration));
        Assert.Equal(new ConfigGeneration(2), new ConfigGeneration(1).Next());
        Assert.Equal(NewCycleIdentity(), NewCycleIdentity());
    }

    [Fact]
    public void WaitingAndUnavailableDecisionsRequireExplicitRetryPolicies()
    {
        var now = new MonotonicTimestamp(100);
        var past = new MonotonicTimestamp(99);
        var equal = new MonotonicTimestamp(100);
        var future = new MonotonicTimestamp(101);

        Assert.True(WakePolicy.Default.IsValid);
        Assert.True(WakePolicy.Immediate.IsValid);
        Assert.True(ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready).IsValid);
        Assert.True(ServiceStartDecision.Wait(
            CommonServiceDecisionCodes.NotReady,
            WakePolicy.AfterDecision(MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(10)))).IsValid);
        Assert.Throws<ArgumentException>(() => ServiceStartDecision.Wait(
            CommonServiceDecisionCodes.NotReady,
            WakePolicy.Immediate));
        Assert.Throws<ArgumentException>(() => ServiceCaptureResult.Unavailable(
            CommonServiceDecisionCodes.CaptureUnavailable,
            WakePolicy.Default));
        Assert.Throws<ArgumentException>(() => ServiceStartDecision.Wait(
            CommonServiceDecisionCodes.NotReady,
            WakePolicy.At(future)));
        Assert.Throws<ArgumentException>(() => ServiceCaptureResult.Unavailable(
            CommonServiceDecisionCodes.CaptureUnavailable,
            WakePolicy.At(future)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ServiceStartDecision.WaitUntil(
            CommonServiceDecisionCodes.NotReady, past, now));
        Assert.Throws<ArgumentOutOfRangeException>(() => ServiceStartDecision.WaitUntil(
            CommonServiceDecisionCodes.NotReady, equal, now));
        Assert.Throws<ArgumentOutOfRangeException>(() => ServiceCaptureResult.UnavailableUntil(
            CommonServiceDecisionCodes.CaptureUnavailable, past, now));
        Assert.Throws<ArgumentOutOfRangeException>(() => ServiceCaptureResult.UnavailableUntil(
            CommonServiceDecisionCodes.CaptureUnavailable, equal, now));
        Assert.True(ServiceStartDecision.WaitUntil(
            CommonServiceDecisionCodes.NotReady, future, now).IsValid);
        Assert.True(ServiceCaptureResult.UnavailableUntil(
            CommonServiceDecisionCodes.CaptureUnavailable, future, now).IsValid);
    }

    [Fact]
    public void ReservedCodeRangesCannotBeCollidedWithOrForgedAcrossOutcomes()
    {
        var featureDecision = new ServiceDecisionCode(ServiceDecisionCode.FirstFeatureCode);
        var featureAction = new ServiceActionResultCode(ServiceActionResultCode.FirstFeatureCode, "feature_code");
        var verified = ServiceNativeMutationEvidence.Observed(
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1));

        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceDecisionCode(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceDecisionCode(1023));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceActionResultCode(7, "low"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceActionResultCode(1023, "low"));
        Assert.Throws<ArgumentException>(() => new ServiceActionResultCode(1024, " "));

        Assert.True(ServiceStartDecision.Ready(featureDecision).IsValid);
        Assert.True(ServiceActionResult.Committed(featureAction, verified).IsValid);

        Assert.Throws<ArgumentException>(() => ServiceStartDecision.Ready(
            CommonServiceDecisionCodes.NotReady));
        Assert.Throws<ArgumentException>(() => ServiceStartDecision.Wait(
            CommonServiceDecisionCodes.Ready,
            WakePolicy.AfterDecision(MonotonicDuration.FromTimeSpan(TimeSpan.FromTicks(1)))));
        Assert.Throws<ArgumentException>(() => ServiceCaptureResult.Captured(
            CommonServiceDecisionCodes.CaptureUnavailable));
        Assert.Throws<ArgumentException>(() => ServiceCaptureResult.Unavailable(
            CommonServiceDecisionCodes.Captured,
            WakePolicy.AfterDecision(MonotonicDuration.FromTimeSpan(TimeSpan.FromTicks(1)))));

        Assert.Throws<ArgumentException>(() => ServiceActionResult.Committed(
            CommonActionResultCodes.AdapterFault,
            verified));
        Assert.Throws<ArgumentException>(() => ServiceActionResult.Rejected(
            CommonActionResultCodes.Committed));
        Assert.Throws<ArgumentException>(() => ServiceActionResult.Faulted(
            CommonActionResultCodes.NativeRejected));
        Assert.Throws<ArgumentException>(() => new ServiceFault(
            ServiceFaultCategory.ActionExecution,
            CommonActionResultCodes.PolicyRejected,
            1,
            new MonotonicTimestamp(1)));
    }

    [Fact]
    public void ActionResultsDistinguishNoNativeRejectionFromObservedFaults()
    {
        var verified = ServiceNativeMutationEvidence.Observed(
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1));
        var failed = ServiceNativeMutationEvidence.Observed(
            NativeMutationOutcome.ExecutionThrew,
            new NativeMutationCallOutcome(1, 1, 0));
        var committed = ServiceActionResult.Committed(CommonActionResultCodes.Committed, verified);
        var rejected = ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected);
        var faulted = ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault, failed);
        var skippedEvidence = ServiceNativeMutationEvidence.Observed(
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(1, 1, 0));
        var skipped = ServiceActionResult.Skipped(CommonActionResultCodes.Skipped, skippedEvidence);
        var preNativeSkipped = ServiceActionResult.Skipped(CommonActionResultCodes.Skipped);
        var preNativeFault = ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);

        Assert.True(committed.IsValid);
        Assert.True(skipped.IsValid);
        Assert.True(preNativeSkipped.IsValid);
        Assert.False(preNativeSkipped.HasNativeEvidence);
        Assert.Equal(ServiceActionEffect.None, preNativeSkipped.Effect);
        Assert.Equal(ServiceActionDisposition.Skipped, skipped.Disposition);
        Assert.False(rejected.HasNativeEvidence);
        Assert.Equal(ServiceActionDisposition.Rejected, rejected.Disposition);
        Assert.Equal(1, faulted.NativeCallOutcome.MutationAttempts);
        Assert.False(preNativeFault.HasNativeEvidence);
        Assert.Throws<ArgumentException>(() => ServiceActionResult.Committed(
            CommonActionResultCodes.Committed,
            failed));
        Assert.Throws<ArgumentException>(() => ServiceActionResult.Faulted(
            CommonActionResultCodes.AdapterFault,
            verified));
        Assert.Throws<ArgumentException>(() => ServiceActionResult.Skipped(
            CommonActionResultCodes.Skipped,
            verified));
        Assert.Throws<ArgumentException>(() => ServiceNativeMutationEvidence.Observed(
            NativeMutationOutcome.ExecutionThrew,
            default));
        Assert.Throws<ArgumentException>(() => ServiceNativeMutationEvidence.Observed(
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(0, 1, 0)));
    }

    [Fact]
    public void TerminalReceiptsPreserveExactCursorSuffixAndNativeFacts()
    {
        var cycle = NewCycleIdentity();
        var rejected = ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected);
        var aggregate = new NativeMutationCallOutcome(1, 1, 1);

        var terminated = BatchReceipt.Terminated(
            cycle, new BatchId(1), actionCount: 3, committedCount: 1, terminalIndex: 1,
            rejected, aggregate, new MonotonicTimestamp(10));
        var orphaned = BatchReceipt.Orphaned(
            cycle, new BatchId(2), actionCount: 5, committedCount: 2,
            new NativeMutationCallOutcome(2, 2, 2), new MonotonicTimestamp(20));
        var empty = BatchReceipt.Completed(
            cycle, new BatchId(3), actionCount: 0, default, new MonotonicTimestamp(30));

        Assert.Equal(1, terminated.UntouchedSuffixCount);
        Assert.Equal(CommonActionResultCodes.NativeRejected, terminated.ResultCode);
        Assert.Equal(1, terminated.NativeCallOutcome.MutationsCommitted);
        Assert.Equal(3, orphaned.UntouchedSuffixCount);
        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, orphaned.ResultCode);
        Assert.Equal(0, empty.ActionCount);
        var skippedPrefix = BatchReceipt.Terminated(
            cycle, new BatchId(4), actionCount: 3, committedCount: 0, terminalIndex: 1,
            rejected, new NativeMutationCallOutcome(1, 1, 0), new MonotonicTimestamp(40));
        Assert.Equal(1, skippedPrefix.SkippedCount);
        var skippedAction = ServiceActionResult.Skipped(
            CommonActionResultCodes.Skipped,
            ServiceNativeMutationEvidence.Observed(
                NativeMutationOutcome.PostconditionFailed,
                new NativeMutationCallOutcome(1, 1, 0)));
        Assert.Throws<ArgumentException>(() => BatchReceipt.Terminated(
            cycle, new BatchId(12), actionCount: 1, committedCount: 0, terminalIndex: 0,
            skippedAction, new NativeMutationCallOutcome(1, 1, 0), new MonotonicTimestamp(45)));
        Assert.Throws<ArgumentException>(() => BatchReceipt.Terminated(
            cycle, new BatchId(5), actionCount: 3, committedCount: 1, terminalIndex: 1,
            rejected, default, new MonotonicTimestamp(50)));
        Assert.Throws<ArgumentException>(() => BatchReceipt.Completed(
            cycle, new BatchId(6), actionCount: 2,
            new NativeMutationCallOutcome(1, 1, 1), new MonotonicTimestamp(60)));
        Assert.Throws<ArgumentException>(() => BatchReceipt.Completed(
            cycle, new BatchId(7), actionCount: 0,
            new NativeMutationCallOutcome(1, 0, 0), new MonotonicTimestamp(70)));
        Assert.Throws<ArgumentException>(() => BatchReceipt.Orphaned(
            cycle, new BatchId(8), actionCount: 5, committedCount: 0,
            new NativeMutationCallOutcome(1, 0, 0), new MonotonicTimestamp(80)));

        var terminalFault = ServiceActionResult.Faulted(
            CommonActionResultCodes.AdapterFault,
            ServiceNativeMutationEvidence.Observed(
                NativeMutationOutcome.PostconditionFailed,
                new NativeMutationCallOutcome(1, 1, 0)));
        Assert.Throws<ArgumentException>(() => BatchReceipt.Terminated(
            cycle, new BatchId(9), actionCount: 3, committedCount: 1, terminalIndex: 1,
            terminalFault, new NativeMutationCallOutcome(1, 1, 1), new MonotonicTimestamp(90)));

        var emergencyAction = ServiceActionResult.Rejected(CommonActionResultCodes.EmergencyStop);
        var emergency = new EmergencyStopContext(
            new EmergencyStopEpisodeId(3),
            new EmergencyStopTransitionGeneration(5),
            EmergencyStopReason.SafetyInterlock);
        Assert.Throws<ArgumentException>(() => BatchReceipt.Terminated(
            cycle, new BatchId(10), actionCount: 1, committedCount: 0, terminalIndex: 0,
            emergencyAction, default, new MonotonicTimestamp(100)));
        Assert.Throws<ArgumentException>(() => BatchReceipt.Terminated(
            cycle, new BatchId(11), actionCount: 1, committedCount: 0, terminalIndex: 0,
            rejected, default, new MonotonicTimestamp(110), emergency));
        var emergencyReceipt = BatchReceipt.Terminated(
            cycle, new BatchId(12), actionCount: 1, committedCount: 0, terminalIndex: 0,
            emergencyAction, default, new MonotonicTimestamp(120), emergency);
        Assert.Equal(emergency, emergencyReceipt.EmergencyStop);
        Assert.True(emergencyReceipt.HasEmergencyStopContext);
    }

    [Fact]
    public void CompletedReceiptRejectsPartialCommitsWithoutNativeEvidence()
    {
        var cycle = NewCycleIdentity();

        Assert.Throws<ArgumentException>(() => BatchReceipt.Completed(
            cycle,
            new BatchId(13),
            actionCount: 5,
            committedCount: 3,
            nativeCallOutcome: default,
            completedAt: new MonotonicTimestamp(130)));

        var publication = BatchReceipt.Completed(
            cycle,
            new BatchId(14),
            actionCount: 5,
            committedCount: 5,
            nativeCallOutcome: default,
            completedAt: new MonotonicTimestamp(140));
        Assert.Equal(5, publication.CommittedCount);
        Assert.Equal(0, publication.PreNativeSkippedCount);
    }

    private static ServiceCycleIdentity NewCycleIdentity() => new(
        new ServiceId("test.contract"),
        new RuntimeLifecycleGeneration(1),
        new ConfigGeneration(1),
        new RuntimeStrategyGeneration(1),
        new WorldGeneration(1),
        new CycleId(1));
}
