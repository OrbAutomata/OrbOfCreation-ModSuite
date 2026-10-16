using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// Projects the worker's per-cycle decision cardinality onto the neutral journal surface.
/// </summary>
/// <remarks>
/// Auto Cast's ordinary output is one cast or none, so a bare "0 planned" says nothing. The six
/// exclusion counters plus the eligible count attribute every equipped slot to a term, and the
/// captured count minus their sum is always the eligible count — a term that starts silently
/// swallowing slots cannot hide in a total.
/// </remarks>
internal static class AutoCastServiceProjection
{
    internal const int CapturedSlotsKey = 10;
    internal const int EligibleSlotsKey = 11;
    internal const int PlannedActionsKey = 12;
    internal const int HoldingChargeKey = 13;
    internal const int ChannelBlockedKey = 14;
    internal const int ExcludedEmptyKey = 15;
    internal const int ExcludedBusyKey = 16;
    internal const int ExcludedNotReadyKey = 17;
    internal const int ExcludedReserveFloorKey = 18;
    internal const int ExcludedBelowStartThresholdKey = 19;
    internal const int ExcludedOutrankedKey = 20;
    internal const int LoadoutOpenKey = 21;

    public static void Write(
        in AutoCastCycleState state,
        ServiceStateProjectionBuilder output)
    {
        var decision = state.Decision;
        output.Add(Key(CapturedSlotsKey), Integer(decision.CapturedSlots));
        output.Add(Key(EligibleSlotsKey), Integer(decision.EligibleSlots));
        output.Add(Key(PlannedActionsKey), Integer(decision.PlannedActions));
        output.Add(Key(HoldingChargeKey), Integer(decision.HoldingCharge ? 1 : 0));
        output.Add(Key(ChannelBlockedKey), Integer(decision.ChannelBlocked ? 1 : 0));

        var exclusions = decision.Exclusions;
        output.Add(Key(ExcludedEmptyKey), Integer(exclusions.Empty));
        output.Add(Key(ExcludedBusyKey), Integer(exclusions.Busy));
        output.Add(Key(ExcludedNotReadyKey), Integer(exclusions.NotReady));
        output.Add(Key(ExcludedReserveFloorKey), Integer(exclusions.ReserveFloor));
        output.Add(Key(ExcludedBelowStartThresholdKey), Integer(exclusions.BelowStartThreshold));
        output.Add(Key(ExcludedOutrankedKey), Integer(exclusions.Outranked));
        output.Add(Key(LoadoutOpenKey), Integer(decision.LoadoutOpen ? 1 : 0));
    }

    private static ServiceProjectionKey Key(int value) => new(value);
    private static ServiceProjectionValue Integer(long value) =>
        ServiceProjectionValue.FromInteger(value);
}
