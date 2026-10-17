using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using RuntimeLifecycleGeneration = OrbModding.Common.Runtime.LifecycleGeneration;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

/// <summary>
/// Everything the main thread asks of a service, whichever shape it is: what to call it, when it may
/// start, and how to carry out what its worker decided.
/// </summary>
/// <remarks>
/// The two shapes are siblings rather than one extending the other, and this is what they share. What
/// they do not share is the worker they hand back: an ordinary worker reads the published world, a
/// source worker reads the buffer its own capture filled, and the two evaluations take different
/// arguments. Naming the worker in a common base would force one shape to hand back a contract it
/// cannot honour, so the worker factory belongs to each shape and only the main-thread half is here.
/// The state type is absent for the same reason: nothing on this surface touches worker state.
/// </remarks>
public interface IServiceCycleMainThreadDefinition<TAction>
{
    ServiceId ServiceId { get; }
    WakePolicy DefaultWakePolicy { get; }
    ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; }

    ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration config,
        in ServiceCycleStartContext context);
    ServiceActionJournalAttribution DescribeAction(in TAction action);
    ServiceActionResult TryExecute(
        in TAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context);
}

/// <summary>
/// Complete feature-owned service boundary for the ordinary shape.
/// </summary>
/// <remarks>
/// It reads the world the runtime pinned for it, decides on the worker, and carries the decision out
/// on the main thread. There is no capture here, because there is nothing an ordinary service can
/// read on the main thread that the shared world does not already say — and a main-thread read costs
/// frame time to learn it twice. A service that produces the world rather than consuming it is the
/// other shape and declares <see cref="IServiceCycleSourceDefinition{TState, TAction}"/>. There is
/// no third.
/// </remarks>
public interface IServiceCycleDefinition<TState, TAction> :
    IServiceCycleMainThreadDefinition<TAction>
{
    /// <summary>
    /// Creates the worker resource under the runtime's serialized reference-factory admission.
    /// The callback must finish in finite time and must not synchronously depend on another
    /// reference factory succeeding. The returned reference remains valid through the runtime's
    /// immediate ownership finalization.
    /// </summary>
    IServiceCycleWorkerDefinition<TState, TAction> CreateWorkerDefinition();
}

/// <summary>
/// The half of a worker definition that owns its state, shared by both shapes.
/// </summary>
/// <remarks>
/// Every worker mints state for its lifecycle, releases it, and projects it for the journal. What
/// differs between the two shapes is only what an evaluation reads — the published world, or the
/// buffer this service's own capture filled — so that is the only thing the two worker contracts
/// declare separately.
/// </remarks>
public interface IServiceCycleWorkerStateDefinition<TState>
{
    /// <summary>
    /// Creates worker state under the runtime's serialized reference-factory admission.
    /// The callback must finish in finite time and must not synchronously depend on another
    /// reference factory succeeding. A returned reference remains valid through the runtime's
    /// immediate ownership finalization.
    /// </summary>
    TState CreateState(RuntimeLifecycleGeneration lifecycle);
    void ReleaseState(ref TState state);
    void ProjectState(
        in TState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output);
}

/// <summary>
/// Worker-only feature port. It is a distinct object from the main-thread definition so a worker
/// cannot retain or invoke native capture/action adapters through its service dependency.
/// </summary>
public interface IServiceCycleWorkerDefinition<TState, TAction> :
    IServiceCycleWorkerStateDefinition<TState>
{
    /// <summary>
    /// Reads what this cycle pinned and decides what to do about it, on the worker thread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Projecting the world and deciding from the projection are one step. They run on the same
    /// thread, back to back, against the same pinned snapshot, and nothing between them can observe
    /// the projection — so a service that needs a buffer keeps it in its state, where the arrays
    /// underneath survive the lifecycle, and a service that does not projects into a local.
    /// </para>
    /// <para>
    /// The world arrives as an argument rather than from a publisher the service holds. There is one
    /// game and therefore one world; the runtime owns its publication, pins it once at cycle start,
    /// and hands over the immutable snapshot. A service that could reach the publisher itself could
    /// read it twice in a cycle and evaluate against one snapshot while acting against another.
    /// </para>
    /// <para>
    /// All three publications arrive the same way, and a service ignores the ones it does not need.
    /// The strategy bulletin is the neutral one until a strategist exists, so taking it costs a
    /// service that does not read it nothing — and a service that does read it cannot be handed a
    /// different bulletin than the world and the configuration it was pinned beside.
    /// </para>
    /// <para>
    /// A cycle with nothing to do returns a wake policy and writes no actions. That is a decision,
    /// not a failure, and it completes and publishes a response like any other.
    /// </para>
    /// </remarks>
    WakePolicy Evaluate(
        in SuiteRuntimeConfiguration config,
        GameWorldState world,
        SuiteStrategy strategy,
        in ServiceCycleContext context,
        ref TState state,
        ServiceActionWriter<TAction> actions);
}
