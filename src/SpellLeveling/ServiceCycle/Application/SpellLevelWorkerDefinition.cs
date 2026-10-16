using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal sealed class SpellLevelWorkerDefinition :
    IServiceCycleWorkerDefinition<
        SpellLevelCycleState,
        SpellLevelCycleAction>
{
    public SpellLevelCycleState CreateState(LifecycleGeneration lifecycle) =>
        SpellLevelCycleState.Create(lifecycle);

    public void ReleaseState(ref SpellLevelCycleState state) => state = default;

    /// <summary>
    /// Plans this cycle's mastery-level purchase from the pinned world.
    /// </summary>
    /// <remarks>
    /// There is no projection step and no scratch. Auto Buy needs one because it prices hundreds of
    /// candidates against a cost table; Spell Leveling reads three published fields off each spell row
    /// and one upgrade, so the world is already the frame.
    /// <para>
    /// The bulletin is taken and not read. Spell leveling spends resources but holds back no reserve:
    /// it buys any ready level the game can afford, and what a stance would mean here is a strategist
    /// question with no strategist to answer it.
    /// </para>
    /// </remarks>
    public WakePolicy Evaluate(
        in SuiteRuntimeConfiguration config,
        GameWorldState world,
        SuiteStrategy strategy,
        in ServiceCycleContext context,
        ref SpellLevelCycleState state,
        ServiceActionWriter<SpellLevelCycleAction> actions)
    {
        // The state carries no plan across cycles, only the lifecycle it was minted under, so that a
        // world projected under a different generation is refused rather than acted on.
        if (state.Lifecycle != context.Identity.Lifecycle)
            throw new InvalidOperationException("Spell Leveling state belongs to a different lifecycle.");

        var wake = SpellLevelCycleEvaluator.Evaluate(world, in config, actions, out var decision);
        state.RecordDecision(decision);
        return wake;
    }

    public void ProjectState(
        in SpellLevelCycleState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) =>
        SpellLevelServiceProjection.Write(state, output);
}
