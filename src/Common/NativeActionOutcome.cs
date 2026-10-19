using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common;

/// <summary>
/// What one GameAction submission did at the native boundary: the stable code, what the
/// verifier observed, the native-call counts and the exact reason. Every submission carries one,
/// and <see cref="ToResult"/> is the one mapping from it to a service-cycle result.
/// </summary>
/// <remarks>
/// The reason keeps its authored <see cref="Sentence"/> apart from the suite-log reference behind
/// it. <see cref="Reason"/> is the two together, for every surface a person reads.
/// </remarks>
public readonly struct NativeActionOutcome
{
    private enum Kind
    {
        Reject = 1,
        Skip,
        Commit,
        Fault,
        FaultBeforeCall,
    }

    private readonly Kind _kind;
    private readonly GameActionReason _reason;

    private NativeActionOutcome(
        Kind kind,
        ServiceActionResultCode code,
        NativeMutationOutcome mutation,
        NativeMutationCallOutcome callOutcome,
        GameActionReason reason)
    {
        _kind = kind;
        Code = code;
        Mutation = mutation;
        CallOutcome = callOutcome;
        _reason = reason;
    }

    public ServiceActionResultCode Code { get; }
    public NativeMutationOutcome Mutation { get; }
    public NativeMutationCallOutcome CallOutcome { get; }
    public string Sentence => _reason.Sentence;
    public string Reason => _reason.Text;
    public bool Verified => _kind == Kind.Commit;
    public bool IsFault => _kind is Kind.Fault or Kind.FaultBeforeCall;

    public static NativeActionOutcome Reject(ServiceActionResultCode code, GameActionReason reason) =>
        new(Kind.Reject, code, NativeMutationOutcome.BeforeCaptureFailed, default, reason);

    /// <summary>
    /// A refusal that came after the game was pressed and the suite pressed it back, verified by
    /// re-reading: the attempt stays on the call counts, so zero attempts still means no press.
    /// </summary>
    public static NativeActionOutcome RefusedAfterUndo(
        ServiceActionResultCode code,
        NativeMutationCallOutcome callOutcome,
        GameActionReason reason) =>
        new(Kind.Reject, code, NativeMutationOutcome.PostconditionFailed, callOutcome, reason);

    /// <summary>
    /// A stop that is ordinary staleness rather than a refusal: the plan raced the game, and the
    /// service tries again without a penalty.
    /// </summary>
    public static NativeActionOutcome Skip(ServiceActionResultCode code, GameActionReason reason) =>
        new(Kind.Skip, code, NativeMutationOutcome.BeforeCaptureFailed, default, reason);

    public static NativeActionOutcome Commit(NativeMutationCallOutcome callOutcome, GameActionReason reason) =>
        new(Kind.Commit, CommonActionResultCodes.Committed, NativeMutationOutcome.Verified,
            callOutcome, reason);

    /// <summary>A fault backed by what the verifier observed, including a failed before-read.</summary>
    public static NativeActionOutcome Fault(
        ServiceActionResultCode code,
        NativeMutationOutcome mutation,
        NativeMutationCallOutcome callOutcome,
        GameActionReason reason) =>
        new(Kind.Fault, code, mutation, callOutcome, reason);

    /// <summary>A fault with no native evidence: the contract failed before any call was made.</summary>
    public static NativeActionOutcome FaultBeforeCall(ServiceActionResultCode code, GameActionReason reason) =>
        new(Kind.FaultBeforeCall, code, NativeMutationOutcome.BeforeCaptureFailed, default, reason);

    public ServiceActionResult ToResult() => _kind switch
    {
        Kind.Commit => ServiceActionResult.Committed(
            Code,
            ServiceNativeMutationEvidence.Observed(NativeMutationOutcome.Verified, CallOutcome)),
        Kind.Reject => ServiceActionResult.Rejected(Code, Reason),
        Kind.Skip => ServiceActionResult.Skipped(Code, Reason),
        Kind.Fault => ServiceActionResult.Faulted(
            Code,
            ServiceNativeMutationEvidence.Observed(Mutation, CallOutcome),
            Reason),
        _ => ServiceActionResult.Faulted(Code, Reason),
    };
}
