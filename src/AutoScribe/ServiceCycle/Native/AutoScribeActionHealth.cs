namespace OrbAutomata;

internal sealed class AutoScribeActionHealth
{
    internal bool HasFailure { get; private set; }
    internal AutoScribePreflight Preflight { get; private set; }
    internal AutoScribeNativeStage Stage { get; private set; }
    internal string Reason { get; private set; } = string.Empty;
    internal long Revision { get; private set; }

    /// <returns>True only when callers should emit a new failure warning.</returns>
    internal bool Observe(in AutoScribeSubmission submission)
    {
        if (submission.Verified)
        {
            Clear();
            return false;
        }
        if (!IsFailure(in submission)) return false;

        // Quarantine is the persistent consequence of its root failure, not a new failure on every
        // publication. Suppress only that same root: a different intervening failure must not hide
        // the boundary's current quarantine state.
        if (submission.Preflight == AutoScribePreflight.Quarantined &&
            HasFailure &&
            (Preflight is AutoScribePreflight.PostPaymentFault or
                AutoScribePreflight.VerificationFailed or
                AutoScribePreflight.Quarantined) &&
            string.Equals(Reason, submission.Reason, System.StringComparison.Ordinal))
            return false;
        if (HasFailure &&
            Preflight == submission.Preflight &&
            Stage == submission.Stage &&
            string.Equals(Reason, submission.Reason, System.StringComparison.Ordinal))
            return false;
        HasFailure = true;
        Preflight = submission.Preflight;
        Stage = submission.Stage;
        Reason = submission.Reason;
        Revision = checked(Revision + 1);
        return true;
    }

    internal void Clear()
    {
        if (!HasFailure && Reason.Length == 0) return;
        HasFailure = false;
        Preflight = AutoScribePreflight.Proceeded;
        Stage = AutoScribeNativeStage.None;
        Reason = string.Empty;
        Revision = checked(Revision + 1);
    }

    internal void InvalidateLifecycle() => Clear();

    internal static bool IsFailure(in AutoScribeSubmission submission) =>
        AutoScribeSubmissionPolicy.Classify(in submission) ==
        AutoScribeSubmissionClass.Failure;
}
