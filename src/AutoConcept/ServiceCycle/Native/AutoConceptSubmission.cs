using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct AutoConceptSubmission
{
    private AutoConceptSubmission(
        NativeActionOutcome outcome,
        int appliedDelta = 0,
        int maximumAmount = 0)
    {
        Outcome = outcome;
        AppliedDelta = appliedDelta;
        MaximumAmount = maximumAmount;
    }

    public NativeActionOutcome Outcome { get; }
    public int AppliedDelta { get; }
    public int MaximumAmount { get; }

    internal static AutoConceptSubmission Rejected(ServiceActionResultCode code, string reason) =>
        new(NativeActionOutcome.Reject(code, reason));

    internal static AutoConceptSubmission Backpressure(string reason, int maximumAmount = 0) =>
        new(
            NativeActionOutcome.Skip(AutoConceptActionResultCodes.AmountUnavailable, reason),
            maximumAmount: maximumAmount);

    internal static AutoConceptSubmission FaultBeforeCall(string reason) =>
        new(NativeActionOutcome.FaultBeforeCall(CommonActionResultCodes.AdapterFault, reason));

    internal static AutoConceptSubmission Attempted(
        NativeMutationCallOutcome outcome,
        NativeMutationOutcome mutationOutcome,
        GameActionReason reason,
        int appliedDelta) =>
        new(
            outcome.MutationAttempts == 1 && mutationOutcome == NativeMutationOutcome.Verified
                ? NativeActionOutcome.Commit(outcome, reason)
                : NativeActionOutcome.Fault(
                    CommonActionResultCodes.AdapterFault, mutationOutcome, outcome, reason),
            appliedDelta);
}

internal interface IAutoConceptNativePort
{
    AutoConceptSubmission Submit(
        in AutoConceptCycleAction action,
        AutoConceptResourceLimits? limits);
}

/// <summary>
/// The resource limits Auto Concept's own cycle holds an assignment back for.
/// </summary>
/// <remarks>
/// A manual press is handed none. The rate reserve is the worker's backpressure, not a gate the
/// game applies to a player pressing Add.
/// </remarks>
internal readonly record struct AutoConceptResourceLimits(float RateReservePercent)
{
    internal static AutoConceptResourceLimits From(AutoConceptConfiguration config) =>
        new(config.RateReservePercent);
}
