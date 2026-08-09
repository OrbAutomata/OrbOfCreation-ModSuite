using System;

namespace OrbAutomata;

internal enum ChallengeActionKind
{
    /// <summary>Presses a challenge row's preferred toggle, which selects or unselects it.</summary>
    Select = 1,

    /// <summary>Presses a challenge row's queue toggle, which moves it between idle and queued.</summary>
    Queue = 2,

    Abandon = 3,

    /// <summary>
    /// Presses the offer screen's new-challenges button, free once per world cycle and one reroll
    /// every time after.
    /// </summary>
    Reroll = 4,
}

internal readonly struct ChallengeAction
{
    internal ChallengeAction(ChallengeActionKind kind, Guid targetId, long lifecycleEpoch)
        : this(kind, targetId, Guid.Empty, lifecycleEpoch)
    {
    }

    internal ChallengeAction(
        ChallengeActionKind kind,
        Guid targetId,
        Guid replacedId,
        long lifecycleEpoch)
    {
        Kind = kind;
        TargetId = targetId;
        ReplacedId = replacedId;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal ChallengeActionKind Kind { get; }
    internal Guid TargetId { get; }

    /// <summary>
    /// The selection this one takes the place of, when the preferred list is full and the caller
    /// asked for a challenge that is not in it. The game's own screen needs two presses for that —
    /// clear a row, then pick the new one — and a caller that only knew the first refusal had no way
    /// to learn the first press existed.
    /// </summary>
    internal Guid ReplacedId { get; }

    internal long LifecycleEpoch { get; }
    internal bool HasTarget => Kind is ChallengeActionKind.Select or ChallengeActionKind.Queue or ChallengeActionKind.Abandon;
}
