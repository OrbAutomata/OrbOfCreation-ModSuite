using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

internal sealed partial class ServiceCycleSlot<TState, TAction>
{
    public ServiceHandoffPhase HandoffPhaseHint =>
        CurrentRunner?.HandoffPhaseHint ?? ServiceHandoffPhase.Stopped;

    public bool IsBetweenCycles =>
        _position0.IsBetweenCycles && _position1.IsBetweenCycles;

    public bool TryGetRunnerSnapshot(out ServiceRunnerSnapshot snapshot, bool waitForHandoffGate)
    {
        var runner = CurrentRunner;
        if (runner is null)
        {
            snapshot = default;
            return false;
        }
        if (!waitForHandoffGate) return runner.TrySnapshot(out snapshot);
        snapshot = runner.Snapshot;
        return true;
    }

    public ServiceResponseAcquisition TryAcquireResponse(
        MonotonicTimestamp now) =>
        CurrentRunner?.TryAcquireResponseNonBlocking(now) ?? default;

    public bool TryAdvancePendingMainOwnership(MonotonicTimestamp now) =>
        CurrentRunner?.TryAdvancePendingMainOwnership() ?? false;

    public ServiceActionDispatch TryExecuteOne(
        MonotonicTimestamp now,
        long frameIdentity,
        IServiceCycleAttemptObserver? observer
#if SERVICE_CYCLE_PROFILE
        , in ServiceCycleProfileCoordinates profileCoordinates
#endif
        )
    {
        var runner = CurrentRunner;
        if (runner is null) return default;
        var dispatch =
#if SERVICE_CYCLE_PROFILE
            runner.TryExecuteOneNonBlockingProfiled(
#else
            runner.TryExecuteOneNonBlocking(
#endif
            now,
            Ordinal,
            observer
#if SERVICE_CYCLE_PROFILE
            , in profileCoordinates
#endif
            );
        RecordWorldInvalidation(in dispatch, frameIdentity);
        return dispatch;
    }

    /// <summary>
    /// Remembers the frame on which this service last attempted a game-facing action.
    /// </summary>
    /// <remarks>
    /// A commit is absent from the pinned world. A skip, rejection, or fault is evidence that live
    /// native reality diverged from the facts which produced the action, so another plan from those
    /// same facts is not trustworthy. Every game-facing attempt therefore waits for a reading
    /// collected strictly after it. A Source is exempt because it only publishes the reading and
    /// gating it behind its own attempt would stop collection.
    /// </remarks>
    private void RecordWorldInvalidation(in ServiceActionDispatch dispatch, long frameIdentity)
    {
        if (!dispatch.Attempted || ActionDispatchPolicy.Shape == ServiceShape.Source) return;
        RaiseWorldGateFloor(frameIdentity);
    }

    /// <summary>
    /// Closes the gate at birth, on the generation that was live when this runner became the slot's
    /// current one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A service that acts on the first world it is handed acts on whatever the collector had
    /// published by then, which at activation is the seed publication — an empty world, or one whose
    /// prices the game has not cooked yet. Both look like a game with nothing in it and no candidate
    /// costing anything, and a mutating service reading that submits work the operator never wanted.
    /// So every mutating service waits for a reading collected strictly after it went live; that is
    /// the same comparison a service's own mutation arms, with activation as its first cause.
    /// </para>
    /// <para>
    /// A Source is exempt because it <em>is</em> the collector: gating it behind a generation only it
    /// can produce would deadlock the whole suite on the first frame. Shape decides, and shape is read
    /// off where the service's turn falls, so nothing declares this.
    /// </para>
    /// <para>
    /// The floor only ever rises. Re-arming on lifecycle replacement must not forgive a mutation the
    /// slot already made — a fresh runner has no memory of the action, but the world is still missing
    /// it — so activation raises the floor and never lowers it.
    /// </para>
    /// </remarks>
    private void ArmWorldGate()
    {
        if (ActionDispatchPolicy.Shape != ServiceShape.Ordinary) return;
        if (!_world.TryGetLatestGeneration(out var generation)) return;
        RaiseWorldGateFloor((long)generation.Value);
    }

    private void RaiseWorldGateFloor(long floor)
    {
        if (floor > _worldGateFloor) _worldGateFloor = floor;
    }

    public ServiceCycleStartAttempt TryStartCycle(
        MonotonicTimestamp now,
        long frameIdentity,
        IServiceCycleAttemptObserver? observer,
        out bool deferredForWorld)
    {
        deferredForWorld = false;
        var runner = CurrentRunner;
        if (runner is null) return default;
        // Not a wake policy: nothing here is a timing condition. The world either has or has not been
        // re-read since this service went live or last attempted an action, and the answer can change
        // on any frame, so the service is simply skipped and asked again next frame — no callback,
        // nothing scheduled.
        if (IsWaitingForAWorldPastTheGateFloor(frameIdentity))
        {
            deferredForWorld = true;
            return default;
        }
        return runner.TryStartCycleNonBlocking(now, Ordinal, observer);
    }

    /// <summary>
    /// Whether the live world is still the one this service went live on, or an older reading than
    /// its own last game-facing action attempt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unconditional: every mutating service is gated and nothing opts in. The floor is raised by
    /// activation and by every attempted game-facing action, so a service is held from birth until a
    /// later reading exists, and afterwards until a reading later than its own last attempt does. A
    /// Source is the one exemption, because it is the collector and would otherwise wait on itself.
    /// The game always has a collector, so a composition with no world publisher is a test fixture
    /// rather than a case to design for, and those supply worlds the way production's collector does
    /// — a fixture that publishes nothing simply never starts a mutating cycle, which is the rule
    /// working.
    /// </para>
    /// <para>
    /// A generation is the pump frame the readings were collected on, so this compares like with
    /// like. Strictly-after is deliberate: within a frame actions dispatch before captures, so a
    /// snapshot stamped with our own action's frame does contain it, but waiting one more collection
    /// costs a fraction of a second where acting on a world missing our own change costs a duplicate.
    /// A source that cannot answer holds the service closed, because "unknown" is not "fresh".
    /// </para>
    /// <para>
    /// Every held frame is recorded, because holding a service is indistinguishable from that
    /// service having nothing to do — the same silence a stalled collector would produce across the
    /// whole suite.
    /// </para>
    /// </remarks>
    private bool IsWaitingForAWorldPastTheGateFloor(long frameIdentity)
    {
        if (_worldGateFloor == 0) return false;
        var answered = _world.TryGetLatestGeneration(out var generation);
        if (answered && (long)generation.Value > _worldGateFloor) return false;
        _latestWorldGateDeferral = new ServiceWorldGateDeferralFact(
            ++_worldGateDeferralSequence,
            frameIdentity,
            _worldGateFloor,
            generation);
        return true;
    }

    public bool RejectForEmergencyStop(
        EmergencyStopContext emergency,
        MonotonicTimestamp now,
        out BatchReceipt receipt)
    {
        var runner = CurrentRunner;
        if (runner is null)
        {
            receipt = default;
            return false;
        }
        return runner.RejectForEmergencyStop(emergency, now, out receipt);
    }
}
