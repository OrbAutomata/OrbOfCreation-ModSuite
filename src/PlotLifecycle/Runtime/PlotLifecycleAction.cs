using System;

namespace OrbAutomata;

internal enum PlotLifecycleActionKind
{
    Add = 1,
    Remove = 2,
}

internal readonly struct PlotLifecycleAction
{
    internal PlotLifecycleAction(
        PlotLifecycleActionKind kind,
        Guid plotId,
        Guid actionId,
        int amount,
        long lifecycleEpoch)
    {
        if (plotId == Guid.Empty)
            throw new ArgumentException("A plot identity is required.", nameof(plotId));
        if (actionId == Guid.Empty)
            throw new ArgumentException("A plot action identity is required.", nameof(actionId));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        Kind = kind;
        PlotId = plotId;
        ActionId = actionId;
        Amount = amount;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal PlotLifecycleActionKind Kind { get; }
    internal Guid PlotId { get; }
    internal Guid ActionId { get; }
    internal int Amount { get; }
    internal long LifecycleEpoch { get; }
}
