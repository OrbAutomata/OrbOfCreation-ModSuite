using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// Projects bounded per-cycle worker decision cardinality onto the neutral journal surface. The
/// semantic trace owns worker duration and emitted action count; these values add the input,
/// eligibility, requested-level, and exclusion dimensions under the same cycle identity.
/// </summary>
/// <remarks>
/// The exclusion counters answer the question a bare "0 eligible of 180" cannot: which term
/// refused them. Captured minus eligible equals their sum on every cycle, so an operator reading the
/// journal can attribute every candidate that did not reach the plan without attaching a debugger.
/// The unaffordable bucket's binding resource does not ride here — the projection surface carries
/// only booleans, integers, and doubles, and a resource identity is a GUID — so that detail stays in
/// the purchase narration, which already prints the binding cost and holdings.
/// </remarks>
internal static class AutoBuyServiceProjection
{
    internal const int CapturedCandidatesKey = 10;
    internal const int EligibleCandidatesKey = 13;
    internal const int PlannedActionsKey = 14;
    internal const int RequestedLevelsKey = 15;
    internal const int ExcludedKindNotSelectedKey = 16;
    internal const int ExcludedUnavailableKey = 19;
    internal const int ExcludedRequirementsUnmetKey = 20;
    internal const int ExcludedTerminalKey = 21;
    internal const int ExcludedUnaffordableKey = 22;
    internal const int ExcludedUnpriceableKey = 23;
    internal const int ExcludedOwningViewUnavailableKey = 28;
    internal const int ExcludedOwningViewRelationMissingKey = 29;
    internal const int ExcludedOwningViewRelationUnreadableKey = 30;
    // Keys 11, 12, 24-27 and 31 are retired; never reuse them.
    internal const int ExcludedOwningViewRelationContradictoryKey = 32;
    internal const int ExcludedDestinationFullKey = 33;
    internal const int ExcludedBackingOffKey = 34;

    public static void Write(
        in AutoBuyCycleState state,
        ServiceStateProjectionBuilder output)
    {
        var decision = state.Decision;
        output.Add(Key(CapturedCandidatesKey), Integer(decision.CapturedCandidates));
        output.Add(Key(EligibleCandidatesKey), Integer(decision.EligibleCandidates));
        output.Add(Key(PlannedActionsKey), Integer(decision.PlannedActions));
        output.Add(Key(RequestedLevelsKey), Integer(decision.RequestedLevels));

        var exclusions = decision.Exclusions;
        output.Add(Key(ExcludedKindNotSelectedKey), Integer(exclusions.KindNotSelected));
        output.Add(Key(ExcludedUnavailableKey), Integer(exclusions.Unavailable));
        output.Add(Key(ExcludedRequirementsUnmetKey), Integer(exclusions.RequirementsUnmet));
        output.Add(Key(ExcludedTerminalKey), Integer(exclusions.Terminal));
        output.Add(Key(ExcludedUnaffordableKey), Integer(exclusions.Unaffordable));
        output.Add(Key(ExcludedUnpriceableKey), Integer(exclusions.Unpriceable));
        output.Add(Key(ExcludedOwningViewUnavailableKey), Integer(exclusions.OwningViewUnavailable));
        output.Add(Key(ExcludedOwningViewRelationMissingKey), Integer(exclusions.OwningViewRelationMissing));
        output.Add(Key(ExcludedOwningViewRelationUnreadableKey), Integer(exclusions.OwningViewRelationUnreadable));
        output.Add(Key(ExcludedOwningViewRelationContradictoryKey), Integer(exclusions.OwningViewRelationContradictory));
        output.Add(Key(ExcludedDestinationFullKey), Integer(exclusions.DestinationFull));
        output.Add(Key(ExcludedBackingOffKey), Integer(exclusions.BackingOff));
    }

    private static ServiceProjectionKey Key(int value) => new(value);
    private static ServiceProjectionValue Integer(long value) =>
        ServiceProjectionValue.FromInteger(value);
}
