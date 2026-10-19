using System;
using System.Runtime.ExceptionServices;

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
        string detail,
        Exception? exception,
        Exception? putBackException = null)
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
        Exception = exception;
        PutBackException = putBackException;
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

    /// <summary>
    /// What the game threw, so a GameAction can log it under the reference its answer names: the
    /// native call's exception when the call threw, otherwise the failed read or postcondition's.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// What the put-back threw after the call had already thrown. <see cref="Exception"/> stays the
    /// cause; this one is logged beside it and never replaces it.
    /// </summary>
    public Exception? PutBackException { get; }

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
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
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
                ExceptionDetail.Describe(ex),
                ex);
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

    /// <summary>
    /// Makes the native call and classifies it from the after-state.
    /// </summary>
    /// <remarks>
    /// Every throw after the call is classified first: the after-state is read and the outcome is
    /// decided. Only then does an exception outside <see cref="NativeFailureClassifier"/> escape, so a
    /// suite defect stays loud. A change that landed before such a throw commits instead, the throw is
    /// logged with a reference, and in a perf-debug build that reference becomes the feature's latest
    /// on <c>suite_health</c>.
    /// <para>
    /// <paramref name="putBackOnThrow"/> runs only when <paramref name="execute"/> threw, before the
    /// after-state is read. The call's exception stays the cause; what the put-back throws is carried
    /// beside it and classified the same way as an after-state read that throws.
    /// </para>
    /// </remarks>
    public static NativeMutationEvidence<TState> ExecuteAfterCapture<TState>(
        string feature,
        string identity,
        string expectedChange,
        TState before,
        Func<TState> captureAfter,
        Action execute,
        Func<TState, TState, bool> verify,
        Action? putBackOnThrow = null)
    {
        if (string.IsNullOrWhiteSpace(feature)) throw new ArgumentException("A feature name is required.", nameof(feature));
        if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("A mutation identity is required.", nameof(identity));
        if (string.IsNullOrWhiteSpace(expectedChange)) throw new ArgumentException("An expected change is required.", nameof(expectedChange));
        if (captureAfter is null) throw new ArgumentNullException(nameof(captureAfter));
        if (execute is null) throw new ArgumentNullException(nameof(execute));
        if (verify is null) throw new ArgumentNullException(nameof(verify));

        var evidence = Classify(
            feature, identity, expectedChange, before, captureAfter, execute, verify, putBackOnThrow,
            out var unexpected);
        if (unexpected is not null && !evidence.IsVerified)
        {
            if (evidence.PutBackException is { } putBackException)
                GameActionFaultLog.RecordReference(evidence.Exception, feature + " for " + identity, putBackException);
            ExceptionDispatchInfo.Capture(unexpected).Throw();
        }
        return evidence;
    }

    /// <summary>
    /// Reads the after-state once a native mutation call has run, for a GameAction that classifies
    /// its own outcome. An unreadable after-state comes back unread instead of thrown, so a change
    /// that may have landed is classified as a fault and never escapes as a refusal.
    /// </summary>
    public static bool TryReadAfterCall<T>(Func<T> read, out T value)
    {
        if (read is null) throw new ArgumentNullException(nameof(read));
        try
        {
            value = read();
            return true;
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            value = default!;
            return false;
        }
    }

    private static NativeMutationEvidence<TState> Classify<TState>(
        string feature,
        string identity,
        string expectedChange,
        TState before,
        Func<TState> captureAfter,
        Action execute,
        Func<TState, TState, bool> verify,
        Action? putBackOnThrow,
        out Exception? unexpected)
    {
        unexpected = null;
        try
        {
            execute();
        }
        catch (Exception executionException)
        {
            unexpected = Unexpected(executionException);
            var putBackException = PutBack(putBackOnThrow);
            if (putBackException is not null) unexpected ??= Unexpected(putBackException);
            TState afterException;
            try
            {
                afterException = captureAfter();
            }
            catch (Exception captureException)
            {
                unexpected ??= Unexpected(captureException);
                return Evidence<TState>(
                    feature,
                    identity,
                    expectedChange,
                    NativeMutationOutcome.ExecutionThrew,
                    true,
                    before,
                    false,
                    default!,
                    $"{ExceptionDetail.Describe(executionException)}; after capture failed: {ExceptionDetail.Describe(captureException)}",
                    executionException,
                    putBackException);
            }

            bool holds;
            try
            {
                holds = verify(before, afterException);
            }
            catch (Exception verifyException)
            {
                unexpected ??= Unexpected(verifyException);
                holds = false;
            }
            if (holds)
            {
                var reference = GameActionFaultLog.RecordReference(
                    executionException, feature + " for " + identity, putBackException);
#if SERVICE_CYCLE_PROFILE
                CommittedAfterThrow.Record(feature, reference);
#endif
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
                    GameActionFaultLog.Sentence(reference),
                    executionException,
                    putBackException);
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
                ExceptionDetail.Describe(executionException),
                executionException,
                putBackException);
        }

        TState after;
        try
        {
            after = captureAfter();
        }
        catch (Exception ex)
        {
            unexpected = Unexpected(ex);
            return Evidence<TState>(
                feature,
                identity,
                expectedChange,
                NativeMutationOutcome.AfterCaptureFailed,
                true,
                before,
                false,
                default!,
                ExceptionDetail.Describe(ex),
                ex);
        }

        bool verified;
        try
        {
            verified = verify(before, after);
        }
        catch (Exception ex)
        {
            unexpected = Unexpected(ex);
            return Evidence<TState>(
                feature,
                identity,
                expectedChange,
                NativeMutationOutcome.PostconditionFailed,
                true,
                before,
                true,
                after,
                $"postcondition evaluation failed: {ExceptionDetail.Describe(ex)}",
                ex);
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
            verified ? "postcondition verified" : "captured state did not match the expected change",
            null);
    }

    private static Exception? PutBack(Action? putBack)
    {
        if (putBack is null) return null;
        try
        {
            putBack();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static Exception? Unexpected(Exception exception) =>
        NativeFailureClassifier.IsExpected(exception) ? null : exception;

    private static NativeMutationEvidence<TState> Evidence<TState>(
        string feature,
        string identity,
        string expectedChange,
        NativeMutationOutcome outcome,
        bool hasBefore,
        TState before,
        bool hasAfter,
        TState after,
        string detail,
        Exception? exception,
        Exception? putBackException = null) =>
        new(
            feature,
            identity,
            expectedChange,
            outcome,
            hasBefore,
            before,
            hasAfter,
            after,
            detail,
            exception,
            putBackException);
}
