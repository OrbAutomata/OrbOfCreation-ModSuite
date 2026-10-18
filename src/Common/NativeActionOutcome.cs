using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common;

/// <summary>
/// What one GameAction submission did at the native boundary: the stable code, what the
/// verifier observed, the native-call counts and the exact reason. Every submission carries one,
/// and <see cref="ToResult"/> is the one mapping from it to a service-cycle result.
/// </summary>
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

    private NativeActionOutcome(
        Kind kind,
        ServiceActionResultCode code,
        NativeMutationOutcome mutation,
        NativeMutationCallOutcome callOutcome,
        string reason)
    {
        _kind = kind;
        Code = code;
        Mutation = mutation;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
    }

    public ServiceActionResultCode Code { get; }
    public NativeMutationOutcome Mutation { get; }
    public NativeMutationCallOutcome CallOutcome { get; }
    public string Reason { get; }
    public bool Verified => _kind == Kind.Commit;

    public static NativeActionOutcome Reject(ServiceActionResultCode code, string reason) =>
        new(Kind.Reject, code, NativeMutationOutcome.BeforeCaptureFailed, default, reason);

    /// <summary>
    /// A stop that is ordinary staleness rather than a refusal: the plan raced the game, and the
    /// service tries again without a penalty.
    /// </summary>
    public static NativeActionOutcome Skip(ServiceActionResultCode code, string reason) =>
        new(Kind.Skip, code, NativeMutationOutcome.BeforeCaptureFailed, default, reason);

    public static NativeActionOutcome Commit(NativeMutationCallOutcome callOutcome, string reason) =>
        new(Kind.Commit, CommonActionResultCodes.Committed, NativeMutationOutcome.Verified,
            callOutcome, reason);

    /// <summary>A fault backed by what the verifier observed, including a failed before-read.</summary>
    public static NativeActionOutcome Fault(
        ServiceActionResultCode code,
        NativeMutationOutcome mutation,
        NativeMutationCallOutcome callOutcome,
        string reason) =>
        new(Kind.Fault, code, mutation, callOutcome, reason);

    /// <summary>A fault with no native evidence: the contract failed before any call was made.</summary>
    public static NativeActionOutcome FaultBeforeCall(ServiceActionResultCode code, string reason) =>
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
