using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal sealed class AutoScribeActionHealth
{
    internal bool HasFailure { get; private set; }
    internal ServiceActionResultCode Code { get; private set; }
    internal string Reason { get; private set; } = string.Empty;
    internal long Revision { get; private set; }

    internal void Observe(in AutoScribeSubmission submission)
    {
        var outcome = submission.Outcome;
        if (outcome.Verified)
        {
            Clear();
            return;
        }
        if (!IsFailure(in submission) || AutoScribeServicePolicies.PaidWithoutWork(outcome.Code)) return;
        if (HasFailure &&
            Code == outcome.Code &&
            string.Equals(Reason, outcome.Reason, System.StringComparison.Ordinal))
            return;
        HasFailure = true;
        Code = outcome.Code;
        Reason = outcome.Reason;
        Revision = checked(Revision + 1);
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
