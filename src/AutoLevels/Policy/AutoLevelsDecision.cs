using System;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal enum AutoLevelsDecisionKind
{
    Hold = 0,
    Raise = 1,
    Lower = 2,
}

internal enum AutoLevelsDecisionReason
{
    Disabled = 0,
    EvidenceUnavailable = 1,
    InvalidEvidence = 2,
    NoActiveWork = 3,
    AtMaximum = 4,
    AtMinimum = 5,
    ReserveMaximum = 6,
    ImmediateCost = 7,
    NextImmediateCost = 8,
    DominantDrain = 9,
    OtherDrainPressure = 10,
    NextDrain = 11,
    Allocation = 12,
    Sustainable = 13,
    ControlLocked = 14,
}

/// <summary>One publication's one-step policy result, including the resource comparison shown to the player.</summary>
internal readonly struct AutoLevelsDecision
{
    internal AutoLevelsDecision(LevelDial dial, int currentLevel, int targetLevel,
        AutoLevelsDecisionKind kind, AutoLevelsDecisionReason reason, Guid resourceId = default,
        BigDouble required = default, BigDouble budget = default, string detail = "")
    {
        Dial = dial;
        CurrentLevel = currentLevel;
        TargetLevel = targetLevel;
        Kind = kind;
        Reason = reason;
        ResourceId = resourceId;
        Required = required;
        Budget = budget;
        Detail = detail ?? string.Empty;
    }

    internal LevelDial Dial { get; }
    internal int CurrentLevel { get; }
    internal int TargetLevel { get; }
    internal AutoLevelsDecisionKind Kind { get; }
    internal AutoLevelsDecisionReason Reason { get; }
    internal Guid ResourceId { get; }
    internal BigDouble Required { get; }
    internal BigDouble Budget { get; }
    internal string Detail { get; }
}
