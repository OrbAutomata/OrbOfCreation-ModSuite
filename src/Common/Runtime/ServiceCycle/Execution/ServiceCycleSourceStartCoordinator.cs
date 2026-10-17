using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

/// <summary>
/// Opens a cycle for the service that reads the game.
/// </summary>
/// <remarks>
/// The capture is the one piece of service code the runtime runs on the main thread on purpose: the
/// game's own state can be read nowhere else. Everything the stage produces — the sequence, the
/// context, the fact — names the capture rather than the cycle, because a capture can fail or come
/// back empty without a cycle ever existing.
/// </remarks>
internal sealed class ServiceCycleSourceStartCoordinator<TState, TAction> :
    ServiceCycleStartCoordinator<
        TState,
        TAction>
{
    private readonly IServiceCycleSourceDefinition<TState, TAction> _definition;
    private readonly GameWorldCycleFrame _frame;
    private ulong _captureSequence;

    internal ServiceCycleSourceStartCoordinator(
        IServiceCycleSourceDefinition<TState, TAction> definition,
        ServiceConfigurationPublisher configuration,
        GameWorldCycleFrame frame,
        ServiceCycleHandoff handoff,
        ServiceCycleMainState state,
        ServiceId serviceId,
        LifecycleGeneration lifecycle,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        IMonotonicClock clock,
        ServiceRunnerLifetime lifetime,
        ServiceStrategyPublisher strategy,
        ServiceWorldPublisher<GameWorldState> world)
        : base(
            definition,
            configuration,
            handoff,
            state,
            serviceId,
            lifecycle,
            faultRecoveryPolicy,
            clock,
            lifetime,
            strategy,
            world,
            wakeOnWorldPublication: false)
    {
        _definition = definition;
        _frame = frame;
    }

    private protected override ServiceCycleStartAttempt Open(
        ConfigurationPublication configuration,
        in ServiceStartDecisionFact startFact,
        in ServiceStartInvocationFact startInvocation,
        bool nonBlockingProbe,
        int ordinal,
        IServiceCycleAttemptObserver? observer)
    {
        var opening = OpenSequences();
        var capture = new CaptureSequence(checked(++_captureSequence));
        var captureAttemptedAt = Clock.Now;
        var captureContext = new ServiceCaptureContext(
            ServiceIdentity,
            Lifecycle,
            configuration.Generation,
            opening.Strategy.Generation,
            capture,
            opening.Cycle,
            opening.World.Snapshot,
            captureAttemptedAt);
        observer?.CaptureStarted(ordinal, in captureContext);
        var captureStartedAt = Clock.Now;
        ServiceCaptureResult result;
        IsCapturing = true;
        try
        {
            var snapshot = configuration.Snapshot;
            result = _definition.Capture(_frame, in snapshot, in captureContext);
            if (!result.IsValid)
                throw new InvalidOperationException(
                    "The service returned an invalid capture result.");
        }
        catch
        {
            var captureFaultedAt = Clock.Now;
            var record = RecordStartFault(ServiceFaultCategory.Capture, captureFaultedAt);
            var faultedCapture = new ServiceCaptureFact(
                captureContext,
                default,
                captureStartedAt,
                captureFaultedAt,
                record.Fault,
                record.RetryDue);
            return new ServiceCycleStartAttempt(
                false, startFact, faultedCapture, default, opening.Batch, default,
                record.Fault, record.RetryDue,
                startInvocation: startInvocation);
        }
        finally
        {
            IsCapturing = false;
        }
        var captureObservedAt = Clock.Now;
        var committedCapture = new ServiceCaptureFact(
            captureContext,
            result,
            captureStartedAt,
            captureObservedAt);
        State.LastCapture = committedCapture;
        var recoveredFault = RecoverStartFault(captureObservedAt);

        if (!result.IsCaptured)
        {
            // A retired lifecycle is not asked to wait: nothing will act on the wake, and scheduling
            // one would outlive the runner that owns it.
            if (!Lifetime.IsSuperseded)
            {
                State.ScheduleWake(
                    ServiceWakeSchedule.FromRetryPolicy(
                        result.WakePolicy,
                        captureObservedAt),
                    configuration.Generation,
                    opening.World.Generation,
                    invalidatedByWorld: WorldEndsWait(result.WakePolicy));
            }
            return new ServiceCycleStartAttempt(
                false, startFact, committedCapture, default, opening.Batch, default,
                recoveredFault: recoveredFault,
                startInvocation: startInvocation);
        }

        return Queue(
            configuration,
            in opening,
            captureObservedAt,
            in startFact,
            in startInvocation,
            in committedCapture,
            in recoveredFault,
            nonBlockingProbe);
    }
}
