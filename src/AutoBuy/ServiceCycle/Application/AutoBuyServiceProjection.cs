using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// Projects bounded per-cycle worker decision cardinality onto the neutral journal surface. The
/// semantic trace owns worker duration and emitted action count; these values add the input,
/// eligibility, requested-level, and exclusion dimensions under the same cycle identity.
/// </summary>
/// <remarks>
/// The ten exclusion counters answer the question a bare "0 eligible of 180" cannot: which term
/// refused them. Captured minus eligible equals their sum on every cycle, so an operator reading the
/// journal can attribute every candidate that did not reach the plan without attaching a debugger.
/// The grouping histogram remains in the worker decision state; the bounded neutral projection is
/// reserved for the complete candidate-exclusion chain so every withheld proposal has a named row.
/// The unaffordable bucket's binding resource does not ride here — the projection surface carries
/// only booleans, integers, and doubles, and a resource identity is a GUID — so that detail stays in
/// the purchase narration, which already prints the binding cost and holdings.
/// </remarks>
internal static class AutoBuyServiceProjection
{
    internal const int CapturedCandidatesKey = 10;
    // Stable decoder keys retained even though the bounded projection now spends its rows on the
    // complete candidate-exclusion chain. Old traces and the trace tool still name these codes.
    internal const int CapturedStructuresKey = 11;
    internal const int CapturedUpgradesKey = 12;
    internal const int EligibleCandidatesKey = 13;
    internal const int PlannedActionsKey = 14;
    internal const int RequestedLevelsKey = 15;
    internal const int ExcludedKindNotSelectedKey = 16;
    internal const int ExcludedUnavailableKey = 19;
    internal const int ExcludedRequirementsUnmetKey = 20;
    internal const int ExcludedTerminalKey = 21;
    internal const int ExcludedUnaffordableKey = 22;
    internal const int ExcludedUnpriceableKey = 23;
    internal const int FullGroupsKey = 24;
    internal const int ReducedGroupsKey = 25;
    internal const int ReducedGroupLevelsKey = 26;
    internal const int LedgerStarvedKey = 27;
    internal const int ExcludedOwningViewUnavailableKey = 28;
    internal const int ExcludedOwningViewRelationMissingKey = 29;
    internal const int ExcludedOwningViewRelationUnreadableKey = 30;
    // Key 31 is retired. It represented the invalid sole-owner assumption and must never be reused,
    // so old decision journals remain unambiguous when decoded beside new ones.
    internal const int RetiredOwningViewRelationAmbiguousKey = 31;
    internal const int ExcludedOwningViewRelationContradictoryKey = 32;
    internal const int ExcludedDestinationFullKey = 33;

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

    }

    private static ServiceProjectionKey Key(int value) => new(value);
    private static ServiceProjectionValue Integer(long value) =>
        ServiceProjectionValue.FromInteger(value);
}
