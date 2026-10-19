using System;

namespace OrbAutomata;

/// <summary>How a past action of this service failed, in the terms the health report uses.</summary>
internal enum AutoHarvestFaultKind
{
    None = 0,

    /// <summary>The game proved the build's authored content does not match its contract.</summary>
    ContractUnavailable = 1,
}

/// <summary>
/// The contract mismatches this service's own actions proved, and how far each one reaches.
/// </summary>
/// <remarks>
/// It lives in the cycle state because it is the worker's memory rather than a reading of anything,
/// so deciding never depends on state only the action boundary can write. See W45. Any other fault
/// is not remembered here: it backs its pair off on the service's retry gate and is tried again.
/// The state is created per lifecycle, so the memory clears when the game reloads, and the next
/// lifecycle probes the contract again.
/// </remarks>
internal readonly struct AutoHarvestFaultMemory
{
    internal AutoHarvestFaultMemory(
        AutoHarvestFaultKind feature,
        AutoHarvestFaultKind fruit,
        AutoHarvestFaultKind treasure)
    {
        Feature = Checked(feature, nameof(feature));
        Fruit = Checked(fruit, nameof(fruit));
        Treasure = Checked(treasure, nameof(treasure));
    }

    /// <summary>A failure that reaches every pair, whichever one provoked it.</summary>
    internal AutoHarvestFaultKind Feature { get; }

    internal AutoHarvestFaultKind Fruit { get; }
    internal AutoHarvestFaultKind Treasure { get; }

    internal bool HasFeatureFault => Feature != AutoHarvestFaultKind.None;

    /// <summary>
    /// What stands in the way of one pair. A feature-wide failure answers for both pairs, because a
    /// pair with no failure of its own is still not one this service can act on.
    /// </summary>
    internal AutoHarvestFaultKind For(AutoHarvestPair pair) =>
        HasFeatureFault
            ? Feature
            : pair switch
            {
                AutoHarvestPair.FruitTree => Fruit,
                AutoHarvestPair.TreasureTree => Treasure,
                _ => throw new ArgumentOutOfRangeException(nameof(pair)),
            };

    internal AutoHarvestFaultMemory With(AutoHarvestPair pair, AutoHarvestFaultKind kind) => pair switch
    {
        AutoHarvestPair.FruitTree => new AutoHarvestFaultMemory(Feature, kind, Treasure),
        AutoHarvestPair.TreasureTree => new AutoHarvestFaultMemory(Feature, Fruit, kind),
        _ => throw new ArgumentOutOfRangeException(nameof(pair)),
    };

    internal AutoHarvestFaultMemory WithFeature(AutoHarvestFaultKind kind) =>
        new(kind, Fruit, Treasure);

    private static AutoHarvestFaultKind Checked(AutoHarvestFaultKind kind, string name) =>
        kind is AutoHarvestFaultKind.None or AutoHarvestFaultKind.ContractUnavailable
            ? kind
            : throw new ArgumentOutOfRangeException(name);
}
