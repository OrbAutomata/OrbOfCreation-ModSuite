using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Tracing.Emission;

public sealed class ServiceCycleSemanticRecorderTests
{
    [Fact]
    public void RecorderExposesBoundedReadsButNotRawAppend()
    {
        var publicMethods = typeof(ServiceCycleSemanticRecorder).GetMethods(BindingFlags.Instance | BindingFlags.Public);

        Assert.Contains(publicMethods, method => method.Name == nameof(ServiceCycleSemanticRecorder.DrainSince));
        Assert.Contains(publicMethods, method => method.Name == nameof(ServiceCycleSemanticRecorder.CreateCapture));
        Assert.Contains(publicMethods, method => method.Name == nameof(ServiceCycleSemanticRecorder.PullCapture));
        Assert.DoesNotContain(publicMethods, method => method.Name.Contains("Append", StringComparison.Ordinal));
    }

    [Fact]
    public void StableIdentitiesAndCausalHeadsFollowRegistrationAndSuiteDomains()
    {
        var recorder = NewRecorder(32, 3);
        recorder.ConfigurationPublished(new ConfigGeneration(1), new MonotonicTimestamp(1));
        recorder.StrategyPublished(new StrategyGeneration(1), new MonotonicTimestamp(2));
        recorder.LifecycleRequested(0, new LifecycleGeneration(2), new MonotonicTimestamp(3));
        var emergency = new EmergencyStopContext(
            new EmergencyStopEpisodeId(1),
            new EmergencyStopTransitionGeneration(1),
            EmergencyStopReason.UserRequested);
        recorder.EmergencyEntered(in emergency, new MonotonicTimestamp(4));
        var rejectedPump = SemanticRecorderFixtures.Pump(accepted: false);
        recorder.PumpCompleted(in rejectedPump, new MonotonicTimestamp(5));
        var cycle = SemanticRecorderFixtures.Cycle;
        var ready = ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
        var startContext = new ServiceCycleStartContext(
            new LifecycleGeneration(2), new ConfigGeneration(3), default, new MonotonicTimestamp(8));
        recorder.CycleQueued(0, in cycle, in ready, new MonotonicTimestamp(5), default);
        recorder.CycleStarted(0, in cycle, new MonotonicTimestamp(6), default);

        var events = Drain(recorder);

        Assert.Equal(new ulong[] { 1, 2, 3, 4, 5, 6, 7 }, events.Select(value => value.Id.Sequence));
        // Publications name the suite, not a service, so they join the suite chain the emergency and
        // the pump summary are on rather than heading each service's own chain.
        Assert.Equal(0UL, events[0].Payload.Service);
        Assert.Equal(0UL, events[1].Payload.Service);
        Assert.False(events[0].HasParent);
        Assert.Equal(events[0].Id, events[1].Parent);
        Assert.False(events[2].HasParent);
        Assert.Equal(events[1].Id, events[3].Parent);
        Assert.Equal(events[3].Id, events[4].Parent);
        Assert.Equal(events[3].Id, events[5].Parent);
        Assert.Equal(events[5].Id, events[6].Parent);
        Assert.Equal(new ServiceCycleTraceServiceId(1), recorder.Identities.ForRegistrationOrdinal(0));
        Assert.Equal(new ServiceCycleTraceServiceId(3), recorder.Identities.ForRegistrationOrdinal(2));
    }

    [Fact]
    public void RegistrationRejectsGapsDuplicatesAndRuntimeIdentityMismatches()
    {
        var recorder = new ServiceCycleSemanticRecorder(new ServiceCycleTraceSessionId(77), 8, 2);
        var primary = SemanticRecorderFixtures.Cycle.Service;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            recorder.RegisterService(1, new ServiceId("test.service.1")));
        recorder.RegisterService(0, primary);
        Assert.Throws<ArgumentException>(() => recorder.RegisterService(1, primary));
        recorder.RegisterService(1, new ServiceId("test.service.1"));

        var mismatched = new ServiceCycleIdentity(
            new ServiceId("test.service.1"),
            new LifecycleGeneration(2),
            new ConfigGeneration(3),
            new StrategyGeneration(4),
            new WorldGeneration(1),
            new CycleId(6));
        Assert.Throws<ArgumentException>(() =>
            recorder.CycleStarted(0, in mismatched, new MonotonicTimestamp(1), default));
        Assert.Equal(0, recorder.Count);
    }

    [Fact]
    public void SemanticMethodsProjectExactNumericPayloads()
    {
        var recorder = NewRecorder(64, 2);
        var cycle = SemanticRecorderFixtures.Cycle;
        var capture = SemanticRecorderFixtures.Capture;
        var fault = SemanticRecorderFixtures.Fault;
        var startContext = new ServiceCycleStartContext(
            new LifecycleGeneration(2), new ConfigGeneration(3), default, new MonotonicTimestamp(8));
        var captured = ServiceCaptureResult.Captured(CommonServiceDecisionCodes.Captured);
        var unavailable = ServiceCaptureResult.Unavailable(
            CommonServiceDecisionCodes.CaptureUnavailable,
            WakePolicy.AfterDecision(new MonotonicDuration(5)));
        var action = SemanticRecorderFixtures.ActionContext();
        var committed = SemanticRecorderFixtures.CommittedAction();
        var completedReceipt = BatchReceipt.Completed(
            cycle,
            new BatchId(8),
            1,
            new ServiceNativeCallTotals(1, 1, 1),
            new MonotonicTimestamp(130));
        var projection = SemanticRecorderFixtures.ProjectionPublication();

        recorder.ConfigurationPublished(new ConfigGeneration(3), new MonotonicTimestamp(1));
        recorder.StrategyPublished(new StrategyGeneration(4), new MonotonicTimestamp(2));
        recorder.LifecycleRequested(0, new LifecycleGeneration(2), new MonotonicTimestamp(3));
        recorder.LifecycleActivated(0, new LifecycleGeneration(2), new MonotonicTimestamp(4));
        recorder.LifecycleRetired(0, new LifecycleGeneration(1), new MonotonicTimestamp(5));
        var emergency = new EmergencyStopContext(
            new EmergencyStopEpisodeId(2),
            new EmergencyStopTransitionGeneration(3),
            EmergencyStopReason.SafetyInterlock);
        recorder.EmergencyEntered(in emergency, new MonotonicTimestamp(6));
        recorder.EmergencyCleared(in emergency, new MonotonicTimestamp(7));
        var ready = ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
        recorder.CycleQueued(0, in cycle, in ready, new MonotonicTimestamp(8), new MonotonicDuration(1));
        recorder.CycleStarted(0, in cycle, new MonotonicTimestamp(9), new MonotonicDuration(2));
        recorder.StartAttempted(0, in startContext, new MonotonicTimestamp(8));
        recorder.StartReady(0, in startContext, in ready, new MonotonicTimestamp(8), default);
        recorder.CaptureStarted(0, in capture);
        recorder.CaptureCompleted(0, in capture, in captured, new MonotonicTimestamp(11), new MonotonicDuration(3));
        recorder.StartAttempted(0, in startContext, new MonotonicTimestamp(8));
        recorder.StartReady(0, in startContext, in ready, new MonotonicTimestamp(8), default);
        recorder.CaptureStarted(0, in capture);
        recorder.CaptureUnavailable(0, in capture, in unavailable, new MonotonicTimestamp(12), new MonotonicDuration(4));
        recorder.StartAttempted(0, in startContext, new MonotonicTimestamp(8));
        recorder.StartReady(0, in startContext, in ready, new MonotonicTimestamp(8), default);
        recorder.CaptureStarted(0, in capture);
        recorder.CaptureFaulted(0, in capture, in fault, new MonotonicTimestamp(13), new MonotonicDuration(5));
        recorder.EvaluationStarted(0, in cycle, new MonotonicTimestamp(14));
        recorder.EvaluationCompleted(
            0,
            in cycle,
            1,
            WakePolicy.AfterDecision(new MonotonicDuration(20)),
            new MonotonicTimestamp(15),
            new MonotonicDuration(6));
        recorder.EvaluationFaulted(0, in cycle, in fault, new MonotonicTimestamp(16), new MonotonicDuration(7));
        recorder.StatePublished(0, in projection);
        recorder.BatchPublished(0, in cycle, new BatchId(8), 1, new MonotonicTimestamp(120));
        recorder.ActionAttempted(0, in action);
        recorder.ActionCompleted(0, in action, in committed, new MonotonicTimestamp(125), new MonotonicDuration(25));
        recorder.BatchTerminal(0, in completedReceipt);
        recorder.CycleCompleted(0, in cycle, new MonotonicTimestamp(131), new MonotonicDuration(123));
        recorder.CycleOrphaned(0, in cycle, new MonotonicTimestamp(132), default);
        recorder.CycleFaulted(0, in cycle, in fault, new MonotonicTimestamp(133), default);
        recorder.FaultObserved(0, new LifecycleGeneration(2), in fault);
        recorder.RetryScheduled(0, new LifecycleGeneration(2), in fault, new MonotonicTimestamp(150));
        recorder.FaultRecovered(0, new LifecycleGeneration(2), in fault, new MonotonicTimestamp(160));
        var activePump = SemanticRecorderFixtures.Pump(accepted: true, actions: 1, captures: 1, responses: 1);
        recorder.PumpCompleted(in activePump, new MonotonicTimestamp(170));

        var events = Drain(recorder);

        Assert.Equal(36, events.Length);
        Assert.Equal(ServiceCycleSemanticEventKind.ConfigurationPublished, events[0].Kind);
        Assert.Equal(0UL, events[0].Payload.Service);
        Assert.Equal(3UL, events[0].Payload.Configuration);
        Assert.Equal(1L, events[0].Payload.TimestampTicks);
        Assert.Equal(ServiceCycleSemanticEventKind.StatePublished, events[24].Kind);
        Assert.Equal(12UL, events[24].Payload.StatePublication);
        Assert.Equal(ServiceCycleSemanticEventKind.StartAttempted, events[9].Kind);
        Assert.Equal(ServiceCycleSemanticEventKind.StartReady, events[10].Kind);
        Assert.Equal(events[9].Id, events[10].Parent);
        Assert.Equal(CommonServiceDecisionCodes.Ready.Value, events[10].Payload.Code);
        Assert.Equal(8L, events[10].Payload.TimestampTicks);
        Assert.Equal(0L, events[10].Payload.DurationTicks);
        Assert.Equal(ServiceCycleSemanticEventKind.CaptureStarted, events[11].Kind);
        Assert.Equal(events[10].Id, events[11].Parent);
        Assert.Equal(0UL, events[11].Payload.Strategy);
        Assert.Equal(ServiceCycleSemanticEventKind.CaptureCompleted, events[12].Kind);
        Assert.Equal(4UL, events[12].Payload.Strategy);
        Assert.Equal(ServiceCycleSemanticEventKind.CaptureUnavailable, events[16].Kind);
        Assert.Equal(0UL, events[16].Payload.Strategy);
        Assert.True(events[16].Payload.TryGetReturnedWake(out var unavailableWake));
        Assert.Equal(WakePolicy.AfterDecision(new MonotonicDuration(5)), unavailableWake);
        Assert.Equal(ServiceCycleSemanticEventKind.CaptureFaulted, events[20].Kind);
        Assert.Equal(0UL, events[20].Payload.Strategy);
        Assert.True(events[22].Payload.TryGetReturnedWake(out var returnedWake));
        Assert.Equal(WakePolicy.AfterDecision(new MonotonicDuration(20)), returnedWake);
        var projectionSnapshot = projection.Snapshot;
        Assert.Equal(ServiceCycleProjectionFingerprint.Compute(in projectionSnapshot), events[24].Payload.Fingerprint);
        Assert.Equal(ServiceCycleSemanticEventKind.ActionCommitted, events[27].Kind);
        Assert.Equal(8UL, events[27].Payload.Batch);
        Assert.Equal(10UL, events[27].Payload.Action);
        Assert.Equal(1L, events[27].Payload.NativeCallsAttempted);
        Assert.Equal(1L, events[27].Payload.MutationsCommitted);
        Assert.Equal(ServiceCycleSemanticEventKind.BatchCompleted, events[28].Kind);
        Assert.Equal(1, events[28].Payload.ActionCount);
        Assert.Equal(1, events[28].Payload.CommittedCount);
        Assert.Equal(ServiceCycleSemanticEventKind.RetryScheduled, events[33].Kind);
        Assert.Equal(150L, events[33].Payload.DeadlineTicks);
        Assert.Equal(ServiceCycleSemanticEventKind.FaultRecovered, events[34].Kind);
        Assert.Equal(160L, events[34].Payload.TimestampTicks);
        Assert.Equal(ServiceCycleSemanticEventKind.PumpCompleted, events[35].Kind);
        Assert.True(events[35].Payload.PumpAccepted);
        Assert.Equal(1, events[35].Payload.ActionsAttempted);
    }

    [Fact]
    public void EmergencyTransitionIsTheExactParentOfServiceRejectionAndBatchAbort()
    {
        var recorder = NewRecorder(8, 1);
        var context = SemanticRecorderFixtures.ActionContext();
        recorder.ActionAttempted(0, in context);
        var emergency = new EmergencyStopContext(
            new EmergencyStopEpisodeId(1),
            new EmergencyStopTransitionGeneration(1),
            EmergencyStopReason.UserRequested);
        recorder.EmergencyEntered(in emergency, new MonotonicTimestamp(101));
        var rejection = ServiceActionResult.Rejected(CommonActionResultCodes.EmergencyStop);
        recorder.ActionRejectedForEmergency(
            0, in context, in rejection, in emergency,
            new MonotonicTimestamp(102), new MonotonicDuration(2));
        var receipt = BatchReceipt.Terminated(
            SemanticRecorderFixtures.Cycle,
            new BatchId(8),
            actionCount: 1,
            committedCount: 0,
            terminalIndex: 0,
            rejection,
            default,
            new MonotonicTimestamp(102),
            emergency);
        recorder.BatchTerminal(0, in receipt);

        var events = Drain(recorder);

        Assert.Equal(ServiceCycleSemanticEventKind.ActionAttempted, events[0].Kind);
        Assert.Equal(ServiceCycleSemanticEventKind.EmergencyEntered, events[1].Kind);
        Assert.Equal(ServiceCycleSemanticEventKind.ActionRejected, events[2].Kind);
        Assert.Equal(events[1].Id, events[2].Parent);
        Assert.Equal(ServiceCycleSemanticEventKind.BatchAborted, events[3].Kind);
        Assert.Equal(events[1].Id, events[3].Parent);
        Assert.Equal(CommonActionResultCodes.EmergencyStop.Value, events[3].Payload.Code);
    }

    [Fact]
    public void LateEmergencyRejectionKeepsItsOriginalEpisodeAcrossClearAndReengage()
    {
        var recorder = NewRecorder(16, 1);
        var first = new EmergencyStopContext(
            new EmergencyStopEpisodeId(1),
            new EmergencyStopTransitionGeneration(1),
            EmergencyStopReason.UserRequested);
        var second = new EmergencyStopContext(
            new EmergencyStopEpisodeId(2),
            new EmergencyStopTransitionGeneration(3),
            EmergencyStopReason.SafetyInterlock);
        recorder.EmergencyEntered(in first, new MonotonicTimestamp(1));
        recorder.EmergencyCleared(in first, new MonotonicTimestamp(2));
        recorder.EmergencyEntered(in second, new MonotonicTimestamp(3));
        recorder.EmergencyCleared(in second, new MonotonicTimestamp(4));

        var rejection = ServiceActionResult.Rejected(CommonActionResultCodes.EmergencyStop);
        var receipt = BatchReceipt.Terminated(
            SemanticRecorderFixtures.Cycle,
            new BatchId(8),
            actionCount: 1,
            committedCount: 0,
            terminalIndex: 0,
            rejection,
            default,
            new MonotonicTimestamp(5),
            first);
        recorder.BatchTerminal(0, in receipt);

        var events = Drain(recorder);
        Assert.Equal(ServiceCycleSemanticEventKind.BatchAborted, events[4].Kind);
        Assert.Equal(events[0].Id, events[4].Parent);
        Assert.NotEqual(events[2].Id, events[4].Parent);
    }

    [Fact]
    public void ForgottenEmergencyEpisodeDoesNotInventAFalseParent()
    {
        var recorder = NewRecorder(2, 1);
        var first = Emergency(1);
        var second = Emergency(2);
        var third = Emergency(3);
        recorder.EmergencyEntered(in first, new MonotonicTimestamp(1));
        recorder.EmergencyEntered(in second, new MonotonicTimestamp(2));
        recorder.EmergencyEntered(in third, new MonotonicTimestamp(3));

        var rejection = ServiceActionResult.Rejected(CommonActionResultCodes.EmergencyStop);
        var receipt = BatchReceipt.Terminated(
            SemanticRecorderFixtures.Cycle,
            new BatchId(8),
            1,
            0,
            0,
            rejection,
            default,
            new MonotonicTimestamp(4),
            first);
        recorder.BatchTerminal(0, in receipt);

        var events = Drain(recorder);
        Assert.Equal(ServiceCycleSemanticEventKind.BatchAborted, events[1].Kind);
        Assert.False(events[1].HasParent);
    }

    [Fact]
    public void DisabledEmitsNothingAndEveryPumpRetainsRotationEvidence()
    {
        var disabled = new ServiceCycleSemanticRecorder(new ServiceCycleTraceSessionId(1), 4, 1, enabled: false);
        disabled.ConfigurationPublished(default, default);
        var activePump = SemanticRecorderFixtures.Pump(accepted: true, actions: 1);
        disabled.PumpCompleted(in activePump, new MonotonicTimestamp(1));
        Assert.Equal(0, disabled.Count);

        var recorder = NewRecorder(4, 1);
        var idle = SemanticRecorderFixtures.Pump(accepted: true);
        recorder.PumpCompleted(in idle, new MonotonicTimestamp(10));
        Assert.Equal(1, recorder.Count);

        var rejected = SemanticRecorderFixtures.Pump(accepted: false);
        recorder.PumpCompleted(in rejected, new MonotonicTimestamp(11));
        Assert.Equal(2, recorder.Count);
        var events = Drain(recorder);
        Assert.True(events[0].Payload.PumpAccepted);
        Assert.False(events[1].Payload.PumpAccepted);
    }

    [Fact]
    public void CapacityOverwriteAndCaptureExposeExactBoundedLoss()
    {
        var recorder = NewRecorder(3, 1);
        for (var generation = 1; generation <= 5; generation++)
            recorder.ConfigurationPublished(new ConfigGeneration((ulong)generation), new MonotonicTimestamp(generation));

        var capture = recorder.CreateCapture(3);
        var drain = recorder.PullCapture(capture, 3);
        var bytes = new byte[capture.GetEncodedLength()];
        capture.Encode(bytes);
        var document = ServiceCycleTraceCodec.Decode(bytes);

        Assert.Equal(2UL, recorder.OverwrittenTotal);
        Assert.Equal(1UL, recorder.OverwrittenRange.FirstSequence);
        Assert.Equal(2UL, recorder.OverwrittenRange.LastSequence);
        Assert.Equal(2UL, drain.Dropped.Count);
        Assert.False(capture.IsComplete);
        Assert.Equal(3, document.Count);
        Assert.Equal(3UL, document[0].Id.Sequence);
        Assert.Equal(5UL, document[2].Id.Sequence);
    }

    [Fact]
    public void EveryRecorderSurfaceIsOwnerThreadAffine()
    {
        var recorder = NewRecorder(4, 1);
        var registrationRecorder = new ServiceCycleSemanticRecorder(new ServiceCycleTraceSessionId(2), 4, 1);
        Exception? appendFailure = null;
        Exception? readFailure = null;
        Exception? registrationFailure = null;
        var thread = new Thread(() =>
        {
            try { recorder.ConfigurationPublished(new ConfigGeneration(1), default); }
            catch (Exception exception) { appendFailure = exception; }
            try { _ = recorder.Count; }
            catch (Exception exception) { readFailure = exception; }
            try { registrationRecorder.RegisterService(0, new ServiceId("other-thread")); }
            catch (Exception exception) { registrationFailure = exception; }
        });
        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(2)),
            "The foreign-thread recorder probe did not complete.");

        Assert.IsType<InvalidOperationException>(appendFailure);
        Assert.IsType<InvalidOperationException>(readFailure);
        Assert.IsType<InvalidOperationException>(registrationFailure);
        Assert.Equal(0, registrationRecorder.Identities.RegisteredCount);
        Assert.Equal(0, recorder.Count);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void WarmInCapacityEmissionAndDrainAllocateNothing()
    {
        var recorder = NewRecorder(16, 1);
        var output = new ServiceCycleSemanticEvent[1];
        var cursor = recorder.Cursor;
        for (var index = 0; index < 64; index++)
        {
            recorder.ConfigurationPublished(new ConfigGeneration((ulong)index + 1), new MonotonicTimestamp(index));
            cursor = recorder.DrainSince(cursor, output).Cursor;
        }

        var generation = 100L;
        var allocated = AllocationProbe.MeasureRepeated(1_000, () =>
        {
            recorder.ConfigurationPublished(
                new ConfigGeneration((ulong)generation), new MonotonicTimestamp(generation));
            cursor = recorder.DrainSince(cursor, output).Cursor;
            generation++;
        });

        Assert.Equal(0, allocated);
    }

    private static ServiceCycleSemanticRecorder NewRecorder(int capacity, int services)
    {
        var recorder = new ServiceCycleSemanticRecorder(new ServiceCycleTraceSessionId(77), capacity, services);
        for (var ordinal = 0; ordinal < services; ordinal++)
        {
            var id = ordinal == 0 ? SemanticRecorderFixtures.Cycle.Service : new ServiceId($"test.service.{ordinal}");
            recorder.RegisterService(ordinal, id);
        }
        return recorder;
    }

    private static EmergencyStopContext Emergency(long episode) => new(
        new EmergencyStopEpisodeId(episode),
        new EmergencyStopTransitionGeneration(episode),
        EmergencyStopReason.UserRequested);

    private static ServiceCycleSemanticEvent[] Drain(ServiceCycleSemanticRecorder recorder)
    {
        var output = new ServiceCycleSemanticEvent[recorder.Count];
        var drain = recorder.DrainSince(default, output);
        Assert.Equal(output.Length, drain.Copied);
        return output;
    }
}
