using System;

namespace OrbAutomata;

internal enum AutoHarvestPairCaptureKind
{
    NotSelected = 1,
    Captured = 2,
    Unavailable = 3,
}

/// <summary>
/// Why a selected pair carries no facts. One value, because there is one such reason left: the game's
/// registries have not been collected yet, which is a fact about the world rather than about a pair
/// and therefore reaches both. Contract mismatches belong to the worker's own memory; see W45.
/// </summary>
internal enum AutoHarvestCaptureUnavailableReason
{
    None = 0,
    RegistryNotReady = 1,
}

internal readonly struct AutoHarvestPairCapture
{
    private AutoHarvestPairCapture(
        AutoHarvestPair pair,
        AutoHarvestPairCaptureKind kind,
        AutoHarvestPairFacts facts,
        AutoHarvestActionSafetyState safety,
        AutoHarvestCaptureUnavailableReason unavailableReason)
    {
        Pair = pair;
        Kind = kind;
        Facts = facts;
        Safety = safety;
        UnavailableReason = unavailableReason;
    }

    public AutoHarvestPair Pair { get; }
    public AutoHarvestPairCaptureKind Kind { get; }
    public AutoHarvestPairFacts Facts { get; }

    /// <summary>
    /// Whether the pair's authored content is what this suite audited, as the snapshot describes it.
    /// </summary>
    /// <remarks>
    /// Kept beside the facts rather than folded into them: the facts decide whether the pair is worth
    /// acting on now, and this decides whether acting on it is safe at all. Only the boundary asks it,
    /// which is why a pair can be eligible on the facts and still be refused there.
    /// </remarks>
    public AutoHarvestActionSafetyState Safety { get; }

    public AutoHarvestCaptureUnavailableReason UnavailableReason { get; }
    public bool IsValid => Kind switch
    {
        AutoHarvestPairCaptureKind.NotSelected =>
            UnavailableReason == AutoHarvestCaptureUnavailableReason.None,
        AutoHarvestPairCaptureKind.Captured => UnavailableReason == AutoHarvestCaptureUnavailableReason.None,
        AutoHarvestPairCaptureKind.Unavailable => UnavailableReason != AutoHarvestCaptureUnavailableReason.None,
        _ => false,
    };

    public static AutoHarvestPairCapture NotSelected(AutoHarvestPair pair) =>
        new(pair, AutoHarvestPairCaptureKind.NotSelected, default, default,
            AutoHarvestCaptureUnavailableReason.None);

    public static AutoHarvestPairCapture Captured(
        AutoHarvestPair pair,
        AutoHarvestPairFacts facts,
        AutoHarvestActionSafetyState safety) =>
        new(pair, AutoHarvestPairCaptureKind.Captured, facts, safety,
            AutoHarvestCaptureUnavailableReason.None);

    public static AutoHarvestPairCapture Unavailable(AutoHarvestPair pair) =>
        new(pair, AutoHarvestPairCaptureKind.Unavailable, default, default,
            AutoHarvestCaptureUnavailableReason.RegistryNotReady);
}

/// <summary>
/// One cycle's view of the two harvest pairs.
/// </summary>
/// <remarks>
/// Whether this instance owns the harvest action family used to be here too. It is a lease another
/// plugin can take mid-cycle, so the action boundary re-reads it before mutating and a copy carried
/// through the decision was a pre-filter over an answer taken again. See W46.
/// </remarks>
internal readonly struct AutoHarvestCycleFrame
{
    public AutoHarvestCycleFrame(
        AutoHarvestPairCapture fruit,
        AutoHarvestPairCapture treasure)
    {
        if (!fruit.IsValid || fruit.Pair != AutoHarvestPair.FruitTree)
            throw new ArgumentException("A valid fruit capture is required.", nameof(fruit));
        if (!treasure.IsValid || treasure.Pair != AutoHarvestPair.TreasureTree)
            throw new ArgumentException("A valid treasure capture is required.", nameof(treasure));
        Fruit = fruit;
        Treasure = treasure;
    }

    public AutoHarvestPairCapture Fruit { get; }
    public AutoHarvestPairCapture Treasure { get; }
}
