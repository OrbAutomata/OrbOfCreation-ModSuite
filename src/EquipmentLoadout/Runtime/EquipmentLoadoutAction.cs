using System;

namespace OrbAutomata;

internal enum EquipmentLoadoutActionKind { Equip = 1, Unequip = 2 }

internal readonly struct EquipmentLoadoutAction
{
    internal EquipmentLoadoutAction(EquipmentLoadoutActionKind kind, Guid targetId, int amount, long lifecycleEpoch)
    {
        if (targetId == Guid.Empty) throw new ArgumentException("An equipment identity is required.", nameof(targetId));
        Kind = kind;
        TargetId = targetId;
        Amount = amount > 0 ? amount : throw new ArgumentOutOfRangeException(nameof(amount));
        LifecycleEpoch = lifecycleEpoch;
    }

    internal EquipmentLoadoutActionKind Kind { get; }
    internal Guid TargetId { get; }
    internal int Amount { get; }
    internal long LifecycleEpoch { get; }
}
