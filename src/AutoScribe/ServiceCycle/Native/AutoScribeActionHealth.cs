using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal sealed class AutoScribeActionHealth
{
    internal bool HasFailure { get; private set; }
    internal ServiceActionResultCode Code { get; private set; }
    internal string Reason { get; private set; } = string.Empty;
    internal long Revision { get; private set; }

    /// <returns>True only when callers should emit a new failure warning.</returns>
    internal bool Observe(in AutoScribeSubmission submission)
    {
        var outcome = submission.Outcome;
        if (outcome.Verified)
        {
            Clear();
            return false;
        }
        if (!IsFailure(in submission)) return false;

        // Quarantine is the persistent consequence of its root failure, not a new failure on every
        // publication. Suppress only that same root: a different intervening failure must not hide
        // the boundary's current quarantine state.
        if (outcome.Code == AutoScribeActionResultCodes.Quarantined &&
            HasFailure &&
            (Code == AutoScribeActionResultCodes.PostPaymentFault ||
                Code == AutoScribeActionResultCodes.VerificationFailed ||
                Code == AutoScribeActionResultCodes.Quarantined) &&
            string.Equals(Reason, outcome.Reason, System.StringComparison.Ordinal))
            return false;
        if (HasFailure &&
            Code == outcome.Code &&
            string.Equals(Reason, outcome.Reason, System.StringComparison.Ordinal))
            return false;
        HasFailure = true;
        Code = outcome.Code;
        Reason = outcome.Reason;
        Revision = checked(Revision + 1);
        return true;
    }

    internal void Clear()
    {
        if (!HasFailure && Reason.Length == 0) return;
        HasFailure = false;
        Code = CommonActionResultCodes.Committed;
        Reason = string.Empty;
        Revision = checked(Revision + 1);
    }

    internal void InvalidateLifecycle() => Clear();

    internal static bool IsFailure(in AutoScribeSubmission submission) =>
        submission.Outcome.ToResult().Disposition == ServiceActionDisposition.Faulted;
}
