using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal sealed class AutoScribeWorkerDefinition :
    IServiceCycleWorkerDefinition<AutoScribeCycleState, AutoScribeCycleAction>
{
    private readonly AutoScribeIdentityProfile _profile;

    internal AutoScribeWorkerDefinition(AutoScribeIdentityProfile profile) =>
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));

    public AutoScribeCycleState CreateState(LifecycleGeneration lifecycle) =>
        AutoScribeCycleState.Create(lifecycle);

    public void ReleaseState(ref AutoScribeCycleState state) => state = default;

    public WakePolicy Evaluate(
        in SuiteRuntimeConfiguration config,
        GameWorldState world,
        SuiteStrategy strategy,
        in ServiceCycleContext context,
        ref AutoScribeCycleState state,
        ServiceActionWriter<AutoScribeCycleAction> actions)
    {
        if (state.Lifecycle != context.Identity.Lifecycle)
            throw new InvalidOperationException("Auto Scribe state belongs to another lifecycle.");
        state.PinRoles(context.Identity.Config, config.AutoScribe.Roles, _profile);
        var wake = AutoScribeCycleEvaluator.Evaluate(
            world,
            in config,
            _profile,
            state.EnabledRoles,
            state.LastSelectedCraftCostOrder,
            context.RetryGate,
            context.DecisionAt,
            actions,
            out var decision);
        if (decision.SelectedCraftCostOrder >= 0)
            state.ObserveSelection(decision.SelectedCraftCostOrder);
        state.RecordDecision(in decision);
        return wake;
    }

    public void ProjectState(
        in AutoScribeCycleState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) =>
        AutoScribeServiceProjection.Write(in state, output);
}
