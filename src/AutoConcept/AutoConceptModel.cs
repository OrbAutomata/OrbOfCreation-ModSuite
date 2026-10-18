using System;
using System.Collections.Generic;

namespace OrbAutomata;

internal enum AutoConceptOperationMode
{
    Disabled,
    Active,
}

internal enum AutoConceptSlotManagementMode
{
    RotateAll,
    PreserveManual,
    TimedCycle,
}

internal readonly struct ConceptProgress
{
    public ConceptProgress(string uuid, int masteryLevel, double masteryProgress, bool eligible)
    {
        Uuid = uuid;
        MasteryLevel = masteryLevel;
        MasteryProgress = double.IsFinite(masteryProgress)
            ? Math.Clamp(masteryProgress, 0.0, 1.0)
            : 1.0;
        Eligible = eligible;
    }

    public string Uuid { get; }
    public int MasteryLevel { get; }
    public double MasteryProgress { get; }
    public bool Eligible { get; }
}

internal static class AutoConceptBalancer
{
    public static bool UsesFullRotation(AutoConceptSlotManagementMode mode) =>
        mode is AutoConceptSlotManagementMode.RotateAll or AutoConceptSlotManagementMode.TimedCycle;

    public static bool RequiresLowerMastery(AutoConceptSlotManagementMode mode) =>
        mode != AutoConceptSlotManagementMode.TimedCycle;

    public static int CompareTimedCycleOrder(
        long? leftLastAssignment,
        string leftUuid,
        long? rightLastAssignment,
        string rightUuid)
    {
        if (leftLastAssignment.HasValue != rightLastAssignment.HasValue)
            return leftLastAssignment.HasValue ? 1 : -1;
        if (leftLastAssignment.HasValue)
        {
            var sequence = leftLastAssignment.Value.CompareTo(rightLastAssignment!.Value);
            if (sequence != 0) return sequence;
        }
        return StringComparer.Ordinal.Compare(leftUuid, rightUuid);
    }

    public static bool ResourceSafeTimedCandidatePrecedes(
        bool resourceSafe,
        long? candidateLastAssignment,
        string candidateUuid,
        long? currentLastAssignment,
        string currentUuid) =>
        resourceSafe && CompareTimedCycleOrder(
            candidateLastAssignment,
            candidateUuid,
            currentLastAssignment,
            currentUuid) < 0;

    public static IReadOnlyList<ConceptProgress> Rank(IReadOnlyList<ConceptProgress> candidates)
    {
        var result = new List<ConceptProgress>(candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (candidate.Eligible && !string.IsNullOrWhiteSpace(candidate.Uuid)) result.Add(candidate);
        }
        result.Sort(Compare);
        return result;
    }

    public static bool HasStrictlyLowerMastery(ConceptProgress candidate, ConceptProgress active)
    {
        var level = candidate.MasteryLevel.CompareTo(active.MasteryLevel);
        return level < 0 || level == 0 && candidate.MasteryProgress < active.MasteryProgress;
    }

    public static bool HasReached(ConceptProgress current, ConceptProgress target) =>
        !HasStrictlyLowerMastery(current, target);

    public static bool HasTrainingPeriodElapsed(
        double startedAtSeconds,
        double currentSeconds,
        int configuredPeriodSeconds)
    {
        var period = Math.Clamp(configuredPeriodSeconds, 10, 3600);
        return Math.Max(0.0, currentSeconds - startedAtSeconds) >= period;
    }

    public static bool HasTrainingSessionCompleted(
        AutoConceptSlotManagementMode mode,
        ConceptProgress current,
        ConceptProgress target,
        double startedAtSeconds,
        double currentSeconds,
        int configuredPeriodSeconds) =>
        HasTrainingPeriodElapsed(startedAtSeconds, currentSeconds, configuredPeriodSeconds) ||
        mode != AutoConceptSlotManagementMode.TimedCycle && HasReached(current, target);

    private static int Compare(ConceptProgress left, ConceptProgress right)
    {
        var level = left.MasteryLevel.CompareTo(right.MasteryLevel);
        if (level != 0) return level;
        var progress = left.MasteryProgress.CompareTo(right.MasteryProgress);
        return progress != 0 ? progress : StringComparer.Ordinal.Compare(left.Uuid, right.Uuid);
    }
}

internal static class AutoConceptResourcePolicy
{
    /// <summary>
    /// Whether <paramref name="trueIncrement"/> more drain would leave the resource's true rate below
    /// the reserve, a share of its gross positive rate. The planner and the boundary keep one rule.
    /// </summary>
    public static bool BreaksRateReserve(
        BigDouble trueRate,
        BigDouble trueDrain,
        BigDouble trueIncrement,
        float reservePercent)
    {
        var grossRate = trueRate + trueDrain;
        var reserve = grossRate.CompareTo(default) < 0
            ? default
            : grossRate * (Math.Clamp(reservePercent, 0f, 100f) / 100d);
        return (trueRate - trueIncrement).CompareTo(reserve) < 0;
    }

    public static bool TryAcceptPositiveDrain(object? nativeIsAtZero, out string reason)
    {
        if (nativeIsAtZero is not bool atZero)
        {
            reason = "zero-quantity state is unavailable";
            return false;
        }
        if (atZero)
        {
            reason = "is at zero";
            return false;
        }
        reason = string.Empty;
        return true;
    }
}

internal readonly struct ConceptOwnership
{
    public ConceptOwnership(int manualBaseline, int automatedDelta)
    {
        ManualBaseline = Math.Max(0, manualBaseline);
        AutomatedDelta = Math.Max(0, automatedDelta);
    }

    public int ManualBaseline { get; }
    public int AutomatedDelta { get; }
    public int ExpectedQuantity => checked(ManualBaseline + AutomatedDelta);
}
