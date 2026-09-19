using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.World;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Projects the pinned world snapshot into an Auto Buy frame, on the worker thread, in one flat pass.
/// No scheduling, dirty-tracking, or reconciliation.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here asks the game anything. The candidates are the snapshot's structures and upgrades,
/// which is the same population the collector read <c>StructureSO.All</c> and <c>UpgradeSO.All</c>
/// for; every fact about one comes from its published row, its published price, or the published
/// effects it authors. The type-specific native <c>CanPurchase()</c> fold and the live action
/// queue's remaining room are taken at the action boundary; see W39.
/// </para>
/// <para>
/// So is the exact-type guard. An entry whose type is not exactly the audited <c>StructureSO</c> or
/// <c>UpgradeSO</c> is refused where the purchase is made, by the same check this used to run first;
/// running it twice made capture a pre-filter over an answer the action boundary takes again.
/// </para>
/// <para>
/// It is a static class holding nothing, and the world reaches it as an argument. Everything it
/// reuses between cycles — the three row arrays — it borrows from the frame the runtime hands it and
/// gives straight back. A worker that owned either would be a worker that could read the world twice
/// in one cycle. See W50.
/// </para>
/// </remarks>
internal static class AutoBuyFrameProjector
{
    private const int InitialCandidateCapacity = 64;
    private const int InitialResourceCapacity = 64;
    private const int InitialCostCapacity = 128;

    /// <summary>
    /// Fills the frame from the world the runtime pinned for this cycle.
    /// </summary>
    /// <remarks>
    /// Bulk development and the action multiplier default to one when their variables are absent from
    /// the snapshot, matching the legacy engine: one level at a time is always safe. A world nothing
    /// has published into yet is empty rather than absent, so it projects to nought candidates and
    /// Auto Buy simply finds nothing to buy.
    /// </remarks>
    internal static void Project(
        ref AutoBuyCycleFrame frame,
        in SuiteRuntimeConfiguration config,
        GameWorldState world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        var candidates = frame.LendCandidates() ?? new AutoBuyCandidateRow[InitialCandidateCapacity];
        var resources = frame.LendResources() ?? new AutoBuyResourceRow[InitialResourceCapacity];
        var costs = frame.LendCosts() ?? new AutoBuyCostRow[InitialCostCapacity];

        var candidateCount = 0;
        var structureCount = 0;
        var resourceCount = 0;
        var costCount = 0;

        if (config.AutoBuy.IncludeStructures)
        {
            var structures = world.Structures.AsSpan();
            for (var index = 0; index < structures.Length; index++)
            {
                AppendCandidate(
                    AutoBuyCandidateKind.Structure,
                    structures[index].EntityId,
                    structures[index].Reading.StructureTypeId,
                    Levels(world, in structures[index]),
                    world,
                    ref candidates,
                    ref resources,
                    ref costs,
                    ref candidateCount,
                    ref resourceCount,
                    ref costCount);
            }

            structureCount = candidateCount;
        }

        if (config.AutoBuy.IncludeUpgrades)
        {
            var upgrades = world.Upgrades.AsSpan();
            for (var index = 0; index < upgrades.Length; index++)
            {
                AppendCandidate(
                    AutoBuyCandidateKind.Upgrade,
                    upgrades[index].EntityId,
                    Guid.Empty,
                    Levels(world, in upgrades[index]),
                    world,
                    ref candidates,
                    ref resources,
                    ref costs,
                    ref candidateCount,
                    ref resourceCount,
                    ref costCount);
            }
        }

        // Taken from the world rather than from the runner: the epoch a plan is judged by is the one
        // the readings it was made from were true for.
        var global = new AutoBuyGlobalRow(
            ReadGlobalCount(world, KnownEntities.BulkDevelopment.Uuid),
            world.CollectedAtEpoch,
            world.CollectedAt);

        frame = new AutoBuyCycleFrame(
            global,
            candidates,
            candidateCount,
            structureCount,
            candidateCount - structureCount,
            resources,
            resourceCount,
            costs,
            costCount);
    }

    private static void AppendCandidate(
        AutoBuyCandidateKind kind,
        Guid uuid,
        Guid categoryId,
        in AutoBuyCandidateLevels levels,
        GameWorldState world,
        ref AutoBuyCandidateRow[] candidates,
        ref AutoBuyResourceRow[] resources,
        ref AutoBuyCostRow[] costs,
        ref int candidateCount,
        ref int resourceCount,
        ref int costCount)
    {
        if (!TryReadCosts(
                uuid,
                world,
                ref resources,
                ref costs,
                ref resourceCount,
                out var costStart,
                out var costRowCount,
                ref costCount))
        {
            return;
        }

        var owningView = OwningView(
            world, kind, uuid, categoryId, out var owningListId, out var owningViewId);
        Append(
            ref candidates,
            candidateCount,
            new AutoBuyCandidateRow(
                kind,
                uuid,
                owningView,
                levels.IsAvailable,
                levels.CurrentLevel,
                levels.QueuedLevels,
                levels.HasFiniteLevels,
                levels.IsMaxLevel,
                levels.IsMaxQueuedLevel,
                levels.MeetsNextLevelRequirements,
                costStart,
                costRowCount,
                owningListId,
                owningViewId));
        candidateCount++;
    }

    private static AutoBuyOwningViewStatus OwningView(
        GameWorldState world,
        AutoBuyCandidateKind kind,
        Guid candidateId,
        Guid categoryId,
        out Guid owningListId,
        out Guid owningViewId)
    {
        owningListId = Guid.Empty;
        owningViewId = Guid.Empty;
        if (!WorldLookup.TryFind(
                world.PurchaseViewRelations,
                candidateId,
                out var relation))
        {
            return RelationCategoryWasClean(world)
                ? AutoBuyOwningViewStatus.RelationMissing
                : AutoBuyOwningViewStatus.RelationUnreadable;
        }

        var expectedKind = kind == AutoBuyCandidateKind.Structure
            ? WorldPurchaseCandidateKind.Structure
            : WorldPurchaseCandidateKind.Upgrade;
        if (relation.Kind != expectedKind || relation.CategoryId != categoryId)
            return AutoBuyOwningViewStatus.RelationContradictory;

        switch (relation.Status)
        {
            case WorldPurchaseViewRelationStatus.Missing:
                return AutoBuyOwningViewStatus.RelationMissing;
            case WorldPurchaseViewRelationStatus.Unreadable:
                return AutoBuyOwningViewStatus.RelationUnreadable;
            case WorldPurchaseViewRelationStatus.Contradictory:
                return AutoBuyOwningViewStatus.RelationContradictory;
            case WorldPurchaseViewRelationStatus.Resolved:
                break;
            default:
                return AutoBuyOwningViewStatus.RelationUnreadable;
        }

        if (relation.RouteCount == 0)
            return AutoBuyOwningViewStatus.RelationContradictory;
        if (!WorldPurchaseViewRouteLookup.TryFindRange(
                world.PurchaseViewRoutes,
                candidateId,
                out var routeStart,
                out var routeCount) ||
            routeCount != relation.RouteCount)
            return AutoBuyOwningViewStatus.RelationContradictory;
        var unreadable = false;
        for (var index = 0; index < routeCount; index++)
        {
            var route = world.PurchaseViewRoutes[routeStart + index];
            if (route.ListId == Guid.Empty)
            {
                unreadable = true;
                continue;
            }

            var alreadyVisited = false;
            for (var prior = 0; prior < index; prior++)
            {
                if (world.PurchaseViewRoutes[routeStart + prior].ListId != route.ListId) continue;
                alreadyVisited = true;
                break;
            }
            if (alreadyVisited) continue;

            // Distinct authored lists are alternate routes. Every view carrying one list is the
            // complete gate chain for that route, so a visible child cannot bypass a locked parent.
            var routeReadable = true;
            var routeAvailable = true;
            for (var member = index; member < routeCount; member++)
            {
                var routeMember = world.PurchaseViewRoutes[routeStart + member];
                if (routeMember.ListId != route.ListId) continue;
                if (routeMember.ViewId == Guid.Empty ||
                    !WorldLookup.TryFind(world.Views, routeMember.ViewId, out var view))
                {
                    routeReadable = false;
                    continue;
                }
                if (!view.Available) routeAvailable = false;
            }

            if (routeReadable && routeAvailable)
            {
                owningListId = route.ListId;
                owningViewId = route.ViewId;
                return AutoBuyOwningViewStatus.Available;
            }
            if (!routeReadable) unreadable = true;
        }
        return unreadable
            ? AutoBuyOwningViewStatus.RelationUnreadable
            : AutoBuyOwningViewStatus.Unavailable;
    }

    private static bool RelationCategoryWasClean(GameWorldState world)
    {
        var categories = world.CollectionCategories.AsSpan();
        for (var index = 0; index < categories.Length; index++)
        {
            var category = categories[index];
            if (!string.Equals(
                    category.Category,
                    WorldPurchaseViewRelationReader.CategoryName,
                    StringComparison.Ordinal))
                continue;
            return category.IsClean;
        }
        return false;
    }

    /// <summary>
    /// Takes the candidate's price from the snapshot rather than asking the game for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to be <c>GetPurchaseCost()</c> per candidate, four hundred times a cycle, and the
    /// game's implementation rebuilds the whole cost list from scratch on every ask — six LINQ
    /// projections and six allocations, none of them cached. The collection pass now computes the
    /// same number once per entity on the worker thread.
    /// </para>
    /// <para>
    /// A candidate with no published price fails rather than being priced at nothing. The deriver
    /// withholds a price it could not complete, and treating "not priced" as "free" is exactly the
    /// way to commit to a purchase that cannot be paid for.
    /// </para>
    /// </remarks>
    private static bool TryReadCosts(
        Guid entityId,
        GameWorldState world,
        ref AutoBuyResourceRow[] resources,
        ref AutoBuyCostRow[] costs,
        ref int resourceCount,
        out int costStart,
        out int costRowCount,
        ref int costCount)
    {
        costStart = costCount;
        costRowCount = 0;

        if (!WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, entityId, out var start, out var count))
            return false;

        // The projection is math-free: emit one cost row per published entry, referencing a
        // deduplicated resource row. The evaluator groups cost rows by ResourceRowIndex and applies
        // the stricter-than-native duplicate-resource rule when it computes affordability.
        for (var offset = 0; offset < count; offset++)
        {
            var published = world.PurchaseCosts[start + offset];
            if (published.ResourceId == Guid.Empty) return false;

            if (!TryResolveResourceRow(
                    published.ResourceId,
                    world,
                    ref resources,
                    ref resourceCount,
                    out var resourceRowIndex))
            {
                return false;
            }

            Append(
                ref costs,
                costCount,
                new AutoBuyCostRow(
                    resourceRowIndex,
                    published.Amount,
                    published.ExactGroupedLevels,
                    published.ExactGroupedAmount));
            costCount++;
            costRowCount++;
        }

        return true;
    }

    /// <summary>
    /// Materializes one resource row from the shared world snapshot rather than by reading the game.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every field here used to be eight reflected native calls per distinct cost resource, per
    /// candidate, per cycle — reads the shared collection pass has already made for the whole game.
    /// </para>
    /// <para>
    /// A resource named by a cost but absent from the snapshot fails the candidate rather than
    /// substituting a default. A missing row means the pass could not read that resource, and
    /// treating "unreadable" as "free" is exactly the way to buy something unaffordable.
    /// </para>
    /// <para>
    /// The already-emitted rows are the dedup index. A dictionary would be faster per lookup and is
    /// the shape this had while it ran on the main thread, but a worker definition may hold no
    /// collection and rebuilding one per cycle would allocate; the distinct resources named across a
    /// whole save's costs number in the tens, so the scan is over a span the loop just wrote.
    /// </para>
    /// </remarks>
    private static bool TryResolveResourceRow(
        Guid resourceId,
        GameWorldState world,
        ref AutoBuyResourceRow[] resources,
        ref int resourceCount,
        out int resourceRowIndex)
    {
        for (var index = 0; index < resourceCount; index++)
        {
            if (resources[index].ResourceId != resourceId) continue;
            resourceRowIndex = index;
            return true;
        }

        if (!TryFindResource(world, resourceId, out var resource))
        {
            resourceRowIndex = 0;
            return false;
        }

        var reading = resource.Reading;
        var row = new AutoBuyResourceRow(
            resourceId,
            reading.Traits.BandwidthResource,
            reading.Quantity,
            resource.TrueQuantity,
            WorldResourceCoordinate.NativeCostAmount(in resource),
            reading.Quality,
            reading.Modifiers.AttributeCostMod,
            resource.IsCapped,
            reading.Capacity,
            reading.Visible);
        resourceRowIndex = resourceCount;
        Append(ref resources, resourceCount, row);
        resourceCount++;
        return true;
    }

    /// <summary>
    /// Finds a resource across both populations the snapshot keeps.
    /// </summary>
    /// <remarks>
    /// Registered resources and element-owned ones are separate tables because the game keeps them
    /// separately: a harvest element creates its <c>ResourceSO</c> at runtime and never registers it,
    /// so it appears in no global aggregate. A cost naming one would resolve against
    /// <c>ResourceSO.All</c> and find nothing, and the candidate would be silently skipped as
    /// unreadable — so both are searched, registered first because that is the overwhelmingly common
    /// case.
    /// </remarks>
    private static bool TryFindResource(GameWorldState world, Guid resourceId, out WorldResource resource)
    {
        if (WorldLookup.TryFind(world.Resources, resourceId, out resource)) return true;
        if (WorldLookup.TryFind(world.HarvestResources, resourceId, out var harvested))
        {
            resource = harvested.Resource;
            return true;
        }

        resource = default;
        return false;
    }

    /// <summary>
    /// One candidate's level and availability facts, taken from the shared snapshot.
    /// </summary>
    /// <remarks>
    /// Every value here is a plain field or an exact restatement of the game's own predicate, so the
    /// snapshot can answer all of them without the game being asked again:
    /// <c>HasFiniteLevels()</c> is <c>maxLevel &gt; 0</c>, <c>IsMaxLevel()</c> is that plus
    /// <c>level &gt;= maxLevel</c>, and <c>IsMaxQueuedLevel()</c> is that plus
    /// <c>level + queuedLevels &gt;= maxLevel</c>. Structures have no bounded-level concept at all,
    /// which is why theirs are false rather than derived.
    /// <para>
    /// The per-level requirement verdict is the one term that is derived rather than restated, because
    /// the game's own answer takes the level as an argument. It is evaluated here from the published
    /// condition rows; see W58.
    /// </para>
    /// </remarks>
    private readonly struct AutoBuyCandidateLevels
    {
        internal AutoBuyCandidateLevels(
            bool isAvailable,
            int currentLevel,
            int queuedLevels,
            bool hasFiniteLevels,
            bool isMaxLevel,
            bool isMaxQueuedLevel,
            bool meetsNextLevelRequirements)
        {
            IsAvailable = isAvailable;
            CurrentLevel = currentLevel;
            QueuedLevels = queuedLevels;
            HasFiniteLevels = hasFiniteLevels;
            IsMaxLevel = isMaxLevel;
            IsMaxQueuedLevel = isMaxQueuedLevel;
            MeetsNextLevelRequirements = meetsNextLevelRequirements;
        }

        internal bool IsAvailable { get; }
        internal int CurrentLevel { get; }
        internal int QueuedLevels { get; }
        internal bool HasFiniteLevels { get; }
        internal bool IsMaxLevel { get; }
        internal bool IsMaxQueuedLevel { get; }
        internal bool MeetsNextLevelRequirements { get; }
    }

    private static AutoBuyCandidateLevels Levels(GameWorldState world, in WorldStructure structure) =>
        new(
            structure.Reading.Unlocked,
            (int)structure.Reading.Level.ToDouble(),
            (int)structure.Reading.QueuedLevels.ToDouble(),
            hasFiniteLevels: false,
            isMaxLevel: false,
            isMaxQueuedLevel: false,
            MeetsRequirements(
                world, structure.EntityId, WorldRequirementEvaluator.StructureCheckLevel(in structure)));

    private static AutoBuyCandidateLevels Levels(GameWorldState world, in WorldUpgrade upgrade) =>
        new(
            upgrade.Reading.Available,
            upgrade.Reading.Level,
            upgrade.Reading.QueuedLevels,
            upgrade.IsBounded,
            upgrade.IsExhausted,
            upgrade.IsBounded && upgrade.CommittedLevel >= upgrade.Reading.MaxLevel,
            MeetsRequirements(
                world, upgrade.EntityId, WorldRequirementEvaluator.UpgradeCheckLevel(in upgrade)));

    /// <summary>
    /// Whether the published conditions gating this entity's next level all hold.
    /// </summary>
    /// <remarks>
    /// Anything short of met refuses the purchase, including a condition the suite cannot evaluate.
    /// The distinction is real and is kept where it can be acted on — the differential pass names the
    /// unmodelled class — but it makes no difference to what may be planned, and carrying it into the
    /// frame would invite a consumer to treat one of the two as passable.
    /// </remarks>
    private static bool MeetsRequirements(GameWorldState world, Guid entityId, long level) =>
        WorldRequirementEvaluator.Evaluate(world, entityId, level) == WorldRequirementVerdict.Met;

    /// <summary>
    /// One of the game's global counts, from the snapshot rather than from the game.
    /// </summary>
    /// <remarks>
    /// Multi-buy and bulk development are <c>IntVariable</c>s, and the game's accessor for one is
    /// <c>AsInt()</c> — which reaches <c>ValueModifierRecord.GetValue()</c> and so recalculates and
    /// re-stamps an observable when the record is dirty. Calling it every cycle made the game
    /// recompute on the suite's schedule, which is the write-on-read the collector exists to avoid.
    /// The collector reads the same variables' modifier records and folds them itself.
    /// <para>
    /// One means "no multiplier" and is the right answer for a snapshot that has not been published
    /// yet or a build where the registry did not resolve: the suite then buys one level at a time,
    /// which is always safe. The mutation path still sets the multiplier natively and verifies what
    /// the game did with it.
    /// </para>
    /// </remarks>
    private static int ReadGlobalCount(GameWorldState world, Guid variableId) =>
        WorldPurchaseGrouping.Read(world.IntVariables, variableId);

    private static void Append<T>(ref T[] buffer, int count, in T value)
    {
        if (count >= buffer.Length) Array.Resize(ref buffer, Math.Max(4, buffer.Length * 2));
        buffer[count] = value;
    }
}
