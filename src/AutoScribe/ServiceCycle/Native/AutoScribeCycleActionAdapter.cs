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
            Plugin.Log?.LogAutomataWarning($"Auto Scribe: {submission.Outcome.Reason}");
        return submission.Outcome.ToResult();
    }

    private bool Owns() => AutoScribeActionFamilyAccess.Owns(_ownsActionFamily);

    private bool EpochMatches(long planned)
    {
        try
        {
            var current = _readLifecycleEpoch();
            return current > 0 && current == planned;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return false;
        }
    }
}
