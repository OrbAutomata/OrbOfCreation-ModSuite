using System;
using System.Reflection;
using OrbModding;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// Revalidates one worker decision against the live game and owns Auto Concept's only mutation
/// boundary.
/// </summary>
internal sealed class AutoConceptCycleActionAdapter : IAutoConceptCycleActionPort
{
    private readonly IAutoConceptNativePort _native;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _ownsActionFamily;
    private readonly AutomationCycleGuard<AutoConceptCycleAction> _guard = new(
        "Auto Concept",
        static action => $"Auto Concept did not complete {action.Kind} for {EntityIdentityFormatter.Format(action.RecipeId)}" +
            (action.ReplacementId == Guid.Empty
                ? string.Empty
                : $" with replacement {EntityIdentityFormatter.Format(action.ReplacementId)}"),
        static message => Plugin.Log?.LogAutomataWarning(message));

    internal AutoConceptSubmission LastSubmission { get; private set; }

    internal AutoConceptCycleActionAdapter(
        IAutoConceptNativePort native,
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _ownsActionFamily = ownsActionFamily ?? throw new ArgumentNullException(nameof(ownsActionFamily));
    }

    public ServiceActionResult TryExecute(
        in AutoConceptCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
        => TryExecuteCore(in action, in config, in context, requireAutomationPolicy: true);

#if SERVICE_CYCLE_PROFILE
    internal ServiceActionResult TryExecuteGameMcp(
        in AutoConceptCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
        => TryExecuteCore(in action, in config, in context, requireAutomationPolicy: false);
#endif

    private ServiceActionResult TryExecuteCore(
        in AutoConceptCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context,
        bool requireAutomationPolicy)
    {
        LastSubmission = default;
        if (requireAutomationPolicy && !AutoConceptConfigurationPolicy.IsOperational(config))
            return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);
        if (!_guard.Owns(_ownsActionFamily))
            return ServiceActionResult.Rejected(AutoConceptActionResultCodes.ActionFamilyUnavailable);
        if (!_guard.TryAdmitEpoch(_readLifecycleEpoch, action.CollectedAtEpoch, in action, out var refusal))
            return refusal;

        AutoConceptSubmission submission;
        try
        {
            submission = _native.Submit(
                in action,
                requireAutomationPolicy ? AutoConceptResourceLimits.From(config.AutoConcept) : null);
            LastSubmission = submission;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return _guard.Fault(ex, in action).ToResult();
        }

        return _guard.Finish(submission.Outcome, in action);
    }
}
