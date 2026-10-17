using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

/// <summary>
/// Decides whether a service starts this frame, and opens the cycle when it does.
/// </summary>
/// <remarks>
/// Abstract because the two service shapes open a cycle differently, and in only that one respect: a
/// source reads the game into its buffer first and may report it unavailable, an ordinary service has
/// nothing to read and goes straight to the handoff. Everything on either side of that — the start
/// decision, the pinned publications, the identity, the handoff, the deferred-request stash — is the
/// same work, and stays here rather than becoming two implementations that drift.
/// </remarks>
internal abstract partial class ServiceCycleStartCoordinator<TState, TAction>
{
    private readonly IServiceCycleMainThreadDefinition<TAction> _definition;
    private readonly ServiceConfigurationPublisher _configuration;
    private readonly ServiceCycleHandoff _handoff;
    private readonly ServiceFaultTracker _startFaults;
    private readonly ServiceStrategyPublisher _strategy;
    private readonly ServiceWorldPublisher<GameWorldState> _world;
    private readonly bool _wakeOnWorldPublication;
    private ulong _cycleSequence;
    private ulong _batchSequence;
    private bool _hasPendingRequest;
    private ConfigurationPublication? _pendingConfiguration;
    private WorldPublication<GameWorldState>? _pendingWorld;
    private StrategyPublication? _pendingStrategy;
    private ServiceCycleContext _pendingContext;
    private BatchId _pendingBatch;
    private ServiceStartDecisionFact _pendingStart;

    private protected ServiceCycleStartCoordinator(
        IServiceCycleMainThreadDefinition<TAction> definition,
        ServiceConfigurationPublisher configuration,
        ServiceCycleHandoff handoff,
        ServiceCycleMainState state,
        ServiceId serviceId,
        LifecycleGeneration lifecycle,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        IMonotonicClock clock,
        ServiceRunnerLifetime lifetime,
        ServiceStrategyPublisher strategy,
        ServiceWorldPublisher<GameWorldState> world,
        bool wakeOnWorldPublication)
    {
        _definition = definition;
        _configuration = configuration;
        _handoff = handoff;
        State = state;
        ServiceIdentity = serviceId;
        Lifecycle = lifecycle;
        _startFaults = new ServiceFaultTracker(faultRecoveryPolicy);
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _wakeOnWorldPublication = wakeOnWorldPublication;
    }

    private protected ServiceCycleMainState State { get; }
    private protected ServiceId ServiceIdentity { get; }
    private protected LifecycleGeneration Lifecycle { get; }
    private protected IMonotonicClock Clock { get; }
    private protected ServiceRunnerLifetime Lifetime { get; }
    private protected WorldGeneration LatestWorldGeneration => _world.ReadLatest().Generation;

    /// <summary>
    /// Whether a newer world ends this wait. Only a service that asked to wait for the next world is
    /// woken by one; a timed wait runs its course, and the source never waits on its own publication.
    /// </summary>
    internal bool WorldEndsWait(WakePolicy wake) =>
        _wakeOnWorldPublication && wake.Kind == WakePolicyKind.OnPublication;

    /// <summary>
    /// Whether the main thread is inside the service's capture callback right now.
    /// </summary>
    /// <remarks>
    /// Always false for an ordinary service, which has no capture to be inside. That is the honest
    /// answer rather than a missing one: the runtime asks in order to know whether a service callback
    /// is on the stack, and for an ordinary service the answer is simply no.
    /// </remarks>
    internal bool IsCapturing { get; private protected set; }
    internal bool IsInvokingStartCallback { get; private set; }
    internal bool HasPendingRequest => _hasPendingRequest;

    internal bool CancelPendingRequestForEmergency()
    {
        if (!_hasPendingRequest) return false;
        ClearPendingRequest();
        return true;
    }

    internal void InvalidateLifecycle()
    {
        if (!_hasPendingRequest) return;
        ClearPendingRequest();
    }

    internal void ResetStartFaults() => _startFaults.Reset();

    /// <summary>
    /// Opens a cycle once the start decision has said to. The two shapes differ here and nowhere else.
    /// </summary>
    private protected abstract ServiceCycleStartAttempt Open(
        ConfigurationPublication configuration,
        in ServiceStartDecisionFact startFact,
        in ServiceStartInvocationFact startInvocation,
        bool nonBlockingProbe,
        int ordinal,
        IServiceCycleAttemptObserver? observer);

    private void ClearPendingRequest()
    {
        _hasPendingRequest = false;
        _pendingConfiguration = null;
        _pendingWorld = null;
        _pendingStrategy = null;
        _pendingContext = default;
        _pendingBatch = default;
        _pendingStart = default;
        State.CycleConfiguration = null;
    }

    private ServiceCycleStartAttempt TryPublishPendingRequest(bool nonBlocking)
    {
        if (Lifetime.IsSuperseded)
        {
            InvalidateLifecycle();
            return default;
        }
        var configuration = _pendingConfiguration ??
            throw new InvalidOperationException("A deferred request lost its pinned configuration.");
        // The world stays the one pinned when the cycle started: the freshness gate approved that
        // generation, and re-pinning here would hand the worker a snapshot the gate never saw.
        var world = _pendingWorld ??
            throw new InvalidOperationException("A deferred request lost its pinned world.");
        var strategy = _pendingStrategy ??
            throw new InvalidOperationException("A deferred request lost its pinned strategy.");
        // As above, the causal queue timestamp must precede the handoff that can wake evaluation.
        var queuedAt = Clock.Now;
        var published = nonBlocking
            ? _handoff.TryPublishRequestNonBlocking(
                configuration, world, strategy, in _pendingContext, _pendingBatch, out _)
            : _handoff.TryPublishRequest(
                configuration, world, strategy, in _pendingContext, _pendingBatch, out _);
        if (!published) return default;

        var identity = _pendingContext.Identity;
        var batch = _pendingBatch;
        var startFact = _pendingStart;
        _hasPendingRequest = false;
        _pendingConfiguration = null;
        _pendingWorld = null;
        _pendingStrategy = null;
        _pendingContext = default;
        _pendingBatch = default;
        _pendingStart = default;
        State.ClearWake();
        State.InFlightCycle = identity;
        State.InFlightBatch = batch;
        State.HasInFlightCycle = true;
        _startFaults.Reset();
        return new ServiceCycleStartAttempt(
            true, startFact, default, identity, batch, queuedAt);
    }

    /// <summary>
    /// Records a fault raised on the main-thread start path, under the callback that raised it.
    /// </summary>
    /// <remarks>
    /// One tracker for both callbacks, because a service that keeps failing to open a cycle backs off
    /// the same way whether the failure came out of its start decision or its capture. The category
    /// still distinguishes them: only a source has a capture, so a fault labelled that way names a
    /// game read that went wrong, and a start fault names a service that could not decide.
    /// </remarks>
    private protected ServiceFaultRecord RecordStartFault(
        ServiceFaultCategory category,
        MonotonicTimestamp observedAt)
    {
        var record = _startFaults.Record(category, observedAt);
        State.LatestFault = record.Fault;
        State.ScheduleWake(
            record.RetryDue,
            State.LatestConfigGeneration,
            invalidatedByConfiguration: false,
            invalidatedByWorld: false);
        return record;
    }

    private protected ServiceFaultRecoveryFact RecoverStartFault(MonotonicTimestamp observedAt)
    {
        var recovery = _startFaults.Recover(observedAt);
        if (recovery.IsPresent && State.LatestFault.Category == recovery.Fault.Category)
            State.LatestFault = default;
        return recovery;
    }
}
