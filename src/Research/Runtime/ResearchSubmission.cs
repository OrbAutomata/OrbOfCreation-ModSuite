using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// The live capture a develop is admitted against. <c>Complete</c> through
/// <c>BelowMaxInvestmentLevel</c> are the gates <c>ResearchSO.IsWithinDevelopRange</c> itself
/// consults, captured so a refusal can name which one closed instead of reporting only that the
/// aggregate said no. <c>Visible</c> is the gate in front of all of them: the Research grid draws
/// no tile for a research the game does not show.
/// </summary>
internal readonly record struct ResearchAdmissionState(
    bool QueueMode, int MultiBuy, int Level, int QueuedLevels, int SelfBonusLevels,
    bool IsActive, bool IsDeveloping, bool CanDevelop,
    bool CanApplyBonusLevel, int FreeBonusLevels, bool CostAffordable, int MaxLevel,
    int LevelsAvailable, bool Complete, bool MeetsLevelRequirements, bool StillHasLeeway,
    bool BelowArtificialMaxLevel, bool BelowMaxInvestmentLevel, bool Visible);

internal readonly struct ResearchSubmission
{
    internal ResearchSubmission(NativeActionOutcome outcome, int maximumAmount = -1)
    {
        Outcome = outcome;
        MaximumAmount = maximumAmount;
    }

    internal NativeActionOutcome Outcome { get; }

    /// <summary>
    /// The ceiling the refusal prose names, or <c>-1</c> when the prose names none. It is read from
    /// the same admission capture the prose is written from, so the sentence and the machine field
    /// can never disagree.
    /// </summary>
    internal int MaximumAmount { get; }

    internal static ResearchSubmission Reject(
        ServiceActionResultCode code, string reason, int maximumAmount = -1) =>
        new(NativeActionOutcome.Reject(code, reason), maximumAmount);
}
