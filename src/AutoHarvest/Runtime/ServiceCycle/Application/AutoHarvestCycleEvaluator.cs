using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbAutomata;

internal sealed class AutoHarvestCycleEvaluator
{
    public AutoHarvestCycleEvaluation Evaluate(
        in AutoHarvestCycleFrame frame,
        in SuiteRuntimeConfiguration config,
        in AutoHarvestCycleState previousState,
        in ServiceCycleContext context)
    {
        if (previousState.Lifecycle != context.Identity.Lifecycle)
            throw new InvalidOperationException("Auto Harvest state belongs to a different lifecycle.");

        var faults = RememberPreviousFault(previousState, context.PreviousReceipt);
        var fruitHealth = ProjectHealth(frame.Fruit, config.AutoHarvest.CollectFruitTrees, faults);
        var treasureHealth = ProjectHealth(frame.Treasure, config.AutoHarvest.CollectTreasureTrees, faults);
        var nextPair = ApplyPreviousReceipt(previousState, context.PreviousReceipt);
        var actionPair = default(AutoHarvestPair);
        var hasAction = AutoHarvestConfigurationPolicy.IsOperational(config) &&
            !HasFeatureFailure(frame, config) &&
            TrySelect(nextPair, fruitHealth, treasureHealth, in context, out actionPair);
        var action = default(AutoHarvestCycleAction);
        if (hasAction)
        {
            var capture = CaptureOf(in frame, actionPair);
            action = new AutoHarvestCycleAction(actionPair, capture.Facts, capture.Safety);
        }

        var state = previousState.CompleteEvaluation(
            nextPair,
            fruitHealth,
            treasureHealth,
            hasAction,
            actionPair,
            faults);
        return new AutoHarvestCycleEvaluation(
            state,
            action,
            hasAction,
            WakePolicy.OnPublication);
    }

    /// <summary>
    /// Ends the retry episode of a pair this world no longer offers as a candidate: one not selected,
    /// one held on a proven contract mismatch, or one the world shows locked, hidden or not ready. A
    /// pair still eligible keeps its episode until it commits.
    /// </summary>
    internal static void SettleRetries(
        in AutoHarvestCycleState state,
        in ServiceCycleContext context,
        ServiceActionWriter<AutoHarvestCycleAction> actions)
    {
        EndRetryUnlessEligible(state.FruitHealth, context.RetryGate, actions);
        EndRetryUnlessEligible(state.TreasureHealth, context.RetryGate, actions);
    }

    private static void EndRetryUnlessEligible(
        in AutoHarvestPairHealth health,
        ServiceRetryGateSnapshot retryGate,
        ServiceActionWriter<AutoHarvestCycleAction> actions)
    {
        if (health.Kind == AutoHarvestPairHealthKind.Eligible) return;
        var target = AutoHarvestServicePolicies.RetryTarget(health.Pair);
        if (retryGate.TryFind(target, out _)) actions.EndRetry(target);
    }

    /// <summary>
    /// What one pair's health is, given what the world says about it and what this service already
    /// knows about itself.
    /// </summary>
    /// <remarks>
    /// A remembered contract mismatch outranks anything the snapshot could say: nothing about the
    /// world is evidence that the build's contract changed.
    /// </remarks>
    private static AutoHarvestPairHealth ProjectHealth(
        in AutoHarvestPairCapture capture,
        bool selected,
        in AutoHarvestFaultMemory faults)
    {
        if (!selected) return AutoHarvestPairHealth.NotSelected(capture.Pair);
        if (faults.For(capture.Pair) == AutoHarvestFaultKind.ContractUnavailable)
        {
            return new AutoHarvestPairHealth(
                capture.Pair,
                true,
                AutoHarvestPairHealthKind.ContractUnavailable,
                faults.HasFeatureFault);
        }

        if (capture.Kind == AutoHarvestPairCaptureKind.Unavailable)
        {
            return new AutoHarvestPairHealth(
                capture.Pair,
                true,
                AutoHarvestPairHealthKind.RegistryNotReady,
                featureScoped: true);
        }

        var decision = AutoHarvestPolicy.EvaluatePair(capture.Pair, selected, capture.Facts);
        return AutoHarvestPairHealthMapper.FromDecision(capture.Pair, decision);
    }

    /// <summary>
    /// Folds the last batch's terminal code into the contract mismatches this service has proved.
    /// </summary>
    /// <remarks>
    /// The receipt names the pair only through the plan that produced it, so this reads
    /// <see cref="AutoHarvestCycleState.PlannedPair"/> for the pair-scoped code. A batch that ended
    /// any other way, or any other code, leaves the memory alone: a rejection says the action was
    /// declined, and any other fault backs its pair off on the retry gate.
    /// </remarks>
    private static AutoHarvestFaultMemory RememberPreviousFault(
        in AutoHarvestCycleState state,
        in BatchReceipt receipt)
    {
        if (!state.HasPlannedAction ||
            !receipt.IsPresent ||
            receipt.Disposition != BatchTerminalDisposition.Faulted)
        {
            return state.Faults;
        }

        if (receipt.ResultCode == AutoHarvestActionResultCodes.FeatureContractUnavailable)
            return state.Faults.WithFeature(AutoHarvestFaultKind.ContractUnavailable);
        if (receipt.ResultCode == AutoHarvestActionResultCodes.PairContractUnavailable)
            return state.Faults.With(state.PlannedPair, AutoHarvestFaultKind.ContractUnavailable);
        return state.Faults;
    }

    private static AutoHarvestPair ApplyPreviousReceipt(
        in AutoHarvestCycleState state,
        in BatchReceipt receipt)
    {
        if (!state.HasPlannedAction ||
            !receipt.IsPresent ||
            receipt.Disposition != BatchTerminalDisposition.Completed ||
            receipt.ActionCount != 1 ||
            receipt.CommittedCount != 1)
        {
            return state.NextPair;
        }

        return Other(state.PlannedPair);
    }

    private static AutoHarvestPairCapture CaptureOf(
        in AutoHarvestCycleFrame frame,
        AutoHarvestPair pair) =>
        pair == AutoHarvestPair.FruitTree ? frame.Fruit : frame.Treasure;

    private static bool TrySelect(
        AutoHarvestPair first,
        in AutoHarvestPairHealth fruit,
        in AutoHarvestPairHealth treasure,
        in ServiceCycleContext context,
        out AutoHarvestPair pair)
    {
        if (IsReady(first, fruit, treasure, in context))
        {
            pair = first;
            return true;
        }

        var second = Other(first);
        if (IsReady(second, fruit, treasure, in context))
        {
            pair = second;
            return true;
        }

        pair = default;
        return false;
    }

    /// <summary>
    /// Whether a selected pair reported something that stops the feature rather than itself.
    /// </summary>
    /// <remarks>
    /// Only this reaches the sibling. A remembered feature fault needs no equivalent here because
    /// <see cref="AutoHarvestFaultMemory.For"/> already answers for both pairs, so neither is
    /// eligible and nothing is left to select.
    /// </remarks>
    private static bool HasFeatureFailure(
        in AutoHarvestCycleFrame frame,
        in SuiteRuntimeConfiguration config) =>
        IsSelectedFeatureFailure(frame.Fruit, config.AutoHarvest.CollectFruitTrees) ||
        IsSelectedFeatureFailure(frame.Treasure, config.AutoHarvest.CollectTreasureTrees);

    private static bool IsSelectedFeatureFailure(
        in AutoHarvestPairCapture capture,
        bool selected) =>
        selected && capture.Kind == AutoHarvestPairCaptureKind.Unavailable;

    /// <summary>Whether the pair is eligible and not backing off.</summary>
    private static bool IsReady(
        AutoHarvestPair pair,
        in AutoHarvestPairHealth fruit,
        in AutoHarvestPairHealth treasure,
        in ServiceCycleContext context) =>
        (pair == AutoHarvestPair.FruitTree ? fruit : treasure).Kind ==
        AutoHarvestPairHealthKind.Eligible &&
        !context.RetryGate.IsWaiting(AutoHarvestServicePolicies.RetryTarget(pair), context.DecisionAt);

    private static AutoHarvestPair Other(AutoHarvestPair pair) => pair switch
    {
        AutoHarvestPair.FruitTree => AutoHarvestPair.TreasureTree,
        AutoHarvestPair.TreasureTree => AutoHarvestPair.FruitTree,
        _ => throw new ArgumentOutOfRangeException(nameof(pair)),
    };
}
