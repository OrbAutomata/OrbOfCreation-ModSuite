using System;
using System.Diagnostics.CodeAnalysis;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common;

/// <summary>
/// The lifecycle-scoped shell of a GameAction: one complete binding set per lifecycle, the
/// thread, bindings and epoch prologue, the expected-failure catch around admission, the mutation
/// permit, and the one post-call classification on <see cref="NativeMutationVerifier"/>. A feature
/// supplies its binding factory, screen, admission, native call and one sentinel.
/// </summary>
internal abstract class NativeGameAction<TBindings> : IDisposable
    where TBindings : class
{
    internal delegate bool Bind(out TBindings? bindings, out string reason);

    private readonly string _feature;
    private readonly string _screen;
    private readonly GameActionCodes _codes;
    private readonly Func<bool> _tryPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Bind _bind;
    private readonly int _mainThreadId;
    private TBindings? _bindings;
    private string _bindingFailure = string.Empty;

    /// <param name="feature">The first word of every fault sentence, such as "Structure".</param>
    /// <param name="screen">The game's name for the page a caller should look at.</param>
    /// <param name="bind">
    /// Builds the binding set. It runs from this constructor, before the derived constructor body,
    /// so it closes over constructor parameters and never over the derived instance.
    /// </param>
    protected NativeGameAction(
        string feature,
        string screen,
        GameActionCodes codes,
        Func<long> readLifecycleEpoch,
        Func<bool> tryPermit,
        Func<string> readOwnershipFailure,
        Bind bind)
    {
        _feature = feature ?? throw new ArgumentNullException(nameof(feature));
        _screen = screen ?? throw new ArgumentNullException(nameof(screen));
        _codes = codes;
        ReadLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryPermit = tryPermit ?? throw new ArgumentNullException(nameof(tryPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _bind = bind ?? throw new ArgumentNullException(nameof(bind));
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    protected Func<long> ReadLifecycleEpoch { get; }

    internal void InvalidateLifecycle()
    {
        OnInvalidate();
        BindLifecycle();
    }

    public void Dispose()
    {
        OnInvalidate();
        _bindings = null;
        _bindingFailure = string.Empty;
    }

    /// <summary>
    /// Drops lifecycle-scoped state the feature owns beside its bindings, on invalidate and on
    /// dispose alike.
    /// </summary>
    protected virtual void OnInvalidate()
    {
    }

    protected NativeActionOutcome Admit(
        long plannedEpoch,
        Func<TBindings, NativeActionOutcome> admission) =>
        Admit(plannedEpoch, static outcome => outcome, admission);

    /// <summary>
    /// Runs <paramref name="admission"/> on the Unity main thread, against this lifecycle's
    /// bindings, in the run the action was planned for. An expected throw from admission is a
    /// refusal; the native call itself never reaches that catch, because
    /// <c>Execute</c> classifies every throw after it.
    /// </summary>
    /// <param name="answer">Wraps a prologue answer in the feature's submission.</param>
    protected TSubmission Admit<TSubmission>(
        long plannedEpoch,
        Func<NativeActionOutcome, TSubmission> answer,
        Func<TBindings, TSubmission> admission)
    {
        if (!TryEnter(plannedEpoch, out var native, out var refusal)) return answer(refusal);
        try
        {
            return admission(native);
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            return answer(NativeActionOutcome.Reject(_codes.ContractUnavailable,
                GameActionAnswer.CouldNotRead(_screen, exception)));
        }
    }

    /// <summary>
    /// The prologue every entry point shares, admission and read alike: the Unity main thread,
    /// this lifecycle's bindings, and the run the caller planned for.
    /// </summary>
    protected bool TryEnter(
        long plannedEpoch,
        [NotNullWhen(true)] out TBindings? bindings,
        out NativeActionOutcome refusal)
    {
        bindings = null;
        if (Environment.CurrentManagedThreadId != _mainThreadId)
        {
            refusal = NativeActionOutcome.Reject(_codes.WrongThread, GameActionAnswer.SuiteStopped());
            return false;
        }
        if (_bindings is not { } native)
        {
            refusal = NativeActionOutcome.Reject(_codes.ContractUnavailable,
                GameActionAnswer.NotAttached(_screen, _bindingFailure));
            return false;
        }
        long epoch;
        try
        {
            epoch = ReadLifecycleEpoch();
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            refusal = NativeActionOutcome.FaultBeforeCall(_codes.ContractUnavailable,
                GameActionAnswer.CouldNotRead(_screen, exception));
            return false;
        }
        if (plannedEpoch != epoch)
        {
            refusal = NativeActionOutcome.Reject(CommonActionResultCodes.LifecycleReplaced,
                GameActionAnswer.RunChanged());
            return false;
        }
        bindings = native;
        refusal = default;
        return true;
    }

    protected bool TryPermit(out NativeActionOutcome refusal)
    {
        if (_tryPermit())
        {
            refusal = default;
            return true;
        }
        refusal = NativeActionOutcome.Reject(_codes.MutationPermitUnavailable, _readOwnershipFailure());
        return false;
    }

    /// <summary>
    /// Makes the native call and answers from the one sentinel: committed when
    /// <paramref name="landed"/> holds on the after-state, whether or not the game threw.
    /// </summary>
    /// <param name="target">The player's name for what was pressed, as the fault sentence names it.</param>
    /// <param name="success">What a committed answer says is now visible.</param>
    protected NativeActionOutcome Execute<TState>(
        string target,
        string success,
        TState before,
        Func<TState> captureAfter,
        Action call,
        Func<TState, TState, bool> landed) =>
        Execute(target, success, before, captureAfter,
            () =>
            {
                call();
                return null;
            },
            landed);

    /// <summary>
    /// The same, for a call of several presses that can stop partway: every press runs inside
    /// <paramref name="call"/>, and one sentinel answers for the whole effect. A stop answers only
    /// when the sentinel does not hold and nothing threw, and it never answers with zero attempts.
    /// </summary>
    protected NativeActionOutcome Execute<TState>(
        string target,
        string success,
        TState before,
        Func<TState> captureAfter,
        Func<NativeStop?> call,
        Func<TState, TState, bool> landed)
    {
        NativeStop? stop = null;
        var evidence = NativeMutationVerifier.ExecuteAfterCapture(
            _feature, target, success, before, captureAfter, () => stop = call(), landed);
        var calls = NativeMutationCallOutcome.FromEvidence(evidence);
        if (evidence.IsVerified) return NativeActionOutcome.Commit(calls, success);
        var stage = evidence.Outcome == NativeMutationOutcome.ExecutionThrew ? "NativeCallback" : "Verification";
        if (evidence.Exception is { } exception)
            return NativeActionOutcome.Fault(_codes.PostCommitFault, evidence.Outcome, calls,
                FaultReason(stage, target, GameActionAnswer.GameErrored(_screen, exception)));
        if (stop is not { } stopped)
            return NativeActionOutcome.Fault(_codes.VerificationFailed, evidence.Outcome, calls,
                FaultReason(stage, target, GameActionAnswer.ChangeNotSeen(_screen)));
        if (stopped.Undone is null)
            return NativeActionOutcome.Fault(stopped.Code, evidence.Outcome, calls,
                FaultReason(stage, target, stopped.Reason));
        return NativeMutationVerifier.TryReadAfterCall(stopped.Undone, out var undone) && undone
            ? NativeActionOutcome.RefusedAfterUndo(stopped.Code, calls,
                stopped.Reason + " " + GameActionAnswer.Undone())
            : NativeActionOutcome.Fault(_codes.VerificationFailed, evidence.Outcome, calls,
                FaultReason(stage, target, stopped.Reason + " " + GameActionAnswer.NotUndone(_screen)));
    }

    private string FaultReason(string stage, string target, string reason) =>
        _feature + " " + stage + " failed on " + target + ": " + reason;

    private void BindLifecycle()
    {
        if (_bind(out var bindings, out var reason))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
            return;
        }
        _bindings = null;
        _bindingFailure = reason;
    }
}
