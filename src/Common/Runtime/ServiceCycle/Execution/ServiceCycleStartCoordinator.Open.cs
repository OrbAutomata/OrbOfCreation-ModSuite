using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal abstract partial class ServiceCycleStartCoordinator<TState, TAction>
{
    /// <summary>
    /// The readings a cycle is pinned to, taken once when it opens.
    /// </summary>
    /// <remarks>
    /// Everything a cycle produces is only interpretable against these, so they are read together and
    /// never re-read: a cycle that pinned one world and published against another could not be
    /// explained afterwards.
    /// </remarks>
    private protected readonly struct CycleOpening
    {
        internal CycleOpening(
            CycleId cycle,
            BatchId batch,
            StrategyPublication strategy,
            WorldPublication<GameWorldState> world)
        {
            Cycle = cycle;
            Batch = batch;
            Strategy = strategy;
            World = world;
        }

        internal CycleId Cycle { get; }
        internal BatchId Batch { get; }
        internal StrategyPublication Strategy { get; }
        internal WorldPublication<GameWorldState> World { get; }
    }

    private protected CycleOpening OpenSequences()
    {
        var cycle = new CycleId(checked(++_cycleSequence));
        var batch = new BatchId(checked(++_batchSequence));
        // Pinned here, with the configuration and the world, so the whole cycle runs against one
        // reading of each. Both snapshots are immutable, so this is three volatile reads.
        return new CycleOpening(cycle, batch, _strategy.ReadLatest(), _world.ReadLatest());
    }

    /// <summary>
    /// Hands the opened cycle to the worker, or stashes it when the handoff is busy.
    /// </summary>
    /// <remarks>
    /// Shared by both shapes. A source arrives here with a capture fact to report and an ordinary
    /// service with none; nothing else about queueing a cycle depends on which shape opened it.
    /// </remarks>
    private protected ServiceCycleStartAttempt Queue(
        ConfigurationPublication configuration,
        in CycleOpening opening,
        MonotonicTimestamp decidedAt,
        in ServiceStartDecisionFact startFact,
        in ServiceStartInvocationFact startInvocation,
        in ServiceCaptureFact captureFact,
        in ServiceFaultRecoveryFact recoveredFault)
    {
        var world = opening.World;
        var strategy = opening.Strategy;
        var identity = new ServiceCycleIdentity(
            ServiceIdentity,
            Lifecycle,
            configuration.Generation,
            opening.Strategy.Generation,
            world.Generation,
            opening.Cycle);
        var batch = opening.Batch;
        if (Lifetime.IsSuperseded)
            return new ServiceCycleStartAttempt(
                false, startFact, captureFact, identity, batch, default,
                recoveredFault: recoveredFault,
                startInvocation: startInvocation);

        var context = new ServiceCycleContext(identity, State.PreviousReceipt, decidedAt);
        State.CycleConfiguration = configuration;
        var queuedAt = Clock.Now;
        if (!_handoff.TryPublishRequestNonBlocking(
                configuration, world, strategy, in context, batch, out _))
        {
            _hasPendingRequest = true;
            _pendingConfiguration = configuration;
            _pendingWorld = world;
            _pendingStrategy = strategy;
            _pendingContext = context;
            _pendingBatch = batch;
            _pendingStart = startFact;
            return new ServiceCycleStartAttempt(
                false, startFact, captureFact, identity, batch, default,
                recoveredFault: recoveredFault,
                startInvocation: startInvocation);
        }

        State.ClearWake();
        State.InFlightCycle = identity;
        State.InFlightBatch = batch;
        State.HasInFlightCycle = true;
        return new ServiceCycleStartAttempt(
            true, startFact, captureFact, identity, batch, queuedAt,
            recoveredFault: recoveredFault,
            startInvocation: startInvocation);
    }
}
