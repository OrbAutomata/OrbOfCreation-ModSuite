namespace OrbAutomata;

internal enum AutoScribeSubmissionClass
{
    Verified = 0,
    Backpressure = 1,
    Failure = 2,
}

internal static class AutoScribeSubmissionPolicy
{
    /// <summary>
    /// Waiting for another publication is quiet backpressure. A broken or ambiguous native
    /// contract, or any attempted mutation that did not verify, is a loud failure. Health and the
    /// ServiceCycle adapter deliberately share this classification.
    /// </summary>
    internal static AutoScribeSubmissionClass Classify(in AutoScribeSubmission submission)
    {
        if (submission.Verified) return AutoScribeSubmissionClass.Verified;
        if (submission.CallOutcome.MutationAttempts > 0)
            return AutoScribeSubmissionClass.Failure;
        return submission.Preflight switch
        {
            AutoScribePreflight.RecipeUnavailable or
            AutoScribePreflight.TargetUnavailable or
            AutoScribePreflight.QueueFull or
            AutoScribePreflight.CompetingSupply or
            AutoScribePreflight.Unaffordable or
            AutoScribePreflight.MutationPermitUnavailable or
            AutoScribePreflight.LifecycleReplaced => AutoScribeSubmissionClass.Backpressure,
            AutoScribePreflight.IdentityUnavailable when submission.Retryable =>
                AutoScribeSubmissionClass.Backpressure,
            _ => AutoScribeSubmissionClass.Failure,
        };
    }
}
