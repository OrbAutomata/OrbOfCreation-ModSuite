using System;

namespace OrbAutomata;

internal enum HarvestLifecycleActionKind
{
    AddElement = 1,
    RemoveElement = 2,
    AddAction = 3,
    RemoveAction = 4,
}

internal readonly struct HarvestLifecycleAction
{
    internal HarvestLifecycleAction(
        HarvestLifecycleActionKind kind,
        Guid elementId,
        Guid actionId,
        int amount,
        long lifecycleEpoch)
    {
        if (elementId == Guid.Empty)
            throw new ArgumentException("A harvest element identity is required.", nameof(elementId));
        if ((kind is HarvestLifecycleActionKind.AddAction or HarvestLifecycleActionKind.RemoveAction) &&
            actionId == Guid.Empty)
            throw new ArgumentException("A harvest action identity is required.", nameof(actionId));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        Kind = kind;
        ElementId = elementId;
        ActionId = actionId;
        Amount = amount;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal HarvestLifecycleActionKind Kind { get; }
    internal Guid ElementId { get; }
    internal Guid ActionId { get; }
    internal int Amount { get; }
    internal long LifecycleEpoch { get; }
}
