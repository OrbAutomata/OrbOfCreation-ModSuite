using System;

namespace OrbAutomata;

internal enum StructureLifecycleActionKind
{
    Enable = 1,
    Disable = 2,
}

internal readonly struct StructureLifecycleAction
{
    internal StructureLifecycleAction(
        StructureLifecycleActionKind kind,
        Guid structureId,
        long lifecycleEpoch)
    {
        if (structureId == Guid.Empty)
            throw new ArgumentException("A structure identity is required.", nameof(structureId));
        Kind = kind;
        StructureId = structureId;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal StructureLifecycleActionKind Kind { get; }
    internal Guid StructureId { get; }
    internal long LifecycleEpoch { get; }
}
