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
    /// The gate that decides what may be spent on a spell, read off the field that decides it.
    /// </summary>
    /// <remarks>
    /// Read off <c>augmentsSpells</c>, the augment options dropped three augments a player already
    /// owns. The core half of this split is gone with the rule it served: the game's Loadout row
    /// reads no core glyphs at all, so what a recipe's core slot holds cannot refuse a load.
    /// </remarks>
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

        // The same glyph in a core slot is no refusal at all: the row the player presses passes
        // the recipe and reads only the augment stack, so nothing about the core reaches the load.
        var odd = GameMcpTestHarness.Context(Recipe(world, recipeId, QuietAugmentId));
        var stillOpen = GameMcpTestHarness.Detail(odd, recipeId)["row"]!["loadoutAdd"]!;

        Assert.True((bool)stillOpen["available"]!);
        Assert.Null(stillOpen["reasonCode"]);
        Assert.Equal(
            GameMcpTestHarness.Handle(QuietAugmentId),
            (string?)Assert.Single(stillOpen["augmentOptions"]!.Values<JObject>())!["glyph"]!["uuid"]);
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
            Views = PublicationTable<WorldView>.Create(new[]
            {
                new WorldView(KnownEntities.MagicSpellbookLoadout.Uuid, false, false, true),
            }),
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

    /// <summary>
    /// Each of the twenty-five unlockers carries one authored condition — Formation wants
    /// <c>LearnFormation</c>, Arcane wants <c>ResearchArcane</c> — and the world now publishes it, so
    /// the row names what to go and do instead of saying the game explains nothing.
    /// </summary>
    [Fact]
    public void An_unlocker_names_the_authored_upgrade_that_unlocks_it()
    {
        var context = GameMcpTestHarness.Context(Gated(
            World(Glyph(FormationId, discoverable: false, learned: false, augmentsSpells: false)),
            WorldRequirementConditionKind.Upgrade));

        var row = GameMcpTestHarness.Detail(context, FormationId)["row"]!;

        Assert.Equal("ERR_LOCKED", (string?)row["reasonCode"]);
        Assert.Equal(
            "Learn Formation unlocks this glyph, and it is not reached yet.",
            (string?)row["reason"]);
        Assert.Equal(
            GameMcpTestHarness.Handle(LearnFormationId),
            (string?)row["blockedBy"]!["uuid"]);
        Assert.Equal("Learn Formation", (string?)row["blockedBy"]!["name"]);
    }

    /// <summary>
    /// An augment's own gate is <c>discovered</c>, which the row already carries, so it says that and
    /// never borrows the sentence written for a lock nobody can explain.
    /// </summary>
    [Fact]
    public void An_undiscovered_augment_says_it_is_undiscovered()
    {
        var context = GameMcpTestHarness.Context(World(
            Glyph(AugmentId, discoverable: true, learned: false, augmentsSpells: true, level: 0)));

        var row = GameMcpTestHarness.Detail(context, AugmentId)["row"]!;

        Assert.Equal("ERR_LOCKED", (string?)row["reasonCode"]);
        Assert.Equal("This has not been discovered yet.", (string?)row["reason"]);
        Assert.Null(row["blockedBy"]);
    }

    /// <summary>
    /// The honest-unknown sentence is what is left when neither population's gate is readable: an
    /// unlocker whose authored condition this suite cannot model has no blocker to name, and
    /// inventing one would be worse than the silence.
    /// </summary>
    [Fact]
    public void An_unlocker_whose_condition_is_unmodelled_keeps_the_honest_unknown_sentence()
    {
        var context = GameMcpTestHarness.Context(Gated(
            World(Glyph(FormationId, discoverable: false, learned: false, augmentsSpells: false)),
            WorldRequirementConditionKind.Unknown));

        var row = GameMcpTestHarness.Detail(context, FormationId)["row"]!;

        Assert.Equal("ERR_LOCKED", (string?)row["reasonCode"]);
        Assert.Equal(
            "The game keeps this locked, and says nothing about what would unlock it.",
            (string?)row["reason"]);
        Assert.Null(row["blockedBy"]);
    }

    /// <summary>
    /// One unlocker opens recipes on three benches, so "which page" has more than one answer and the
    /// cell gives all of them in the pinned order. Refusing the way the upgrade column refuses a row
    /// two screens claim would throw away a fact the game plainly authored.
    /// </summary>
    [Fact]
    public void A_glyph_several_pages_carry_names_every_page_it_is_on()
    {
        var context = GameMcpTestHarness.Context(Listed(
            World(Glyph(UnlockerId, discoverable: false, learned: true, augmentsSpells: false)),
            (UnlockerId, KnownEntities.GlyphsEquipment.Uuid),
            (UnlockerId, KnownEntities.GlyphsCoreAlchemy.Uuid),
            (UnlockerId, KnownEntities.GlyphsCoreSpell.Uuid)));

        Assert.Equal(
            "Magic/Spellbook, Alchemy/Alchemy",
            (string?)GameMcpTestHarness.Detail(context, UnlockerId)["row"]!["screen"]);
    }

    /// <summary>
    /// An augment is met on one page, and it is the subtab <c>game_navigate</c> reaches rather than
    /// the grid two levels down that no cold navigation can select.
    /// </summary>
    [Fact]
    public void An_augment_names_the_subtab_the_navigation_tool_accepts()
    {
        var context = GameMcpTestHarness.Context(Listed(
            World(Glyph(AugmentId, discoverable: true, learned: true, augmentsSpells: true)),
            (AugmentId, KnownEntities.GlyphsAugmentSpell.Uuid)));

        Assert.Equal(
            "Magic/Augments",
            (string?)GameMcpTestHarness
                .Json(GameMcpWorldQuery.ListRows(context, "glyphs", 0, 50))["rows"]!
                .Values<JObject>()
                .Single()!["screen"]);
    }

    /// <summary>
    /// The ten forging glyphs are on <c>EquipmentGlyphs</c> and nothing else, and that list is named
    /// by no view anywhere in the object graph. The membership read perfectly; the game names no
    /// page for it, which is a different fact from an unreadable one and says so in its own word.
    /// </summary>
    [Fact]
    public void A_glyph_whose_only_list_no_view_names_says_the_game_names_no_page()
    {
        var context = GameMcpTestHarness.Context(Listed(
            World(Glyph(UnlockerId, discoverable: false, learned: true, augmentsSpells: false)),
            (UnlockerId, KnownEntities.GlyphsEquipment.Uuid)));

        Assert.Equal(
            "no_page",
            (string?)GameMcpTestHarness.Detail(context, UnlockerId)["row"]!["screen"]);
    }

    /// <summary>
    /// Membership is published whole or withheld whole, so a withheld publication reads as a suite
    /// gap rather than demoting all 47 rows to "the game names no page".
    /// </summary>
    [Fact]
    public void A_withheld_membership_publication_never_reads_as_a_page()
    {
        var context = GameMcpTestHarness.Context(World(
            Glyph(UnlockerId, discoverable: false, learned: true, augmentsSpells: false)));

        Assert.Equal(
            "unreadable",
            (string?)GameMcpTestHarness.Detail(context, UnlockerId)["row"]!["screen"]);
    }

    private static GameWorldState Listed(
        GameWorldState world,
        params (Guid GlyphId, Guid ListId)[] edges) =>
        world with
        {
            GlyphListMemberships = PublicationTable<WorldGlyphListMembership>.Create(
                edges
                    .OrderBy(edge => edge.GlyphId)
                    .ThenBy(edge => edge.ListId)
                    .Select(edge => new WorldGlyphListMembership(edge.GlyphId, edge.ListId))
                    .ToArray()),
        };

    /// <summary>The shipped Formation glyph and the shipped upgrade its container names.</summary>
    private static readonly Guid FormationId =
        Guid.Parse("02e0cda8-1c4b-4d93-b9d5-7d318cd352ad");

    private static readonly Guid LearnFormationId =
        Guid.Parse("3ee502aa-8d21-4b5d-9893-398eadbd9d38");

    /// <summary>
    /// One unlocker behind one unbought upgrade, which is the shape eighteen of the twenty-five
    /// have. <c>reqType</c> 0 is the game's "at least one level", and the upgrade sits at zero.
    /// </summary>
    private static GameWorldState Gated(
        GameWorldState world,
        WorldRequirementConditionKind kind)
    {
        var scaling = default(WorldRequirementScaling);
        var raw = new RawUpgradeSample(
            LearnFormationId,
            level: 0,
            maxLevel: 1,
            available: true,
            queuedLevels: 0,
            buildTime: BigDouble.Zero,
            developmentTime: 1d,
            cachedCostLevel: 0);
        return world with
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                new WorldUpgrade(
                    in raw,
                    isBounded: true,
                    isExhausted: false,
                    remainingLevels: 1,
                    committedLevel: 0,
                    isDeveloping: false,
                    developmentProgress: 0d),
            }),
            EntityRequirements = PublicationTable<WorldEntityRequirement>.Create(new[]
            {
                new WorldEntityRequirement(
                    FormationId,
                    WorldRequirementOwnerKind.Glyph,
                    ordinal: 0,
                    kind,
                    kind == WorldRequirementConditionKind.Upgrade
                        ? "UpgradeRequirement"
                        : "ListRequirement",
                    LearnFormationId,
                    reqType: 0,
                    baseValue: 0d,
                    in scaling,
                    in scaling),
            }),
        };
    }
}
