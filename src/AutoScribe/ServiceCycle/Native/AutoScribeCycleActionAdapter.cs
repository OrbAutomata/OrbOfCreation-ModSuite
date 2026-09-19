using System;
using OrbModding;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal sealed class AutoScribeCycleActionAdapter : IAutoScribeCycleActionPort
{
    private readonly AutoScribeOneShotCraftGameAction _gameAction;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _ownsActionFamily;
    private readonly AutoScribeActionHealth _health;

    internal AutoScribeCycleActionAdapter(
        AutoScribeOneShotCraftGameAction gameAction,
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily,
        AutoScribeActionHealth health)
    {
        _gameAction = gameAction ?? throw new ArgumentNullException(nameof(gameAction));
        _readLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _ownsActionFamily = ownsActionFamily ??
            throw new ArgumentNullException(nameof(ownsActionFamily));
        _health = health ?? throw new ArgumentNullException(nameof(health));
    }

    public ServiceActionResult TryExecute(
        in AutoScribeCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
    {
        // Roles are intentionally not re-read here. This config is the cycle-pinned operational
        // policy; the worker pinned Roles when the cycle opened.
        if (!AutoScribeConfigurationPolicy.IsOperational(config))
            return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);
        if (!Owns())
        {
            return ServiceActionResult.Rejected(
                AutoScribeActionResultCodes.MutationPermitUnavailable);
        }
        if (!EpochMatches(action.CollectedAtEpoch))
            return ServiceActionResult.Rejected(CommonActionResultCodes.LifecycleReplaced);

        var submission = _gameAction.Submit(in action);
        var enteredFailure = _health.Observe(in submission);
        if (enteredFailure)
            Plugin.Log?.LogAutomataWarning(
                $"Auto Scribe {submission.Stage}/{submission.Preflight}: {submission.Reason}");
        return Map(in submission);
    }

    internal static ServiceActionResult Map(in AutoScribeSubmission submission)
    {
        var code = submission.Preflight switch
        {
            AutoScribePreflight.IdentityUnavailable =>
                AutoScribeActionResultCodes.IdentityUnavailable,
            AutoScribePreflight.RelationshipMismatch =>
                AutoScribeActionResultCodes.RelationshipMismatch,
            AutoScribePreflight.RecipeUnavailable =>
                AutoScribeActionResultCodes.RecipeUnavailable,
            AutoScribePreflight.TargetUnavailable =>
                AutoScribeActionResultCodes.TargetUnavailable,
            AutoScribePreflight.QueueFull => AutoScribeActionResultCodes.QueueFull,
            AutoScribePreflight.CompetingSupply =>
                AutoScribeActionResultCodes.CompetingSupply,
            AutoScribePreflight.Unaffordable => AutoScribeActionResultCodes.Unaffordable,
            AutoScribePreflight.MutationPermitUnavailable =>
                AutoScribeActionResultCodes.MutationPermitUnavailable,
            AutoScribePreflight.ContractUnavailable =>
                AutoScribeActionResultCodes.ContractUnavailable,
            AutoScribePreflight.Quarantined => AutoScribeActionResultCodes.Quarantined,
            AutoScribePreflight.PostPaymentFault =>
                AutoScribeActionResultCodes.PostPaymentFault,
            AutoScribePreflight.VerificationFailed =>
                AutoScribeActionResultCodes.VerificationFailed,
            AutoScribePreflight.LifecycleReplaced =>
                CommonActionResultCodes.LifecycleReplaced,
            AutoScribePreflight.WrongThread => AutoScribeActionResultCodes.WrongThread,
            AutoScribePreflight.Proceeded => CommonActionResultCodes.Committed,
            _ => CommonActionResultCodes.AdapterFault,
        };
        if (submission.CallOutcome.MutationAttempts > 0)
        {
            var evidence = ServiceNativeMutationEvidence.Observed(
                submission.Outcome,
                submission.CallOutcome);
            return submission.Verified
                ? ServiceActionResult.Committed(CommonActionResultCodes.Committed, evidence)
                : ServiceActionResult.Faulted(code, evidence);
        }
        return AutoScribeSubmissionPolicy.Classify(in submission) ==
            AutoScribeSubmissionClass.Backpressure
            ? ServiceActionResult.Rejected(code)
            : ServiceActionResult.Faulted(code);
    }

    private bool Owns() => AutoScribeActionFamilyAccess.Owns(_ownsActionFamily);

    private bool EpochMatches(long planned)
    {
        try
        {
            var current = _readLifecycleEpoch();
            return current > 0 && current == planned;
        }
        catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException)
        {
            return false;
        }
    }
}
