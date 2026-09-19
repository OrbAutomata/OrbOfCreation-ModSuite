using OrbModding.Common;

namespace OrbAutomata;

internal enum PrestigePreflight
{
    Proceeded = 0,
    WrongThread = 1,
    LifecycleReplaced = 2,
    ContractUnavailable = 3,
    WorldCycleIncomplete = 5,
    ChallengesNotFetched = 6,
    MutationPermitUnavailable = 7,
    PostCommitFault = 8,
    VerificationFailed = 9,
}

internal enum PrestigeNativeStage
{
    None = 0,
    NativeTransaction = 1,
    Verification = 2,
}

internal readonly struct PrestigeAdmissionState
{
    internal PrestigeAdmissionState(long lifecycleEpoch,
        bool worldCycleComplete, bool challengesFetched)
    {
        LifecycleEpoch = lifecycleEpoch;
        WorldCycleComplete = worldCycleComplete;
        ChallengesFetched = challengesFetched;
    }

    internal long LifecycleEpoch { get; }
    internal bool WorldCycleComplete { get; }
    internal bool ChallengesFetched { get; }
}

internal readonly struct PrestigeSubmission
{
    internal PrestigeSubmission(PrestigePreflight preflight, PrestigeNativeStage stage,
        NativeMutationOutcome outcome, NativeMutationCallOutcome callOutcome,
        string reason)
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
    }

    internal PrestigePreflight Preflight { get; }
    internal PrestigeNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }
    internal bool Verified => Preflight == PrestigePreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static PrestigeSubmission Reject(PrestigePreflight preflight, string reason) =>
        new(preflight, PrestigeNativeStage.None, NativeMutationOutcome.BeforeCaptureFailed,
            default, reason);
}
