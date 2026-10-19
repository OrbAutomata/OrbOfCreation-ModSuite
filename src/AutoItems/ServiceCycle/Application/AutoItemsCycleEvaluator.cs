using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Plans at most one use from one immutable world publication. Every path waits for another world
/// or committed-configuration publication; temporary expiry is observed rather than scheduled.
/// </summary>
internal static class AutoItemsCycleEvaluator
{
    internal static WakePolicy Evaluate(
        GameWorldState world,
        in SuiteRuntimeConfiguration configuration,
        in ServiceCycleContext context,
        ref AutoItemsCycleState state,
        ServiceActionWriter<AutoItemsCycleAction> actions,
        out AutoItemsDecisionMetrics metrics)
    {
        var previousReceipt = context.PreviousReceipt;
        AutoItemsTemporaryActivationPolicy.ReconcileReceipt(
            in previousReceipt,
            ref state);
        if (state.HasPendingReceipt)
        {
            metrics = EmptyMetrics(world, AutoItemsDecisionKind.AwaitingTemporaryActivation);
            return WakePolicy.OnPublication;
        }

        var activation = AutoItemsTemporaryActivationPolicy.Observe(world, ref state);
        if (activation.State == AutoItemsTemporaryActivationState.AwaitingActivation)
        {
            metrics = EmptyMetrics(world, AutoItemsDecisionKind.AwaitingTemporaryActivation);
            return WakePolicy.OnPublication;
        }
        if (!AutoItemsConfigurationPolicy.IsOperational(configuration))
        {
            metrics = EmptyMetrics(world, AutoItemsDecisionKind.Disabled);
            return WakePolicy.OnPublication;
        }

        var scan = AutoItemsCandidateScanner.Scan(
            world,
            in configuration,
            state.TemporaryAllowlist,
            state.TargetlessScrolls);
        if (scan.TemporaryUsagePending)
        {
            metrics = scan.ToMetrics(AutoItemsDecisionKind.AwaitingTemporaryActivation);
            return WakePolicy.OnPublication;
        }
        if (scan.EligibleRelics > 0)
        {
            var action = scan.FirstRelic;
            return Plan(
                in action,
                in scan,
                AutoItemsDecisionKind.Relic,
                actions,
                ref state,
                out metrics);
        }
        if (scan.EligibleScrolls > 0)
        {
            var action = scan.FirstScroll;
            if (action.ItemId == Guid.Empty)
            {
                // Every eligible Scroll last found no target; structures may have changed since,
                // so the rotation starts over.
                state.ForgetTargetlessScrolls();
                action = scan.FirstTargetlessScroll;
            }
            return Plan(
                in action,
                in scan,
                AutoItemsDecisionKind.Scroll,
                actions,
                ref state,
                out metrics);
        }
        var temporaryEffectActive =
            activation.State is AutoItemsTemporaryActivationState.Active or
                AutoItemsTemporaryActivationState.Paused ||
            scan.TemporaryUsagePresent;
        if (temporaryEffectActive)
        {
            metrics = scan.ToMetrics(AutoItemsDecisionKind.TemporaryEffectActive);
            return WakePolicy.OnPublication;
        }
        if (scan.EligibleTemporaryItems > 0)
        {
            var action = scan.FirstTemporary;
            return Plan(
                in action,
                in scan,
                AutoItemsDecisionKind.TemporaryItem,
                actions,
                ref state,
                out metrics);
        }

        metrics = scan.ToMetrics(AutoItemsDecisionKind.Idle);
        return WakePolicy.OnPublication;
    }

    private static AutoItemsDecisionMetrics EmptyMetrics(
        GameWorldState world,
        AutoItemsDecisionKind kind) =>
        new(world.Consumables.Count, 0, 0, 0, 0, 0, 0, kind);

    private static WakePolicy Plan(
        in AutoItemsCycleAction action,
        in AutoItemsCandidateScan scan,
        AutoItemsDecisionKind kind,
        ServiceActionWriter<AutoItemsCycleAction> actions,
        ref AutoItemsCycleState state,
        out AutoItemsDecisionMetrics metrics)
    {
        actions.Add(action);
        state.RecordPlanned(in action);
        metrics = scan.ToMetrics(kind, plannedActions: 1);
        return WakePolicy.OnPublication;
    }
}
