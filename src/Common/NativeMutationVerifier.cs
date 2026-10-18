using System;

namespace OrbModding.Common;

public enum NativeMutationOutcome
{
    Verified,
    BeforeCaptureFailed,
    ExecutionThrew,
    AfterCaptureFailed,
    PostconditionFailed
}

public readonly struct NativeMutationEvidence<TState>
{
    internal NativeMutationEvidence(
        string feature,
        string identity,
        string expectedChange,
        NativeMutationOutcome outcome,
        bool hasBefore,
        TState before,
        bool hasAfter,
        TState after,
        string detail)
    {
        Feature = feature;
        Identity = identity;
        ExpectedChange = expectedChange;
        Outcome = outcome;
        HasBefore = hasBefore;
        Before = before;
        HasAfter = hasAfter;
        After = after;
        Detail = detail;
    }

    public string Feature { get; }
    public string Identity { get; }
    public string ExpectedChange { get; }
    public NativeMutationOutcome Outcome { get; }
    public bool HasBefore { get; }
    public TState Before { get; }
    public bool HasAfter { get; }
    public TState After { get; }
    public string Detail { get; }
    public bool IsVerified => Outcome == NativeMutationOutcome.Verified;
    public bool MutationWasAttempted => Outcome != NativeMutationOutcome.BeforeCaptureFailed;

    public string Format(Func<TState, string>? formatState = null)
    {
        formatState ??= value => value?.ToString() ?? "<null>";
        var before = HasBefore ? formatState(Before) : "<unavailable>";
        var after = HasAfter ? formatState(After) : "<unavailable>";
        return $"feature={Feature}; identity={Identity}; outcome={Outcome}; expected={ExpectedChange}; before={before}; after={after}; detail={Detail}";
    }
}

public static class NativeMutationVerifier
{
    public static NativeMutationEvidence<TState> Execute<TState>(
        string feature,
        string identity,
        string expectedChange,
        Func<TState> capture,
        Action execute,
        Func<TState, TState, bool> verify)
    {
        if (string.IsNullOrWhiteSpace(feature)) throw new ArgumentException("A feature name is required.", nameof(feature));
        if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("A mutation identity is required.", nameof(identity));
        if (string.IsNullOrWhiteSpace(expectedChange)) throw new ArgumentException("An expected change is required.", nameof(expectedChange));
        if (capture is null) throw new ArgumentNullException(nameof(capture));
        if (execute is null) throw new ArgumentNullException(nameof(execute));
        if (verify is null) throw new ArgumentNullException(nameof(verify));

        TState before;
        try
        {
            before = capture();
        }
        catch (Exception ex)
        {
            return Evidence<TState>(
                feature,
                identity,
                expectedChange,
                NativeMutationOutcome.BeforeCaptureFailed,
                false,
                default!,
                false,
                default!,
                ExceptionDetail.Describe(ex));
        }

        return ExecuteAfterCapture(
            feature,
            identity,
            expectedChange,
            before,
            capture,
            execute,
            verify);
    }

    public static NativeMutationEvidence<TState> ExecuteAfterCapture<TState>(
        string feature,
        string identity,
        string expectedChange,
        TState before,
        Func<TState> captureAfter,
        Action execute,
        Func<TState, TState, bool> verify)
    {
        if (string.IsNullOrWhiteSpace(feature)) throw new ArgumentException("A feature name is required.", nameof(feature));
        if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("A mutation identity is required.", nameof(identity));
        if (string.IsNullOrWhiteSpace(expectedChange)) throw new ArgumentException("An expected change is required.", nameof(expectedChange));
        if (captureAfter is null) throw new ArgumentNullException(nameof(captureAfter));
        if (execute is null) throw new ArgumentNullException(nameof(execute));
        if (verify is null) throw new ArgumentNullException(nameof(verify));

        try
        {
            execute();
        }
        catch (Exception executionException)
        {
            try
            {
                var afterException = captureAfter();
                if (Holds(verify, before, afterException))
                {
                    return Evidence<TState>(
                        feature,
                        identity,
                        expectedChange,
                        NativeMutationOutcome.Verified,
                        true,
                        before,
                        true,
                        afterException,
                        "postcondition verified after the game threw " +
                        ExceptionDetail.Describe(executionException) + "." +
                        GameActionFaultLog.Record(executionException, feature + " for " + identity));
                }
                return Evidence<TState>(
                    feature,
                    identity,
                    expectedChange,
                    NativeMutationOutcome.ExecutionThrew,
                    true,
                    before,
                    true,
                    afterException,
                    ExceptionDetail.Describe(executionException));
            }
            catch (Exception captureException)
            {
                return Evidence<TState>(
                    feature,
                    identity,
                    expectedChange,
                    NativeMutationOutcome.ExecutionThrew,
                    true,
                    before,
                    false,
                    default!,
                    $"{ExceptionDetail.Describe(executionException)}; after capture failed: {ExceptionDetail.Describe(captureException)}");
            }
        }

        TState after;
        try
        {
            after = captureAfter();
        }
        catch (Exception ex)
        {
            return Evidence<TState>(
                feature,
                identity,
                expectedChange,
                NativeMutationOutcome.AfterCaptureFailed,
                true,
                before,
                false,
                default!,
                ExceptionDetail.Describe(ex));
        }

        bool verified;
        try
        {
            verified = verify(before, after);
        }
        catch (Exception ex)
        {
            return Evidence<TState>(
                feature,
                identity,
                expectedChange,
                NativeMutationOutcome.PostconditionFailed,
                true,
                before,
                true,
                after,
                $"postcondition evaluation failed: {ExceptionDetail.Describe(ex)}");
        }

        return Evidence<TState>(
            feature,
            identity,
            expectedChange,
            verified ? NativeMutationOutcome.Verified : NativeMutationOutcome.PostconditionFailed,
            true,
            before,
            true,
            after,
            verified ? "postcondition verified" : "captured state did not match the expected change");
    }

    /// <summary>
    /// Reads the after-state once a native mutation call has run, for a GameAction that classifies
    /// its own outcome. An unreadable after-state comes back unread instead of thrown, so a change
    /// that may have landed is classified as a fault and never escapes as a refusal.
    /// </summary>
    public static bool TryReadAfterCall<T>(Func<T> read, Func<Exception, bool> isExpected, out T value)
    {
        if (read is null) throw new ArgumentNullException(nameof(read));
        if (isExpected is null) throw new ArgumentNullException(nameof(isExpected));
        try
        {
            value = read();
            return true;
        }
        catch (Exception exception) when (isExpected(exception))
        {
            value = default!;
            return false;
        }
    }

    private static bool Holds<TState>(Func<TState, TState, bool> verify, TState before, TState after)
    {
        try
        {
            return verify(before, after);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static NativeMutationEvidence<TState> Evidence<TState>(
        string feature,
        string identity,
        string expectedChange,
        NativeMutationOutcome outcome,
        bool hasBefore,
        TState before,
        bool hasAfter,
        TState after,
        string detail) =>
        new(
            feature,
            identity,
            expectedChange,
            outcome,
            hasBefore,
            before,
            hasAfter,
            after,
            detail);
}
