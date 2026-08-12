using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// The 47 glyphs are two populations, and every count a reader takes off the page means something
/// different for each. These pin which authored fact decides the split, and that no surface reads
/// the one that does not.
/// </summary>
public sealed class GameMcpGlyphPopulationTests
{
    private static readonly Guid AugmentId = Guid.Parse("01273b00-0000-4000-8000-000000000001");

    /// <summary>Distinct, Weak and Wrath: discoverable augments that carry `augmentsSpells` false.</summary>
    private static readonly Guid QuietAugmentId = Guid.Parse("01273b00-0000-4000-8000-000000000002");

    private static readonly Guid UnlockerId = Guid.Parse("02e0cd00-0000-4000-8000-000000000001");

    private static WorldGlyph Glyph(
        Guid id,
        bool discoverable,
        bool learned,
        bool augmentsSpells,
        int level = 1) => new(
        id,
        level,
        freeLevels: 0,
        discoveryRarityLevel: 0,
        learned,
        discoverable,
        discoveryRequired: false,
        augmentsSpells,
        requiresDuration: false,
        requiresToggleable: false,
        masteryReqCount: 0,
        freeUsages: BigDouble.Zero,
        freeLoadoutUsages: BigDouble.Zero,
        maxUsages: BigDouble.Zero,
        maximumUsages: 2);

    private static GameWorldState World(params WorldGlyph[] glyphs) => new()
    {
        Glyphs = PublicationTable<WorldGlyph>.Create(
            glyphs.OrderBy(glyph => glyph.EntityId).ToArray()),
        CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
        {
            new WorldCollectionCategoryStatus(
                "glyphs", WorldCategoryOutcome.Collected, glyphs.Length, 0, string.Empty),
        }),
        CollectedAtEpoch = 71,
        CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
    };

    /// <summary>
    /// The column exists so the spread between "available", "discovered" and "tiles on the grid" is
    /// self-explaining instead of costing a reader an investigation.
    /// </summary>
    [Fact]
    public void Every_glyph_row_says_which_population_it_belongs_to()
    {
        var context = GameMcpTestHarness.Context(World(
            Glyph(AugmentId, discoverable: true, learned: true, augmentsSpells: true),
            Glyph(UnlockerId, discoverable: false, learned: true, augmentsSpells: false)));

        // Rows come back sorted by identity, and the augment's is the lower of the two.
        var rows = GameMcpTestHarness
            .Json(GameMcpWorldQuery.ListRows(context, "glyphs", 0, 50))["rows"]!
            .Values<JObject>()
            .Select(row => (string?)row!["population"])
            .ToArray();

        Assert.Equal(new[] { "augment", "unlocker" }, rows);
        Assert.Equal(
            "augment",
            (string?)GameMcpTestHarness.Detail(context, AugmentId)["row"]!["population"]);
    }

    /// <summary>
    /// `augmentsSpells` is false for three members of the augment list, so it splits 19/28 where the
    /// population splits 22/25. A row that read it called those three unlockers.
    /// </summary>
    [Fact]
    public void An_augment_that_carries_augmentsSpells_false_is_still_an_augment()
    {
        var context = GameMcpTestHarness.Context(World(
            Glyph(QuietAugmentId, discoverable: true, learned: true, augmentsSpells: false)));

        Assert.Equal(
            "augment",
            (string?)GameMcpTestHarness.Detail(context, QuietAugmentId)["row"]!["population"]);
    }

    /// <summary>
    /// <c>GlyphSO.IsVisible()</c> is a call to <c>IsAvailable()</c>, so the two verdicts are one
    /// fact and an unlocker can never be shown as available-but-invisible.
    /// </summary>
    [Fact]
    public void An_unlockers_visible_and_available_are_the_one_fact_the_game_holds()
    {
        var held = GameMcpTestHarness.Context(World(
            Glyph(UnlockerId, discoverable: false, learned: true, augmentsSpells: false)));
        var blocked = GameMcpTestHarness.Context(World(
            Glyph(UnlockerId, discoverable: false, learned: false, augmentsSpells: false)));

        var open = GameMcpTestHarness.Detail(held, UnlockerId)["predicates"]!;
        Assert.True((bool)open["visible"]!["available"]!);
        Assert.True((bool)open["available"]!["available"]!);

        var shut = GameMcpTestHarness.Detail(blocked, UnlockerId)["predicates"]!;
        Assert.False((bool)shut["visible"]!["available"]!);
        Assert.False((bool)shut["available"]!["available"]!);
        Assert.Equal(
            (string?)shut["available"]!["reason"],
            (string?)shut["visible"]!["reason"]);
    }

    /// <summary>
    /// The two gates that decide what a spell's core slots may hold and what may be spent on it.
    /// Read off <c>augmentsSpells</c>, they admitted three augments as core glyphs and dropped the
    /// same three from the options a player already owns.
    /// </summary>
    [Fact]
    public void The_spell_surfaces_split_the_two_populations_by_the_field_that_splits_them()
    {
        var recipeId = Guid.Parse("36375616-0000-4000-8000-000000000001");
        var world = World(
            Glyph(QuietAugmentId, discoverable: true, learned: true, augmentsSpells: false),
            Glyph(UnlockerId, discoverable: false, learned: true, augmentsSpells: false));

        var usable = GameMcpTestHarness.Context(Recipe(world, recipeId, UnlockerId));
        var next = GameMcpTestHarness.Detail(usable, recipeId)["row"]!["loadoutAdd"]!;

        Assert.True((bool)next["available"]!);
        var option = Assert.Single(next["augmentOptions"]!.Values<JObject>())!;
        Assert.Equal(
            GameMcpTestHarness.Handle(QuietAugmentId),
            (string?)option["glyph"]!["uuid"]);

        // The same glyph in a core slot is the refusal, in the word that already existed for it.
        var wrong = GameMcpTestHarness.Context(Recipe(world, recipeId, QuietAugmentId));
        var refused = GameMcpTestHarness.Detail(wrong, recipeId)["row"]!["loadoutAdd"]!;

        Assert.False((bool)refused["available"]!);
        Assert.Equal("ERR_LOCKED", (string?)refused["reasonCode"]);
        Assert.Null(refused["augmentOptions"]);
    }

    private static GameWorldState Recipe(GameWorldState world, Guid recipeId, Guid coreGlyphId) =>
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
                    PublicationTable<WorldSpellRecipeGlyph>.Create(new[]
                    {
                        new WorldSpellRecipeGlyph(0, coreGlyphId),
                    })),
            }),
            SpellWorkbench = new WorldSpellWorkbench(0, 3, true, 4, 12, 3, 9),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "glyphs", WorldCategoryOutcome.Collected, 2, 0, string.Empty),
                new WorldCollectionCategoryStatus(
                    "spell-recipes", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
        };

    /// <summary>
    /// An augment is never a discovery-tree offer once discovered, and an unlocker never is at all,
    /// so the offer fact rides on the row's own discovery block rather than standing in for
    /// visibility.
    /// </summary>
    [Fact]
    public void The_offer_fact_rides_on_the_discovery_block_rather_than_on_visibility()
    {
        var offered = Guid.Parse("01273b00-0000-4000-8000-000000000003");
        var world = World(
            Glyph(offered, discoverable: true, learned: false, augmentsSpells: true, level: 0));
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
}
