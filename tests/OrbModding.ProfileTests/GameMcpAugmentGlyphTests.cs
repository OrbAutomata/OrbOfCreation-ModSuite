using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// The twenty-two Augment Glyphs, which are the whole of what <c>GlyphSO</c> publishes now. These
/// pin what a level buys, and that the offer to buy one exists only where the game draws a button.
/// </summary>
public sealed class GameMcpAugmentGlyphTests
{
    private static readonly Guid AugmentId = Guid.Parse("01273b00-0000-4000-8000-000000000001");

    /// <summary>Distinct, Weak and Wrath: augments that carry <c>augmentsSpells</c> false.</summary>
    private static readonly Guid QuietAugmentId = Guid.Parse("01273b00-0000-4000-8000-000000000002");

    private static readonly Guid SecondAugmentId =
        Guid.Parse("01273b00-0000-4000-8000-000000000004");

    private static WorldGlyph Glyph(
        Guid id,
        bool learned,
        bool augmentsSpells,
        int level = 1,
        int slots = 2,
        int freeSlots = 0) => new(
        id,
        level,
        freeLevels: 0,
        discoveryRarityLevel: 0,
        learned,
        discoverable: true,
        discoveryRequired: false,
        augmentsSpells,
        requiresDuration: false,
        requiresToggleable: false,
        masteryReqCount: 0,
        freeUsages: BigDouble.Zero,
        freeLoadoutUsages: BigDouble.Zero,
        maxUsages: BigDouble.Zero,
        maximumUsages: slots,
        maximumFreeUsages: freeSlots);

    private static GameWorldState World(params WorldGlyph[] glyphs) => new()
    {
        AugmentGlyphs = PublicationTable<WorldGlyph>.Create(
            glyphs.OrderBy(glyph => glyph.EntityId).ToArray()),
        CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
        {
            new WorldCollectionCategoryStatus(
                "augment glyphs", WorldCategoryOutcome.Collected, glyphs.Length, 0, string.Empty),
        }),
        CollectedAtEpoch = 71,
        CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
    };

    /// <summary>The Augment Table bought, which is the only state where the game draws a level button.</summary>
    private static GameWorldState WithAugmentTable(GameWorldState world, bool unlocked) =>
        world with
        {
            Views = PublicationTable<WorldView>.Create(new[]
            {
                new WorldView(KnownEntities.MagicGlyphsUpgrade.Uuid, false, false, unlocked),
            }),
        };

    /// <summary>
    /// What a level buys, in the game's own two words. <c>GetMaxUsages()</c> prints as
    /// <c>[N] Slot</c> and <c>GetFreeUsages()</c> as <c>[M] Free Slot</c> in the level panel's own
    /// detail nodes, so those are the two numbers the row carries and the suite's old
    /// <c>usableCount</c> is gone with the word.
    /// </summary>
    [Fact]
    public void Every_row_carries_the_slots_a_level_buys_and_nothing_the_retired_partition_left()
    {
        var context = GameMcpTestHarness.Context(World(
            Glyph(AugmentId, learned: true, augmentsSpells: true, slots: 2, freeSlots: 1)));

        var row = GameMcpTestHarness
            .Json(GameMcpWorldQuery.ListRows(context, "augment-glyphs", 0, 50))["rows"]!
            .Values<JObject>()
            .Single()!;

        Assert.Equal(
            new[]
            {
                "uuid", "name", "state", "slots", "freeSlots", "paidLevel", "bonusLevel",
                "totalLevel",
            },
            row.Properties().Select(property => property.Name).ToArray());
        Assert.Equal(2, (int)row["slots"]!);
        Assert.Equal(1, (int)row["freeSlots"]!);
    }

    /// <summary>
    /// The retired columns are retired on the detail read too: <c>population</c> named a partition
    /// that no longer exists, and <c>screen</c> repeated one category-level fact on every row.
    /// </summary>
    [Fact]
    public void The_detail_row_carries_neither_a_population_nor_a_screen()
    {
        var context = GameMcpTestHarness.Context(World(
            Glyph(AugmentId, learned: true, augmentsSpells: true)));

        var detail = GameMcpTestHarness.Detail(context, AugmentId);
        var row = detail["row"]!;

        Assert.Equal("augment-glyphs", (string?)detail["category"]);
        Assert.Null(row["population"]);
        Assert.Null(row["screen"]);
        Assert.Null(row["usableCount"]);
        Assert.Equal(2, (int)row["slots"]!);
        Assert.Equal(0, (int)row["freeSlots"]!);
    }

    /// <summary>
    /// <c>GlyphSO.IsVisible()</c> is a call to <c>IsAvailable()</c>, so the two verdicts are one
    /// fact and a glyph can never be shown as available-but-invisible.
    /// </summary>
    [Fact]
    public void Visible_and_available_are_the_one_fact_the_game_holds()
    {
        var held = GameMcpTestHarness.Context(World(
            Glyph(AugmentId, learned: true, augmentsSpells: true)));
        var blocked = GameMcpTestHarness.Context(World(
            Glyph(AugmentId, learned: false, augmentsSpells: true)));

        var open = GameMcpTestHarness.Detail(held, AugmentId)["predicates"]!;
        Assert.True((bool)open["visible"]!["available"]!);
        Assert.True((bool)open["available"]!["available"]!);

        var shut = GameMcpTestHarness.Detail(blocked, AugmentId)["predicates"]!;
        Assert.False((bool)shut["visible"]!["available"]!);
        Assert.False((bool)shut["available"]!["available"]!);
        Assert.Equal(
            (string?)shut["available"]!["reason"],
            (string?)shut["visible"]!["reason"]);
    }

    /// <summary>
    /// The gate that decides what may be spent on a spell, read off the level a player holds.
    /// Read off <c>augmentsSpells</c> instead, the options dropped Distinct, Weak and Wrath —
    /// three augments a player already owns.
    /// </summary>
    [Fact]
    public void The_augment_options_read_the_level_a_player_holds_not_augmentsSpells()
    {
        var recipeId = Guid.Parse("36375616-0000-4000-8000-000000000001");
        var world = World(
            Glyph(QuietAugmentId, learned: true, augmentsSpells: false),
            Glyph(SecondAugmentId, learned: true, augmentsSpells: true, level: 0));

        var context = GameMcpTestHarness.Context(Recipe(world, recipeId));
        var next = GameMcpTestHarness.Detail(context, recipeId)["row"]!["loadoutAdd"]!;

        Assert.True((bool)next["available"]!);

        // The unlevelled augment is not an option — a glyph at level zero is not owned yet — so the
        // one that comes back is the one `augmentsSpells` would have dropped.
        var option = Assert.Single(next["augmentOptions"]!.Values<JObject>())!;
        Assert.Equal(
            GameMcpTestHarness.Handle(QuietAugmentId),
            (string?)option["glyph"]!["uuid"]);
        Assert.Equal(2, (int)option["slots"]!);
        Assert.Null(option["usableCount"]);
    }

    private static GameWorldState Recipe(GameWorldState world, Guid recipeId) =>
        world with
        {
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(new[]
            {
                new WorldSpellRecipe(
                    recipeId,
                    true,
                    0,
                    BigDouble.Zero,
                    0,
                    false,
                    false,
                    false,
                    0,
                    1d,
                    1,
                    false,
                    BigDouble.One,
                    BigDouble.One,
                    BigDouble.One,
                    BigDouble.One,
                    BigDouble.One,
                    BigDouble.One,
                    false,
                    PublicationTable<WorldSpellRecipeGlyph>.Empty),
            }),
            SpellWorkbench = new WorldSpellWorkbench(0, 3, true, 4, 12, 3, 9),
            Views = PublicationTable<WorldView>.Create(new[]
            {
                new WorldView(KnownEntities.MagicSpellbookLoadout.Uuid, false, false, true),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "augment glyphs", WorldCategoryOutcome.Collected, 2, 0, string.Empty),
                new WorldCollectionCategoryStatus(
                    "spell-recipes", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
        };

    /// <summary>
    /// An augment glyph is never a discovery-tree offer once discovered, so the offer fact rides on
    /// the row's own discovery block rather than standing in for visibility.
    /// </summary>
    [Fact]
    public void The_offer_fact_rides_on_the_discovery_block_rather_than_on_visibility()
    {
        var offered = Guid.Parse("01273b00-0000-4000-8000-000000000003");
        var world = World(Glyph(offered, learned: false, augmentsSpells: true, level: 0));
        var context = GameMcpTestHarness.Context(world with
        {
            DiscoveryTrees = PublicationTable<WorldDiscoveryTree>.Create(new[]
            {
                new WorldDiscoveryTree(
                    Guid.Parse("0d15c000-0000-4000-8000-000000000001"),
                    true, 2, BigDouble.Zero, 1, false, Guid.Empty,
                    new[] { offered }, false, true, Array.Empty<WorldDiscoveryTreeCost>(),
                    Guid.Empty, Guid.Empty, 0, 0, false, 1, 1, 4, 2, true, true, false),
            }),
        });

        var row = GameMcpTestHarness.Detail(context, offered)["row"]!;

        Assert.True((bool)row["discover"]!["offered"]!);
        Assert.False((bool)GameMcpTestHarness.Detail(context, offered)
            ["predicates"]!["visible"]!["available"]!);
    }

    /// <summary>
    /// <c>GlyphSO.IsAvailable()</c> returns <c>discovered</c> for a discoverable glyph, and every
    /// glyph the world publishes is one, so the only reason left for a locked row is its own
    /// discovery — and the row says exactly that instead of borrowing a prerequisite sentence.
    /// </summary>
    [Fact]
    public void An_undiscovered_augment_glyph_says_it_is_undiscovered()
    {
        var context = GameMcpTestHarness.Context(World(
            Glyph(AugmentId, learned: false, augmentsSpells: true, level: 0)));

        var row = GameMcpTestHarness.Detail(context, AugmentId)["row"]!;

        Assert.Equal("ERR_LOCKED", (string?)row["reasonCode"]);
        Assert.Equal("This has not been discovered yet.", (string?)row["reason"]);
        Assert.Null(row["blockedBy"]);
    }

    /// <summary>
    /// The general rule, pinned: a level offer exists only where the game draws a level button,
    /// never because an interface says it can. <c>GlyphSO.CanLevel()</c> is <c>ldc.i4.1; ret</c> —
    /// the constant <c>true</c> — and while Magic &gt; Augments &gt; Upgrade is locked the game
    /// instantiates no level panel, so there is no press to offer.
    /// </summary>
    [Fact]
    public void A_locked_augment_table_refuses_the_level_in_the_screens_own_words()
    {
        var context = GameMcpTestHarness.Context(WithAugmentTable(
            World(Glyph(AugmentId, learned: true, augmentsSpells: true)),
            unlocked: false));

        var purchase = GameMcpTestHarness.Detail(context, AugmentId)["row"]!["purchase"]!;

        Assert.False((bool)purchase["available"]!);
        Assert.Equal("ERR_LOCKED", (string?)purchase["reasonCode"]);
        Assert.Equal(
            "Magic > Augments > Upgrade is not unlocked yet, so the game draws no level button " +
            "for an augment glyph. Buy the Upgrade Glyphs upgrade first.",
            (string?)purchase["reason"]);
    }

    /// <summary>
    /// The same row with the Augment Table bought: the button exists, so the offer does. Without
    /// this the test above would pass on a surface that never offers a level at all.
    /// </summary>
    [Fact]
    public void An_unlocked_augment_table_lets_the_level_offer_stand()
    {
        var context = GameMcpTestHarness.Context(WithAugmentTable(
            World(Glyph(AugmentId, learned: true, augmentsSpells: true)),
            unlocked: true));

        var purchase = GameMcpTestHarness.Detail(context, AugmentId)["row"]!["purchase"]!;

        Assert.DoesNotContain(
            "Magic > Augments > Upgrade",
            (string?)purchase["reason"] ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The same rule at the verb, not only on the row: <c>game_level_up</c> is admitted for an
    /// augment glyph only where the game draws the button. The row saying "locked" and the verb
    /// taking the call anyway is the shape that let a round spend a currency on a press that does
    /// not exist.
    /// </summary>
    [Fact]
    public void The_level_verb_admits_a_target_only_where_the_game_draws_the_button()
    {
        var glyph = Glyph(AugmentId, learned: true, augmentsSpells: true);

        var locked = WithAugmentTable(World(glyph), unlocked: false) with
        {
            EntityIdentities = GameMcpTestHarness.EntityCatalog,
        };
        Assert.False(GameMcpEntityCapabilityMap.TryResolveGenericLevelType(
            locked, AugmentId, out _, out var reason));
        Assert.EndsWith(
            "has no level button yet: Magic > Augments > Upgrade is locked until the Upgrade " +
            "Glyphs upgrade is bought.",
            reason,
            StringComparison.Ordinal);

        var open = WithAugmentTable(World(glyph), unlocked: true) with
        {
            EntityIdentities = GameMcpTestHarness.EntityCatalog,
        };
        Assert.True(GameMcpEntityCapabilityMap.TryResolveGenericLevelType(
            open, AugmentId, out var nativeType, out _));
        Assert.Equal("GlyphSO", nativeType);
    }
}
