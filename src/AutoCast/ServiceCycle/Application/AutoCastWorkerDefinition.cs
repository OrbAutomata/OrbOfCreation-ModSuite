using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal sealed class AutoCastWorkerDefinition :
    IServiceCycleWorkerDefinition<
        AutoCastCycleState,
        AutoCastCycleAction>
{
    public AutoCastCycleState CreateState(LifecycleGeneration lifecycle) =>
        AutoCastCycleState.Create(lifecycle);

    public void ReleaseState(ref AutoCastCycleState state) => state = default;

    /// <summary>
    /// Plans this cycle's cast from the pinned world.
    /// </summary>
    /// <remarks>
    /// There is no projection step and no scratch. Auto Buy needs one because it prices hundreds of
    /// candidates against a cost table; Auto Cast reads a handful of published fields off at most a
    /// loadout's worth of rows, so the world is already the frame.
    /// <para>
    /// The bulletin is taken and not read. Auto Cast spends resources and applies a reserve floor, so
    /// it is a stance consumer in principle — but the floor it applies is the operator's configured
    /// one, read the same way Auto Buy reads it, and what a stance would mean here is a strategist
    /// question with no strategist to answer it.
    /// </para>
    /// </remarks>
    public WakePolicy Evaluate(
        in SuiteRuntimeConfiguration config,
        GameWorldState world,
        SuiteStrategy strategy,
        in ServiceCycleContext context,
        ref AutoCastCycleState state,
        ServiceActionWriter<AutoCastCycleAction> actions)
    {
        // The state carries a cursor and a hold across cycles, so a world projected under a different
        // generation is refused rather than acted on: a hold minted in another run of the game names
        // a loadout position that no longer means anything.
        if (state.Lifecycle != context.Identity.Lifecycle)
            throw new InvalidOperationException("Auto Cast state belongs to a different lifecycle.");

        var wake = AutoCastCycleEvaluator.Evaluate(
            world, in config, ref state, context.RetryGate, context.DecisionAt, actions, out var decision);
        state.RecordDecision(decision);
        return wake;
    }

    public void ProjectState(
        in AutoCastCycleState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) =>
        AutoCastServiceProjection.Write(state, output);
}
