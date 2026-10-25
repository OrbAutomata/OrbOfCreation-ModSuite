using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal struct QualityOfLifeCycleState
{
    internal LifecycleGeneration Lifecycle;
    internal bool EvidenceReady;
}

internal sealed class QualityOfLifeWorkerDefinition :
    IServiceCycleWorkerDefinition<QualityOfLifeCycleState, QualityOfLifeCycleAction>
{
    internal const int EvidenceReadyKey = 1;

    public QualityOfLifeCycleState CreateState(LifecycleGeneration lifecycle) => new() { Lifecycle = lifecycle };
    public void ReleaseState(ref QualityOfLifeCycleState state) => state = default;

    public WakePolicy Evaluate(in SuiteRuntimeConfiguration config, GameWorldState world,
        SuiteStrategy strategy, in ServiceCycleContext context, ref QualityOfLifeCycleState state,
        ServiceActionWriter<QualityOfLifeCycleAction> actions)
    {
        if (state.Lifecycle != context.Identity.Lifecycle)
            throw new InvalidOperationException("Quality of Life state belongs to another lifecycle.");
        state.EvidenceReady = HasCleanEvidence(world);
        if (!state.EvidenceReady || !QualityOfLifePolicy.IsOperational(config)) return WakePolicy.OnPublication;
        foreach (var supported in QualityOfLifePolicy.SupportedPassives)
        {
            var target = QualityOfLifeService.RetryTarget(supported.Uuid);
            if (!WorldLookup.TryFind(world.PassiveAbilities, supported.Uuid, out var passive) ||
                !QualityOfLifePolicy.ShouldMute(in passive))
            {
                if (context.RetryGate.TryFind(target, out _)) actions.EndRetry(target);
                continue;
            }
            if (context.RetryGate.IsWaiting(target, context.DecisionAt)) continue;
            actions.Add(new QualityOfLifeCycleAction(supported.Uuid, checked((long)context.Identity.Lifecycle.Value)));
        }
        return WakePolicy.OnPublication;
    }

    public void ProjectState(in QualityOfLifeCycleState state, in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) =>
        output.Add(new ServiceProjectionKey(EvidenceReadyKey), ServiceProjectionValue.FromBoolean(state.EvidenceReady));

    private static bool HasCleanEvidence(GameWorldState world)
    {
        for (var index = 0; index < world.CollectionCategories.Count; index++)
        {
            var category = world.CollectionCategories[index];
            if (string.Equals(category.Category, "passive abilities", StringComparison.Ordinal)) return category.IsClean;
        }
        return false;
    }
}
