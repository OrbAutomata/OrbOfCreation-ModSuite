namespace OrbModding.Common;

/// <summary>
/// Exact outcome observed at an audited native mutation boundary. A rejected
/// adapter preflight reports zeroes; an invoked but unverified call reports an
/// attempt without a commit.
/// </summary>
public readonly struct NativeMutationCallOutcome
{
    public NativeMutationCallOutcome(
        int nativeCallsAttempted,
        int mutationAttempts,
        int mutationsCommitted)
    {
        NativeCallsAttempted = nativeCallsAttempted;
        MutationAttempts = mutationAttempts;
        MutationsCommitted = mutationsCommitted;
    }

    public int NativeCallsAttempted { get; }
    public int MutationAttempts { get; }
    public int MutationsCommitted { get; }

    public static NativeMutationCallOutcome FromEvidence<TState>(
        NativeMutationEvidence<TState> evidence,
        int nativeCallsAttempted = 1) =>
        evidence.MutationWasAttempted
            ? new NativeMutationCallOutcome(nativeCallsAttempted, 1, evidence.IsVerified ? 1 : 0)
            : default;

    public NativeMutationCallOutcome Add(NativeMutationCallOutcome other) =>
        new(
            NativeCallsAttempted + other.NativeCallsAttempted,
            MutationAttempts + other.MutationAttempts,
            MutationsCommitted + other.MutationsCommitted);

}

/// <summary>
/// Optional contract for production adapters that can distinguish preflight
/// rejection from an actual audited native invocation.
/// </summary>
public interface INativeMutationOutcomeSource
{
    NativeMutationCallOutcome LastNativeMutationOutcome { get; }
}
