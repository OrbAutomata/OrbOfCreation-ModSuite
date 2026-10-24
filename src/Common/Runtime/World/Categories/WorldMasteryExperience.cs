using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>The native mastery track that produced one observed experience gain.</summary>
internal enum MasteryExperienceDomain
{
    Spell = 0,
    Artifact = 1,
    Alchemy = 2,
}

/// <summary>
/// One exact native mastery-XP input observed by an audited patch and published with the world.
/// </summary>
/// <remarks>
/// Saved XP cannot be differenced safely: a mastery rollover may consume it and an automation
/// action may add to it. The patch therefore records the native method argument, while the world
/// supplies every relationship fact used to decide what that input means.
/// </remarks>
internal readonly struct WorldMasteryExperience
{
    internal WorldMasteryExperience(
        long sequence,
        MasteryExperienceDomain domain,
        Guid sourceId,
        int sourceMastery,
        bool sourceEligible,
        BigDouble amount)
    {
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        Sequence = sequence;
        Domain = domain;
        SourceId = sourceId;
        SourceMastery = sourceMastery;
        SourceEligible = sourceEligible;
        Amount = amount;
    }

    internal long Sequence { get; }
    internal MasteryExperienceDomain Domain { get; }
    internal Guid SourceId { get; }
    internal int SourceMastery { get; }
    internal bool SourceEligible { get; }
    internal BigDouble Amount { get; }
}

/// <summary>
/// Main-thread input to world collection. Implementations retain a bounded observation history;
/// collection only copies values belonging to the epoch it is currently reading.
/// </summary>
internal interface IWorldMasteryExperienceSource
{
    void CopyTo(long lifecycleEpoch, WorldRelationBuffer<WorldMasteryExperience> destination);
}

internal sealed class EmptyWorldMasteryExperienceSource : IWorldMasteryExperienceSource
{
    internal static readonly IWorldMasteryExperienceSource Instance =
        new EmptyWorldMasteryExperienceSource();

    private EmptyWorldMasteryExperienceSource()
    {
    }

    public void CopyTo(long lifecycleEpoch, WorldRelationBuffer<WorldMasteryExperience> destination)
    {
    }
}
