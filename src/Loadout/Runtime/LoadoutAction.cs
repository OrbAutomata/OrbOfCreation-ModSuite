using System;

namespace OrbAutomata;

internal enum LoadoutActionKind
{
    Select = 1,
    SetEquipmentSection = 2,
    SetAlchemySection = 3,
    Rename = 4,
    NextIcon = 5,
    NextColor = 6,
    SnapshotSave = 7,
    SnapshotLoad = 8,
    SnapshotClear = 9,
}

internal readonly struct LoadoutAction
{
    internal LoadoutAction(LoadoutActionKind kind, Guid targetId, int slot,
        bool enabled, string name, long lifecycleEpoch)
    {
        if (targetId == Guid.Empty)
            throw new ArgumentException("A loadout identity is required.", nameof(targetId));
        Kind = kind;
        TargetId = targetId;
        Slot = slot;
        Enabled = enabled;
        Name = name ?? string.Empty;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal LoadoutActionKind Kind { get; }
    internal Guid TargetId { get; }
    internal int Slot { get; }
    internal bool Enabled { get; }
    internal string Name { get; }
    internal long LifecycleEpoch { get; }
}
