using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// Projects the worker's per-cycle decision cardinality onto the neutral journal surface.
/// </summary>
/// <remarks>
/// Spell Leveling's ordinary output is no action at all, so a bare "0 planned" says nothing. The
/// exclusion counters plus the ready count attribute every discovered spell to a term, and the
/// captured count minus their sum is always the ready count — a term that starts silently swallowing
/// spells cannot hide in a total.
/// </remarks>
internal static class SpellLevelServiceProjection
{
    internal const int CapturedSpellsKey = 10;
    internal const int ReadySpellsKey = 11;
    internal const int PlannedActionsKey = 12;
    internal const int CapabilityKey = 13;
    internal const int ExcludedUndiscoveredKey = 14;
    internal const int ExcludedNotReadyKey = 15;
    internal const int ExcludedOutrankedKey = 16;
    internal const int ExcludedUnaffordableKey = 17;
    internal const int ExcludedBackingOffKey = 18;

    public static void Write(
        in SpellLevelCycleState state,
        ServiceStateProjectionBuilder output)
    {
        var decision = state.Decision;
        output.Add(Key(CapturedSpellsKey), Integer(decision.CapturedSpells));
        output.Add(Key(ReadySpellsKey), Integer(decision.ReadySpells));
        output.Add(Key(PlannedActionsKey), Integer(decision.PlannedActions));
        output.Add(Key(CapabilityKey), Integer((int)decision.Capability));

        var exclusions = decision.Exclusions;
        output.Add(Key(ExcludedUndiscoveredKey), Integer(exclusions.Undiscovered));
        output.Add(Key(ExcludedNotReadyKey), Integer(exclusions.NotReady));
        output.Add(Key(ExcludedUnaffordableKey), Integer(exclusions.Unaffordable));
        output.Add(Key(ExcludedOutrankedKey), Integer(exclusions.Outranked));
        output.Add(Key(ExcludedBackingOffKey), Integer(exclusions.BackingOff));
    }

    private static ServiceProjectionKey Key(int value) => new(value);
    private static ServiceProjectionValue Integer(long value) =>
        ServiceProjectionValue.FromInteger(value);
}
