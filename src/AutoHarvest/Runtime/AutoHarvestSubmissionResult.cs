using System;
using OrbModding.Common;

namespace OrbAutomata;

// Numeric values are append-only structured diagnostics contracts.
internal enum AutoHarvestSubmissionFailureCode
{
    None = 0,
    RuntimeReadFailed = 4,
    PolicyRevalidationRejected = 5,
    NativePrerequisitesCurrentlyUnmet = 6,
    NativePrerequisiteValidationUnavailable = 7,
    NativePairIdentityRevalidationRefused = 8,
    NativePlotVisibilityRefused = 9,
    NativeOfferedInstanceMembershipRefused = 10,
    NativeActionRowVisibilityRefused = 11,
    NativeHasEnoughForOneInstanceRefused = 12,
    NativeMaximumRemainingInstancesRefused = 13,
}

internal readonly struct AutoHarvestSubmissionResult
{
    public AutoHarvestSubmissionResult(
        AutoHarvestSubmissionFailureCode failureCode)
        : this(false, default, default, failureCode)
    {
    }

    public AutoHarvestSubmissionResult(
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome)
        : this(true, outcome, callOutcome, AutoHarvestSubmissionFailureCode.None)
    {
    }

    private AutoHarvestSubmissionResult(
        bool hasNativeMutationOutcome,
        NativeMutationOutcome nativeMutationOutcome,
        NativeMutationCallOutcome nativeMutationCallOutcome,
        AutoHarvestSubmissionFailureCode failureCode)
    {
        if (hasNativeMutationOutcome && failureCode != AutoHarvestSubmissionFailureCode.None)
            throw new ArgumentException("Native mutation evidence cannot carry a preflight failure code.", nameof(failureCode));
        if (!hasNativeMutationOutcome && failureCode == AutoHarvestSubmissionFailureCode.None)
            throw new ArgumentException("A preflight rejection requires a structured failure code.", nameof(failureCode));
        HasNativeMutationOutcome = hasNativeMutationOutcome;
        NativeMutationOutcome = nativeMutationOutcome;
        NativeMutationCallOutcome = nativeMutationCallOutcome;
        FailureCode = failureCode;
    }

    public bool HasNativeMutationOutcome { get; }
    public NativeMutationOutcome NativeMutationOutcome { get; }
    public NativeMutationCallOutcome NativeMutationCallOutcome { get; }
    public AutoHarvestSubmissionFailureCode FailureCode { get; }
    public bool Verified => HasNativeMutationOutcome && NativeMutationOutcome == NativeMutationOutcome.Verified;
    public bool MutationAttempted => NativeMutationCallOutcome.MutationAttempts != 0;
}
