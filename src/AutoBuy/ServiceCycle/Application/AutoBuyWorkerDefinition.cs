using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal sealed class AutoBuyWorkerDefinition :
    IServiceCycleWorkerDefinition<
        AutoBuyCycleState,
        AutoBuyCycleAction>
{
    public AutoBuyCycleState CreateState(LifecycleGeneration lifecycle) =>
        AutoBuyCycleState.Create(lifecycle);

    public void ReleaseState(ref AutoBuyCycleState state) => state = default;

    /// <summary>
    /// Projects the pinned world into the state's scratch and plans this cycle's purchases from it.
    /// </summary>
    /// <remarks>
    /// One step, because it was never two. Projecting and evaluating happen on the same thread,
    /// back to back, against the same pinned world, and nothing between them could observe the
    /// projection or act on it — the split existed only because the runtime handed the projection a
    /// buffer of its own.
    /// <para>
    /// The bulletin is taken and not read. Every service is handed all three publications; what Auto
    /// Buy would do with a stance is a strategist question, and there is no strategist.
    /// </para>
    /// </remarks>
    public WakePolicy Evaluate(
        in SuiteRuntimeConfiguration config,
        GameWorldState world,
        SuiteStrategy strategy,
        in ServiceCycleContext context,
        ref AutoBuyCycleState state,
        ServiceActionWriter<AutoBuyCycleAction> actions)
    {
        // The state carries no plan across cycles, only the lifecycle it was minted under, so that a
        // world projected under a different generation is refused rather than acted on.
        if (state.Lifecycle != context.Identity.Lifecycle)
            throw new InvalidOperationException("Auto Buy state belongs to a different lifecycle.");

        AutoBuyFrameProjector.Project(ref state.Scratch, in config, world);
        var wake = AutoBuyCycleEvaluator.Evaluate(
            in state.Scratch, in config, context.RetryGate, context.DecisionAt, actions, out var decision);
        state.RecordDecision(decision);
        return wake;
    }

    public void ProjectState(
        in AutoBuyCycleState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) =>
        AutoBuyServiceProjection.Write(state, output);
}
