using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal sealed class AutoHarvestWorkerDefinition :
    IServiceCycleWorkerDefinition<
        AutoHarvestCycleState,
        AutoHarvestCycleAction>
{
    private readonly AutoHarvestCycleEvaluator _evaluator = new();

    public AutoHarvestCycleState CreateState(LifecycleGeneration lifecycle) =>
        AutoHarvestCycleState.Create(lifecycle);

    public void ReleaseState(ref AutoHarvestCycleState state) => state = default;

    /// <summary>
    /// Projects the pinned world and decides what to harvest from it, in one step.
    /// </summary>
    /// <remarks>
    /// The projection is a local because nothing outlives the evaluation that reads it: it is
    /// derived from the world this cycle pinned, consumed immediately, and worthless afterwards.
    /// The bulletin is taken and not read: every service is handed all three publications and
    /// ignores what it does not need.
    /// </remarks>
    public WakePolicy Evaluate(
        in SuiteRuntimeConfiguration config,
        GameWorldState world,
        SuiteStrategy strategy,
        in ServiceCycleContext context,
        ref AutoHarvestCycleState state,
        ServiceActionWriter<AutoHarvestCycleAction> actions)
    {
        var projected = AutoHarvestFrameProjector.Project(in config, world);
        var result = _evaluator.Evaluate(projected, config, state, context);
        state = result.State;
        AutoHarvestCycleEvaluator.SettleRetries(in state, in context, actions);
        if (result.HasAction)
        {
            var action = result.Action;
            actions.Add(in action);
        }
        return result.Wake;
    }

    public void ProjectState(
        in AutoHarvestCycleState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) =>
        AutoHarvestServiceProjection.Write(state, output);
}
