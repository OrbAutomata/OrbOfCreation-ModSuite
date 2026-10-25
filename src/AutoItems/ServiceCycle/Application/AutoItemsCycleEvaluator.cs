using System;
using OrbModding.Common;
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

        // A missing row is evidence of completion only in a clean consumables reading. The
        // collector already publishes and logs capture failures; wait without losing follow-up
        // state or interpreting partial rows as player cancellation.
        if (!HasCleanConsumables(world))
        {
            metrics = EmptyMetrics(world, state.PendingTemporaryItem != Guid.Empty
                ? AutoItemsDecisionKind.AwaitingTemporaryActivation
                : state.PausedTemporaryItem != Guid.Empty
                    ? AutoItemsDecisionKind.TemporaryEffectActive
                    : AutoItemsDecisionKind.Idle);
            return WakePolicy.OnPublication;
        }

        var activation = AutoItemsTemporaryActivationPolicy.Observe(world, ref state);
        if (activation.State == AutoItemsTemporaryActivationState.AwaitingActivation)
        {
            metrics = EmptyMetrics(world, AutoItemsDecisionKind.AwaitingTemporaryActivation);
            return WakePolicy.OnPublication;
        }
        if (activation.State == AutoItemsTemporaryActivationState.Cancelled)
        {
            actions.ReportFailure(
                AutoItemsServicePolicies.RetryTarget(activation.ItemId),
                AutoItemsActionResultCodes.UseNotVerified,
                $"the usage Auto Items queued for {EntityIdentityFormatter.PlayerName(activation.ItemId)} " +
                "disappeared before it engaged.");
            metrics = EmptyMetrics(world, AutoItemsDecisionKind.Idle);
            return WakePolicy.OnPublication;
        }
        if (activation.State is AutoItemsTemporaryActivationState.Active or
                AutoItemsTemporaryActivationState.Paused &&
            context.RetryGate.TryFind(AutoItemsServicePolicies.RetryTarget(activation.ItemId), out _))
            actions.EndRetry(AutoItemsServicePolicies.RetryTarget(activation.ItemId));
        if (!AutoItemsConfigurationPolicy.IsOperational(configuration))
        {
            metrics = EmptyMetrics(world, AutoItemsDecisionKind.Disabled);
            return WakePolicy.OnPublication;
        }

        var scan = AutoItemsCandidateScanner.Scan(
            world,
            in configuration,
            state.TemporaryAllowlist,
            state.TargetlessScrolls,
            context.RetryGate,
            context.DecisionAt,
            actions);
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

    private static bool HasCleanConsumables(GameWorldState world)
    {
        foreach (var category in world.CollectionCategories.AsSpan())
        {
            if (string.Equals(category.Category, "consumables", StringComparison.Ordinal))
                return category.IsClean;
        }
        return false;
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
