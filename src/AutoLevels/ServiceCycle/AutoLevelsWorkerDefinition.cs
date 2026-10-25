using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
namespace OrbAutomata;
internal struct AutoLevelsCycleState
{
    internal LifecycleGeneration Lifecycle;
    internal WorldGeneration EvaluatedWorld;
    internal int NextDial;
    internal AutoLevelsDecision Output, Reserve, Druidry, Alchemy;
    internal bool Observed;
    internal AutoLevelsDecision Decision(int index) => index switch
    { 0 => Output, 1 => Reserve, 2 => Druidry, 3 => Alchemy, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
}
/// <summary>One total dial change from each world, lowering danger first and rotating raises fairly.</summary>
internal sealed class AutoLevelsWorkerDefinition : IServiceCycleWorkerDefinition<AutoLevelsCycleState, AutoLevelsCycleAction>
{
    public AutoLevelsCycleState CreateState(LifecycleGeneration lifecycle) => new() { Lifecycle = lifecycle };
    public void ReleaseState(ref AutoLevelsCycleState state) => state = default;
    public WakePolicy Evaluate(in SuiteRuntimeConfiguration config, GameWorldState world, SuiteStrategy strategy,
        in ServiceCycleContext context, ref AutoLevelsCycleState state, ServiceActionWriter<AutoLevelsCycleAction> actions)
    {
        if (state.Lifecycle != context.Identity.Lifecycle) throw new InvalidOperationException("Auto Levels state belongs to another lifecycle.");
        if (!AutoLevelsService.IsOperational(config) || state.EvaluatedWorld == context.Identity.World) return WakePolicy.OnPublication;
        state.EvaluatedWorld = context.Identity.World;
        state.Output = AutoLevelsPolicy.Evaluate(world, LevelDial.Output, config.AutoLevels);
        state.Reserve = AutoLevelsPolicy.Evaluate(world, LevelDial.Reserve, config.AutoLevels);
        state.Druidry = AutoLevelsPolicy.Evaluate(world, LevelDial.Druidry, config.AutoLevels);
        state.Alchemy = AutoLevelsPolicy.Evaluate(world, LevelDial.Alchemy, config.AutoLevels);
        state.Observed = true;
        for (var pass = 0; pass < 2; pass++)
        for (var offset = 0; offset < 4; offset++)
        {
            var index = (state.NextDial + offset) % 4;
            var decision = state.Decision(index);
            var target = AutoLevelsService.RetryTarget(decision.Dial);
            if (decision.Kind == AutoLevelsDecisionKind.Hold)
            {
                if (context.RetryGate.TryFind(target, out _)) actions.EndRetry(target);
                continue;
            }
            if ((pass == 0) != (decision.Kind == AutoLevelsDecisionKind.Lower) || context.RetryGate.IsWaiting(target, context.DecisionAt)) continue;
            actions.Add(new AutoLevelsCycleAction(decision.Dial, decision.CurrentLevel, decision.TargetLevel, world.CollectedAtEpoch));
            state.NextDial = (index + 1) % 4;
            return WakePolicy.OnPublication;
        }
        return WakePolicy.OnPublication;
    }
    public void ProjectState(in AutoLevelsCycleState state, in ServiceProjectionContext context, ServiceStateProjectionBuilder output) =>
        AutoLevelsServiceProjection.Write(in state, output);
}
