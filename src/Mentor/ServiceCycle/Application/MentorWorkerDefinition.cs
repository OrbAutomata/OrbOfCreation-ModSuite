using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;

namespace OrbMentor;

internal sealed class MentorWorkerDefinition :
    IServiceCycleWorkerDefinition<MentorCycleState, MentorCycleAction>
{
    public MentorCycleState CreateState(LifecycleGeneration lifecycle) =>
        MentorCycleState.Create(lifecycle);

    public void ReleaseState(ref MentorCycleState state) => state = default;

    public WakePolicy Evaluate(
        in SuiteRuntimeConfiguration config,
        GameWorldState world,
        SuiteStrategy strategy,
        in ServiceCycleContext context,
        ref MentorCycleState state,
        ServiceActionWriter<MentorCycleAction> actions)
    {
        if (state.Lifecycle != context.Identity.Lifecycle)
            throw new InvalidOperationException("Mentor state belongs to a different lifecycle.");
        return MentorCycleEvaluator.Evaluate(world, in config, ref state, actions);
    }

    public void ProjectState(
        in MentorCycleState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) =>
        MentorServiceProjection.Write(in state, output);
}
