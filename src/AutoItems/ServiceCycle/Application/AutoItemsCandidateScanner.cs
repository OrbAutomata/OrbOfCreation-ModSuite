using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// One deterministic scan over an immutable world publication. World consumables are UUID-sorted,
/// so the first eligible member of each family is stable.
/// </summary>
internal readonly struct AutoItemsCandidateScan
{
    internal AutoItemsCandidateScan(
        int captured,
        int rejectedProfiles,
        int temporaryItems,
        int eligibleTemporaryItems,
        int eligibleRelics,
        int eligibleScrolls,
        bool temporaryUsagePresent,
        bool temporaryUsagePending,
        in AutoItemsCycleAction firstTemporary,
        in AutoItemsCycleAction firstRelic,
        in AutoItemsCycleAction firstScroll,
        in AutoItemsCycleAction firstTargetlessScroll)
    {
        Captured = captured;
        RejectedProfiles = rejectedProfiles;
        TemporaryItems = temporaryItems;
        EligibleTemporaryItems = eligibleTemporaryItems;
        EligibleRelics = eligibleRelics;
        EligibleScrolls = eligibleScrolls;
        TemporaryUsagePresent = temporaryUsagePresent;
        TemporaryUsagePending = temporaryUsagePending;
        FirstTemporary = firstTemporary;
        FirstRelic = firstRelic;
        FirstScroll = firstScroll;
        FirstTargetlessScroll = firstTargetlessScroll;
    }

    internal int Captured { get; }
    internal int RejectedProfiles { get; }
    internal int TemporaryItems { get; }
    internal int EligibleTemporaryItems { get; }
    internal int EligibleRelics { get; }
    internal int EligibleScrolls { get; }
    internal bool TemporaryUsagePresent { get; }
    /// <summary>A temporary use is still preparing, so the game takes no other use yet.</summary>
    internal bool TemporaryUsagePending { get; }
    internal AutoItemsCycleAction FirstTemporary { get; }
    internal AutoItemsCycleAction FirstRelic { get; }
    internal AutoItemsCycleAction FirstScroll { get; }
    /// <summary>The first eligible Scroll whose last attempt found no target.</summary>
    internal AutoItemsCycleAction FirstTargetlessScroll { get; }

    internal AutoItemsDecisionMetrics ToMetrics(
        AutoItemsDecisionKind kind,
        int plannedActions = 0) =>
        new(
            Captured,
            RejectedProfiles,
            TemporaryItems,
            EligibleTemporaryItems,
            EligibleRelics,
            EligibleScrolls,
            plannedActions,
            kind);
}

internal static class AutoItemsCandidateScanner
{
    internal static AutoItemsCandidateScan Scan(
        GameWorldState world,
        in SuiteRuntimeConfiguration configuration,
        PublicationTable<Guid> quarantinedTemporaryItems,
        PublicationTable<Guid>? temporaryAllowlist,
        PublicationTable<Guid> targetlessScrolls)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (quarantinedTemporaryItems is null)
            throw new ArgumentNullException(nameof(quarantinedTemporaryItems));

        var rows = world.Consumables.AsSpan();
        var rejected = 0;
        var temporary = 0;
        var eligibleTemporary = 0;
        var eligibleRelics = 0;
        var eligibleScrolls = 0;
        var temporaryUsagePresent = false;
        var temporaryUsagePending = false;
        AutoItemsCycleAction firstTemporary = default;
        AutoItemsCycleAction firstRelic = default;
        AutoItemsCycleAction firstScroll = default;
        AutoItemsCycleAction firstTargetlessScroll = default;

        for (var index = 0; index < rows.Length; index++)
        {
            var itemId = rows[index].ConsumableId;
            var profile = AutoItemsConsumableProfileBuilder.Build(world, itemId);
            if (AutoItemsConsumableFamilies.IsTemporary(profile.Family))
            {
                temporary++;
                ReadUsage(world, itemId, ref temporaryUsagePresent, ref temporaryUsagePending);
            }

            if (!profile.IsReady)
            {
                rejected++;
                continue;
            }

            if (AutoItemsConsumableFamilies.IsTemporary(profile.Family))
            {
                if (!AutoItemsConfigurationPolicy.Allows(
                        configuration.AutoItems,
                        profile.Family,
                        itemId,
                        temporaryAllowlist) ||
                    AutoItemsTemporaryItemAllowlist.Contains(
                        quarantinedTemporaryItems,
                        itemId) ||
                    !IsBaseEligible(in profile) ||
                    !HasSafeTemporaryShape(in profile) ||
                    !HasCurrentToxicityHeadroom(in profile))
                {
                    continue;
                }

                eligibleTemporary++;
                CaptureFirst(
                    ref firstTemporary,
                    in profile,
                    world.CollectedAtFrame,
                    world.CollectedAtEpoch);
                continue;
            }

            if (!IsBaseEligible(in profile) ||
                !HasCurrentToxicityHeadroom(in profile) ||
                !AutoItemsConfigurationPolicy.Allows(
                    configuration.AutoItems,
                    profile.Family,
                    itemId,
                    temporaryAllowlist))
            {
                continue;
            }

            if (profile.Family == AutoItemsConsumableFamily.Relic)
            {
                eligibleRelics++;
                CaptureFirst(
                    ref firstRelic,
                    in profile,
                    world.CollectedAtFrame,
                    world.CollectedAtEpoch);
                continue;
            }

            if (profile.Family != AutoItemsConsumableFamily.Scroll ||
                !profile.Consumable.CanBeRandomized ||
                !WorldConsumableCountLookup.TryGetStrongestOwnedLevel(
                    world.ConsumableCounts,
                    itemId,
                    out var strongestLevel))
            {
                continue;
            }

            eligibleScrolls++;
            CaptureFirst(
                ref AutoItemsTemporaryItemAllowlist.Contains(targetlessScrolls, itemId)
                    ? ref firstTargetlessScroll
                    : ref firstScroll,
                in profile,
                world.CollectedAtFrame,
                world.CollectedAtEpoch,
                strongestLevel);
        }

        return new AutoItemsCandidateScan(
            rows.Length,
            rejected,
            temporary,
            eligibleTemporary,
            eligibleRelics,
            eligibleScrolls,
            temporaryUsagePresent,
            temporaryUsagePending,
            in firstTemporary,
            in firstRelic,
            in firstScroll,
            in firstTargetlessScroll);
    }

    private static void ReadUsage(
        GameWorldState world,
        Guid itemId,
        ref bool present,
        ref bool pending)
    {
        if (!WorldConsumableUsageLookup.TryFindRange(
                world.ConsumableUsages,
                itemId,
                out var start,
                out var count))
        {
            return;
        }

        for (var index = 0; index < count; index++)
        {
            var usage = world.ConsumableUsages[start + index];
            present |= !usage.Expired;
            pending |= usage.Pending;
        }
    }

    private static bool IsBaseEligible(in AutoItemsConsumableProfile profile) =>
        profile.Consumable.Visible &&
        profile.Consumable.Quantity - profile.Consumable.QueuedQuantity > 0 &&
        profile.Consumable.CurrentPrepTime.CompareTo(BigDouble.Zero) <= 0 &&
        profile.Consumable.CurrentCooldown.CompareTo(BigDouble.Zero) <= 0;

    private static bool HasCurrentToxicityHeadroom(in AutoItemsConsumableProfile profile) =>
        profile.Toxicity.TrueQuantity.CompareTo(profile.ImmediateToxicityCost) >= 0;

    private static bool HasSafeTemporaryShape(in AutoItemsConsumableProfile profile) =>
        profile.Consumable.HasDuration &&
        profile.Consumable.DurationBase > 0d &&
        !double.IsNaN(profile.Consumable.DurationBase) &&
        !double.IsInfinity(profile.Consumable.DurationBase) &&
        !profile.HasAdditionalImmediateCosts &&
        !profile.HasAdditionalHeldCosts;

    private static void CaptureFirst(
        ref AutoItemsCycleAction first,
        in AutoItemsConsumableProfile profile,
        long collectedAtFrame,
        long collectedAtEpoch,
        int plannedLevel = 0)
    {
        if (first.ItemId != Guid.Empty) return;
        first = new AutoItemsCycleAction(
            profile.Consumable.ConsumableId,
            profile.Family,
            collectedAtEpoch,
            plannedLevel,
            collectedAtFrame);
    }
}
