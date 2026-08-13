using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OrbAutomata;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.Verification;

/// <summary>
/// The identity walk feeds the live collision check, and a walk that quietly skips a table would make
/// that check weaker rather than fail it. These tests are mostly about the skipping.
/// </summary>
public sealed class WorldIdentityWalkTests
{
    [Fact]
    public void AnEmptySnapshotYieldsNothing()
    {
        Assert.Empty(WorldIdentityWalk.Enumerate(GameWorldStateDefaults.Empty));
    }

    [Fact]
    public void EveryRowInEveryTableIsVisited()
    {
        var mana = Guid.NewGuid();
        var stone = Guid.NewGuid();
        var hoard = Guid.NewGuid();

        var world = new GameWorldState
        {
            IntVariables = WorldTable.Create(
                new WorldNumberVariable(mana, new BigDouble(1d), isPercent: false),
                new WorldNumberVariable(stone, new BigDouble(2d), isPercent: false)),
            TreasurePools = WorldTable.Create(
                new WorldTreasurePool(hoard, 3, new BigDouble(0.5d), false, 1, false)),
        };

        Assert.Equal(
            new[] { mana, stone, hoard }.OrderBy(id => id),
            WorldIdentityWalk.Enumerate(world).Select(sighting => sighting.Id).OrderBy(id => id));
    }

    /// <summary>
    /// Every sighting names the table it came from, because the check downstream asserts uniqueness
    /// inside one table and reports sharing across tables — neither of which a bare identity supports.
    /// </summary>
    [Fact]
    public void EveryIdentityIsReportedUnderTheTableItWasPublishedIn()
    {
        var mana = Guid.NewGuid();
        var hoard = Guid.NewGuid();

        var world = new GameWorldState
        {
            IntVariables = WorldTable.Create(
                new WorldNumberVariable(mana, new BigDouble(1d), isPercent: false)),
            TreasurePools = WorldTable.Create(
                new WorldTreasurePool(hoard, 3, new BigDouble(0.5d), false, 1, false)),
        };

        Assert.Equal(
            new[] { ("IntVariables", mana), ("TreasurePools", hoard) }.OrderBy(row => row.Item1),
            WorldIdentityWalk.Enumerate(world)
                .Select(sighting => (sighting.Table, sighting.Id))
                .OrderBy(row => row.Table));
    }

    /// <summary>
    /// The collision check exists to find exactly this, and it can only find it if the walk reports
    /// the same identity twice rather than deduplicating on the way out.
    /// </summary>
    [Fact]
    public void AnIdentityHeldByTwoTablesIsYieldedTwice()
    {
        var shared = Guid.NewGuid();

        var world = new GameWorldState
        {
            IntVariables = WorldTable.Create(
                new WorldNumberVariable(shared, new BigDouble(1d), isPercent: false)),
            DoubleVariables = WorldTable.Create(
                new WorldNumberVariable(shared, new BigDouble(2d), isPercent: false)),
        };

        Assert.Equal(2, WorldIdentityWalk.Enumerate(world).Count(sighting => sighting.Id == shared));
    }

    /// <summary>
    /// Tables the walk deliberately does not read, and why.
    /// </summary>
    /// <remarks>
    /// <c>PurchaseCosts</c> is several rows per entity, keyed by an identity the structures table
    /// already owns. Walking it would report every priced structure as colliding with itself, which
    /// would turn the collision check from a real invariant into noise everyone learns to ignore.
    /// <c>PlotActions</c> is worse: its rows are pairs, so neither of the two identities on one is
    /// the row's own, and both belong to a table that already claims them. <c>PlotActionInstances</c>
    /// is that edge several times over, one row per instance the plot holds. <c>PlotAuthoring</c>,
    /// <c>PlotPhaseDescriptors</c> and <c>EffectBlocks</c> are all second readings of an entity the
    /// plot and action tables already claim, said about the entity rather than as it. So is
    /// <c>EntityRequirements</c>, whose rows are conditions on an upgrade, structure, or prerequisite
    /// link already owned elsewhere. <c>PrerequisiteLinkTiers</c> is the volatile state for each
    /// `(link, tier)` relation, and its link identity belongs to the authored catalog. <c>ActionQueueSlots</c>
    /// is a position in a list, which is no entity at all. <c>MasteryExperience</c> is an ordered input journal keyed by sequence; its
    /// source identity points at a recipe or equipment row that already owns that identity.
    /// <c>ConsumableTypes</c>, <c>ConsumableCosts</c>, <c>ConsumableUsages</c>, and
    /// <c>ConsumableCounts</c> are relation rows keyed by a consumable the primary table already
    /// owns. Their secondary identities or levels describe one edge or stock tier rather than a
    /// second entity namespace. <c>CraftingDecisions</c>, <c>CraftingDecisionCosts</c>, and
    /// <c>CraftingQueueEntries</c> are the
    /// current execution route and price evidence keyed by a recipe the crafting-recipe table
    /// already owns. <c>PurchaseViewRoutes</c> likewise holds authored edges between a
    /// candidate, list, and view whose identities belong to their primary tables.
    /// <c>CraftingStationOptions</c> and <c>CraftingStationDrains</c> describe selectors and costs
    /// keyed by a runtime station whose own row owns the identity. <c>CollectionCategories</c> is
    /// <c>PlayerLoadoutEntries</c>, <c>SnapshotSlots</c>, and <c>SnapshotEntries</c> are saved-entry
    /// and owner/slot relations keyed by the player or snapshot-list rows that own their UUIDs.
    /// <c>HarvestElementControls</c>, <c>HarvestActionControls</c>, and
    /// <c>HarvestLifecycleCosts</c> are list-state and cost relations keyed by harvest elements,
    /// actions, and resources whose identities come from their primary tables or the live catalog.
    /// <c>CollectionCategories</c> is
    /// availability evidence about one collector pass, not a native
    /// row and not a second identity namespace. <c>ScribeWork</c>,
    /// <c>StructureEnchantments</c>, <c>ScrollTargets</c>, and
    /// <c>ScrollTargetEvidence</c> are relationship or evidence rows keyed by recipes, structures,
    /// Scrolls, and enchantments whose owning tables already carry those identities.
    /// <c>Targeting</c> is the one current request and its candidate edges; its candidates are
    /// structures already owned by the structures table, while the request itself has no UUID.
    /// <c>RequirementListMembers</c> is one row per position in an authored list variable: the list
    /// and its members are both entities other tables already claim, and the row itself is the
    /// membership between them. <c>UpgradeListMemberships</c> is the same shape for the upgrade
    /// panels: both the upgrade and the authored list it sits on are claimed elsewhere, and the row
    /// is only the edge between them, and <c>GlyphListMemberships</c> is that same edge for the
    /// authored glyph populations. <c>EntityKeywords</c> is the same shape again: the entity and
    /// the type asset whose display name is the keyword are both claimed elsewhere — the entity by its
    /// own category and the type by the lifecycle identity catalog — and the row is only the authored
    /// membership between them.
    /// <c>TypeModifierTotals</c>, <c>KeywordModifiers</c> and <c>SpellTypeResonance</c> are derived
    /// arithmetic rather than entities: the first two are keyed by a type asset the identity catalog
    /// already claims and by the record name on it, and the third is keyed by a loadout position,
    /// which is not an identity for the same reason <c>SpellSlots</c> is not.
    /// <para>
    /// <c>ActionQueues</c> is not among them: a queue is a list variable with a uuid of its own that
    /// no other category collects, so it is walked like any other entity.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> NotIdentityTables = new(StringComparer.Ordinal)
    {
        "CollectionCategories",
        "PurchaseCosts",
        "PurchaseViewRoutes",
        "UpgradeListMemberships",
        "GlyphListMemberships",
        "PlotActions",
        "PlotActionInstances",
        "ActionQueueSlots",
        "SpellSlots",
        "SpellCosts",
        "MasteryExperience",
        "ConsumableTypes",
        "ConsumableCosts",
        "ConsumableUsages",
        "ConsumableCounts",
        "CraftingDecisions",
        "CraftingDecisionCosts",
        "CraftingQueueEntries",
        "ConceptRecipes",
        "AlchemyInstances",
        "AlchemyCosts",
        "AlchemyUsageCosts",
        "ScribeWork",
        "StructureEnchantments",
        "ScrollTargets",
        "ScrollTargetEvidence",
        "Targeting",
        "PlotAuthoring",
        "PlotPhaseDescriptors",
        "EffectBlocks",
        "EntityRequirements",
        "RequirementListMembers",
        "PrerequisiteLinkTiers",
        "CraftingStationOptions",
        "CraftingStationDrains",
        "PlayerLoadoutEntries",
        "SnapshotSlots",
        "SnapshotEntries",
        "HarvestElementControls",
        "HarvestActionControls",
        "HarvestLifecycleCosts",
        "SpellAuthoredCosts",
        "SpellRelations",
        "EntityKeywords",
        "TypeModifiers",
        "TypeModifierContributions",
        "TypeSubtypes",
        "ChallengeTypes",
        "ChallengeTypeMemberships",
        "SpellSlotTypes",
        "TypeModifierTotals",
        "KeywordModifiers",
        "SpellTypeResonance",
    };

    /// <summary>
    /// The walk selects tables by "row implements <c>IWorldEntity</c>", so a table added later whose
    /// row does not would be skipped in silence. That is the one way this can rot, so it is asserted
    /// against the snapshot's real shape rather than against a fixture — with every exclusion named
    /// above rather than merely happening.
    /// </summary>
    [Fact]
    public void EveryPublishedTableHoldsRowsTheWalkCanRead()
    {
        var unreadable = new List<string>();

        var properties = typeof(GameWorldState).GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        var tables = 0;
        foreach (var property in properties)
        {
            var type = property.PropertyType;
            if (!type.IsGenericType) continue;
            if (type.GetGenericTypeDefinition() != typeof(PublicationTable<>)) continue;

            tables++;
            if (NotIdentityTables.Contains(property.Name)) continue;

            var row = type.GetGenericArguments()[0];
            if (!typeof(IWorldEntity).IsAssignableFrom(row)) unreadable.Add(property.Name);
        }

        Assert.True(tables > 0, "the snapshot published no tables at all; the filter must be wrong");
        Assert.True(
            unreadable.Count == 0,
            $"these tables hold rows without an identity the walk can read: {string.Join(", ", unreadable)}");
    }
}
