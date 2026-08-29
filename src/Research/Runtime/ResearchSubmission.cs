using OrbModding.Common;

namespace OrbAutomata;

internal enum ResearchPreflight
{
    Proceeded = 0, WrongThread = 1, LifecycleReplaced = 2, ContractUnavailable = 3,
    IdentityUnavailable = 5, DevelopUnavailable = 6,
    MultiBuyUnavailable = 7, InvalidMode = 8, InvalidState = 9,
    BonusUnavailable = 10, MutationPermitUnavailable = 11, PostCommitFault = 12,
    VerificationFailed = 13, AmountUnavailable = 14,
    AlreadyMaxed = 15, Unaffordable = 16, RequirementsUnmet = 17, LeewayExhausted = 18,
    AlreadyDeveloping = 19,
}

internal enum ResearchNativeStage { None = 0, NativeCallback = 1, Verification = 2 }

/// <summary>
/// The live capture a develop is admitted against. <c>Complete</c> through
/// <c>BelowMaxInvestmentLevel</c> are the gates <c>ResearchSO.IsWithinDevelopRange</c> itself
/// consults, captured so a refusal can name which one closed instead of reporting only that the
/// aggregate said no.
/// </summary>
internal readonly record struct ResearchAdmissionState(
    bool QueueMode, int MultiBuy, int Level, int QueuedLevels, int SelfBonusLevels,
    bool IsActive, bool IsDeveloping, bool CanDevelop,
    bool CanApplyBonusLevel, int FreeBonusLevels, bool CostAffordable, int MaxLevel,
    int LevelsAvailable, bool Complete, bool MeetsLevelRequirements, bool StillHasLeeway,
    bool BelowArtificialMaxLevel, bool BelowMaxInvestmentLevel);

internal readonly struct ResearchSubmission
{
    internal ResearchSubmission(ResearchPreflight preflight, ResearchNativeStage stage,
        NativeMutationOutcome outcome, NativeMutationCallOutcome callOutcome,
        string reason, int maximumAmount = -1)
    {
        Preflight = preflight; Stage = stage; Outcome = outcome; CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
        MaximumAmount = maximumAmount;
    }

    internal ResearchPreflight Preflight { get; }
    internal ResearchNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }

    /// <summary>
    /// The ceiling the refusal prose names, or <c>-1</c> when the prose names none. It is read from
    /// the same admission capture the prose is written from, so the sentence and the machine field
    /// can never disagree.
    /// </summary>
    internal int MaximumAmount { get; }

    internal bool Verified => Preflight == ResearchPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static ResearchSubmission Reject(
        ResearchPreflight preflight, string reason, int maximumAmount = -1) =>
        new(preflight, ResearchNativeStage.None, NativeMutationOutcome.BeforeCaptureFailed,
            default, reason, maximumAmount);
}
