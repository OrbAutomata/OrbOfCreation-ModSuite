using System;

namespace OrbAutomata;

internal enum CraftingStationActionKind
{
    SetIngredient = 1,
    SetOutput = 2,
    SetLevel = 3,
    Start = 4,
    Stop = 5,
}

internal readonly struct CraftingStationAction
{
    internal CraftingStationAction(
        CraftingStationActionKind kind,
        Guid stationId,
        Guid selectionId,
        int value,
        long lifecycleEpoch)
    {
        if (stationId == Guid.Empty)
            throw new ArgumentException("A Brewing Station identity is required.", nameof(stationId));
        Kind = kind;
        StationId = stationId;
        SelectionId = selectionId;
        Value = value;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal CraftingStationActionKind Kind { get; }
    internal Guid StationId { get; }
    internal Guid SelectionId { get; }
    internal int Value { get; }
    internal long LifecycleEpoch { get; }
}
