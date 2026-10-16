using System;

namespace OrbAutomata;

internal enum RitualLifecycleActionKind
{
    Select = 1,
    Deselect = 2,
    SetLevel = 3,
    Activate = 4,
    CancelDuration = 5,
    EndBattle = 6,
}

internal readonly struct RitualLifecycleAction
{
    internal RitualLifecycleAction(
        RitualLifecycleActionKind kind,
        Guid ritualId,
        int level,
        long lifecycleEpoch)
    {
        if (ritualId == Guid.Empty)
            throw new ArgumentException("A ritual identity is required.", nameof(ritualId));
        Kind = kind;
        RitualId = ritualId;
        Level = level;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal RitualLifecycleActionKind Kind { get; }
    internal Guid RitualId { get; }
    internal int Level { get; }
    internal long LifecycleEpoch { get; }
}
