using System;

namespace OrbAutomata;

internal enum ResearchActionKind { Develop = 1, Pause = 2, Resume = 3, Cancel = 4, Bonus = 5 }

internal readonly struct ResearchAction
{
    internal ResearchAction(ResearchActionKind kind, Guid targetId, int amount, long lifecycleEpoch)
    {
        if (targetId == Guid.Empty) throw new ArgumentException("A research identity is required.", nameof(targetId));
        Kind = kind;
        TargetId = targetId;
        Amount = amount > 0 ? amount : throw new ArgumentOutOfRangeException(nameof(amount));
        LifecycleEpoch = lifecycleEpoch;
    }

    internal ResearchActionKind Kind { get; }
    internal Guid TargetId { get; }
    internal int Amount { get; }
    internal long LifecycleEpoch { get; }
}
