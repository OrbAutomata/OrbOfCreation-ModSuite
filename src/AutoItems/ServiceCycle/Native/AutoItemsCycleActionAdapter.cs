using System;
using OrbModding;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal sealed class AutoItemsCycleActionAdapter : IAutoItemsCycleActionPort
{
    private readonly AutoItemsConsumableUseGameAction _gameAction;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _ownsActionFamily;
    private readonly Func<string> _readOwnershipFailure;
    private readonly AutoItemsActionHealth _health;

    internal AutoItemsCycleActionAdapter(
        AutoItemsConsumableUseGameAction gameAction,
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily,
        Func<string> readOwnershipFailure,
        AutoItemsActionHealth health)
    {
        _gameAction = gameAction ?? throw new ArgumentNullException(nameof(gameAction));
        _readLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _ownsActionFamily = ownsActionFamily ??
            throw new ArgumentNullException(nameof(ownsActionFamily));
        _readOwnershipFailure = readOwnershipFailure ??
            throw new ArgumentNullException(nameof(readOwnershipFailure));
        _health = health ?? throw new ArgumentNullException(nameof(health));
    }

    public ServiceActionResult TryExecute(
        in AutoItemsCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
    {
        // This is the cycle-pinned policy reading by design. There is no current-configuration
        // reader in this adapter or in the GameAction beneath it.
        if (!AutoItemsConfigurationPolicy.IsOperational(config) ||
            !AutoItemsConfigurationPolicy.Allows(
                config.AutoItems,
                action.Family,
                action.ItemId))
            return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);
        if (!Owns())
        {
            var reason = ReadOwnershipFailure();
            _health.ObserveOwnership(reason);
            return ServiceActionResult.Rejected(
                AutoItemsActionResultCodes.ActionFamilyUnavailable);
        }
        if (!NativeEpochMatches(action.CollectedAtEpoch))
            return ServiceActionResult.Rejected(CommonActionResultCodes.LifecycleReplaced);

        AutoItemsSubmission submission;
        try
        {
            submission = _gameAction.Submit(in action);
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            var reason =
                "Auto Items failed at the native boundary: " +
                ex.GetBaseException().Message;
            submission = AutoItemsSubmission.Reject(
                AutoItemsPreflight.ContractUnavailable,
                reason);
        }

        _health.Observe(in submission);
        if (!submission.Verified)
            Plugin.Log?.LogAutomataWarning(
                $"Auto Items {submission.Preflight}: {submission.Reason}");
        return Map(in submission);
    }

    internal static ServiceActionResult Map(in AutoItemsSubmission submission)
    {
        var code = submission.Preflight switch
        {
            AutoItemsPreflight.ItemUnavailable => AutoItemsActionResultCodes.ItemUnavailable,
            AutoItemsPreflight.FamilyChanged => AutoItemsActionResultCodes.FamilyChanged,
            AutoItemsPreflight.NativeBusy => AutoItemsActionResultCodes.NativeBusy,
            AutoItemsPreflight.NotVisible => AutoItemsActionResultCodes.NotVisible,
            AutoItemsPreflight.CanFireRefused => AutoItemsActionResultCodes.CanFireRefused,
            AutoItemsPreflight.RandomizationUnavailable =>
                AutoItemsActionResultCodes.RandomizationUnavailable,
            AutoItemsPreflight.TargetUnavailable =>
                AutoItemsActionResultCodes.TargetUnavailable,
            AutoItemsPreflight.MutationPermitUnavailable =>
                AutoItemsActionResultCodes.MutationPermitUnavailable,
            AutoItemsPreflight.ContractUnavailable =>
                AutoItemsActionResultCodes.ContractUnavailable,
            AutoItemsPreflight.MultiBuyUnavailable =>
                AutoItemsActionResultCodes.MultiBuyUnavailable,
            AutoItemsPreflight.Quarantined =>
                AutoItemsActionResultCodes.Quarantined,
            AutoItemsPreflight.TemporaryDurationChanged =>
                AutoItemsActionResultCodes.TemporaryDurationChanged,
            AutoItemsPreflight.TemporaryCostChanged =>
                AutoItemsActionResultCodes.TemporaryCostChanged,
            AutoItemsPreflight.TemporaryEffectPresent =>
                AutoItemsActionResultCodes.TemporaryEffectPresent,
            AutoItemsPreflight.TargetingInProgress =>
                AutoItemsActionResultCodes.TargetingInProgress,
            AutoItemsPreflight.Proceeded => CommonActionResultCodes.Committed,
            _ => CommonActionResultCodes.AdapterFault,
        };
        if (submission.Preflight != AutoItemsPreflight.Proceeded)
        {
            if (submission.CallOutcome.MutationAttempts > 0)
            {
                var rejectedEvidence = ServiceNativeMutationEvidence.Observed(
                    submission.Outcome,
                    submission.CallOutcome);
                return ServiceActionResult.Faulted(code, rejectedEvidence);
            }
            return IsExpectedRejection(submission.Preflight)
                ? ServiceActionResult.Rejected(code)
                : ServiceActionResult.Faulted(code);
        }

        var evidence = ServiceNativeMutationEvidence.Observed(
            submission.Outcome,
            submission.CallOutcome);
        return submission.Verified
            ? ServiceActionResult.Committed(CommonActionResultCodes.Committed, evidence)
            : ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault, evidence);
    }

    private static bool IsExpectedRejection(AutoItemsPreflight preflight) =>
        preflight is AutoItemsPreflight.ItemUnavailable or
            AutoItemsPreflight.FamilyChanged or
            AutoItemsPreflight.NativeBusy or
            AutoItemsPreflight.NotVisible or
            AutoItemsPreflight.CanFireRefused or
            AutoItemsPreflight.RandomizationUnavailable or
            AutoItemsPreflight.MutationPermitUnavailable or
            AutoItemsPreflight.TargetUnavailable or
            AutoItemsPreflight.TemporaryDurationChanged or
            AutoItemsPreflight.TemporaryCostChanged or
            AutoItemsPreflight.TemporaryEffectPresent or
            AutoItemsPreflight.TargetingInProgress;

    private bool Owns()
    {
        try
        {
            return _ownsActionFamily();
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return false;
        }
    }

    private string ReadOwnershipFailure()
    {
        try
        {
            var reason = _readOwnershipFailure();
            return string.IsNullOrWhiteSpace(reason)
                ? "Auto Items does not own ConsumableUse and NativeMultiBuyOverride."
                : reason;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return "Auto Items ownership evidence failed: " + ex.GetBaseException().Message;
        }
    }

    private bool NativeEpochMatches(long plannedEpoch)
    {
        try
        {
            var current = _readLifecycleEpoch();
            return current > 0 && current == plannedEpoch;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return false;
        }
    }
}
