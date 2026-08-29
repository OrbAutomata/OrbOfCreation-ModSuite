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
        if (!Owns())
            return ServiceActionResult.Rejected(AutoConceptActionResultCodes.ActionFamilyUnavailable);
        if (!NativeEpochMatches(action.CollectedAtEpoch))
            return ServiceActionResult.Rejected(CommonActionResultCodes.LifecycleReplaced);

        AutoConceptSubmission submission;
        try
        {
            submission = _native.Submit(in action, config.AutoConcept);
            LastSubmission = submission;
        }
        catch (Exception ex) when (
            ex is TargetInvocationException or ArgumentException or InvalidOperationException or
            TargetException or MemberAccessException or FormatException or OverflowException)
        {
            Plugin.Log?.LogAutomataWarning(
                $"Auto Concept action failed at the native boundary: {ex.GetBaseException().Message}.");
            return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        }

        Narrate(in action, in submission);
        return Map(in submission);
    }

    private static ServiceActionResult Map(in AutoConceptSubmission submission)
    {
        switch (submission.Preflight)
        {
            case AutoConceptPreflight.ContractUnavailable:
                return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
            case AutoConceptPreflight.RecipeIdentityChanged:
                return ServiceActionResult.Rejected(AutoConceptActionResultCodes.RecipeIdentityChanged);
            case AutoConceptPreflight.AssignmentUnsettled:
                return ServiceActionResult.Rejected(AutoConceptActionResultCodes.AssignmentUnsettled);
            case AutoConceptPreflight.OwnershipChanged:
                return ServiceActionResult.Rejected(AutoConceptActionResultCodes.OwnershipChanged);
            case AutoConceptPreflight.SlotUnavailable:
                return ServiceActionResult.Rejected(AutoConceptActionResultCodes.SlotUnavailable);
            case AutoConceptPreflight.ProjectionRefused:
                return ServiceActionResult.Rejected(AutoConceptActionResultCodes.ProjectionRefused);
            case AutoConceptPreflight.MasteryLimitChanged:
                return ServiceActionResult.Rejected(AutoConceptActionResultCodes.MasteryLimitChanged);
            case AutoConceptPreflight.ResourceBackpressure:
                return ServiceActionResult.Skipped(AutoConceptActionResultCodes.AmountUnavailable);
        }

        var evidence = ServiceNativeMutationEvidence.Observed(
            submission.Outcome, submission.CallOutcome);
        return submission.Verified
            ? ServiceActionResult.Committed(CommonActionResultCodes.Committed, evidence)
            : ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault, evidence);
    }

    private static void Narrate(
        in AutoConceptCycleAction action,
        in AutoConceptSubmission submission)
    {
        if (submission.Verified ||
            submission.Preflight == AutoConceptPreflight.ResourceBackpressure) return;
        var replacement = action.ReplacementId == Guid.Empty
            ? string.Empty
            : $" with replacement {EntityIdentityFormatter.Format(action.ReplacementId)}";
        var message =
            $"Auto Concept did not complete {action.Kind} for {EntityIdentityFormatter.Format(action.RecipeId)}{replacement}: " +
            submission.Reason;
        Plugin.Log?.LogAutomataWarning(message);
    }

    private bool Owns()
    {
        try
        {
            return _ownsActionFamily();
        }
        catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException)
        {
            return false;
        }
    }

    private bool NativeEpochMatches(long plannedEpoch)
    {
        try
        {
            var current = _readLifecycleEpoch();
            return current > 0 && current == plannedEpoch;
        }
        catch (Exception ex) when (
            ex is TargetInvocationException or ArgumentException or InvalidOperationException or
            TargetException or MemberAccessException)
        {
            return false;
        }
    }
}
