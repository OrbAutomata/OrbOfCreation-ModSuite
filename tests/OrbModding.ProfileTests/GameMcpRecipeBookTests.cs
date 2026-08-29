using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// The thirty-four Recipe Books, which carry one fact each: whether the player owns it. A live
/// round spent a currency levelling twenty-five of these because the wire offered a level the game
/// draws no button for, so these pin the shape that offers none.
/// </summary>
public sealed class GameMcpRecipeBookTests
{
    /// <summary>Expansion — a book, a spell type twin, and four discovery pools.</summary>
    private static readonly Guid ExpansionBookId =
        Guid.Parse("98b5a31f-3af1-4bdb-b957-b71ad782d474");

    private static readonly Guid ExpansionSpellTypeId =
        Guid.Parse("ad896268-9998-4029-8ad5-26489a01040c");

    /// <summary>Insight — a book with no twin, on the Spell Discoveries pool alone.</summary>
    private static readonly Guid InsightBookId =
        Guid.Parse("a9a4dd71-53cf-405c-b9c3-6d252308fe9b");

    private static readonly Guid InsightGlyphId =
        Guid.Parse("0f38b02c-b81a-4fcd-9e07-73e09bd38dee");

    private static readonly Guid SpellTreeId = Guid.Parse("5ba0b305-21bd-4b43-af88-cc763ac04df8");
    private static readonly Guid AlchemyTreeId = Guid.Parse("c90e1ad2-2117-4d5f-b52f-a4abebbc79a4");
    private static readonly Guid LearnExpandId = Guid.Parse("2f34e7b8-cc57-4516-8dff-681abc3f1a08");

    /// <summary>The Expansion spell type, present only so the book's twin has something to name.</summary>
    private static WorldSpellType SpellType() => new(
        ExpansionSpellTypeId,
        typeLevel: 0,
        typeXp: BigDouble.Zero,
        typeXpRequiredBase: 1d,
        augmentPowerMod: 1d,
        hasNoLevels: false,
        isElemental: true,
        isLoadoutUnique: false,
        hasNotTypeSignificance: false,
        isVisible: true,
        debugMode: false,
        typeXpMod: BigDouble.One,
        power: BigDouble.One,
        cooldownSpeed: BigDouble.One,
        cooldownTime: BigDouble.One,
        costMod: BigDouble.One,
        drainCostMod: BigDouble.One,
        durationMod: BigDouble.One,
        elementalResonance: BigDouble.One,
        augmentResonance: BigDouble.One,
        maxStacksMod: BigDouble.One,
        scalingMod: BigDouble.One,
        usageCostReduction: BigDouble.Zero,
        bonusCritRate: BigDouble.Zero,
        critEffectMod: BigDouble.One,
        critDurationMod: BigDouble.One,
        bonusDoubleCastRate: BigDouble.Zero,
        doubleCastEffectMod: BigDouble.One,
        chargeTimeMod: BigDouble.One,
        chargeEffectMod: BigDouble.One,
        chargeSpecialMod: BigDouble.One,
        bonusFlashRate: BigDouble.Zero,
        flashEffectMod: BigDouble.One);

    private static GameWorldState World(params WorldRecipeBook[] books) => new()
    {
        RecipeBooks = PublicationTable<WorldRecipeBook>.Create(
            books.OrderBy(book => book.EntityId).ToArray()),
        SpellTypes = PublicationTable<WorldSpellType>.Create(new[] { SpellType() }),
        DiscoveryTreeBooks = PublicationTable<WorldDiscoveryTreeBook>.Create(new[]
        {
            new WorldDiscoveryTreeBook(ExpansionBookId, AlchemyTreeId),
            new WorldDiscoveryTreeBook(ExpansionBookId, SpellTreeId),
            new WorldDiscoveryTreeBook(InsightBookId, SpellTreeId),
        }.OrderBy(edge => edge.RecipeBookId).ThenBy(edge => edge.TreeId).ToArray()),
        RecipeBookGlyphs = PublicationTable<WorldRecipeBookGlyph>.Create(new[]
        {
            new WorldRecipeBookGlyph(InsightGlyphId, InsightBookId),
        }),
        CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
        {
            new WorldCollectionCategoryStatus(
                "recipe books", WorldCategoryOutcome.Collected, books.Length, 0, string.Empty),
        }),
        CollectedAtEpoch = 71,
        CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
    };

    /// <summary>
    /// <c>RecipeBookSO</c> carries exactly one instance field, <c>prerequisites</c>, so the list is
    /// one column. The retired glyph shape put a level, a cost curve and a <c>purchase</c> offer on
    /// these rows; none of the three named anything the game draws.
    /// </summary>
    [Fact]
    public void The_list_row_is_the_one_fact_a_book_holds()
    {
        var context = GameMcpTestHarness.Context(
            World(new WorldRecipeBook(InsightBookId, true)));

        var row = GameMcpTestHarness
            .Json(GameMcpWorldQuery.ListRows(context, "recipe-books", 0, 50))["rows"]!
            .Values<JObject>()
            .Single()!;

        Assert.Equal(
            new[] { "uuid", "name", "owned" },
            row.Properties().Select(property => property.Name).ToArray());
        Assert.Equal("Insight", (string?)row["name"]);
        Assert.True((bool)row["owned"]!);
    }

    /// <summary>
    /// The page adds only what the list leaves out: the pools the book widens, and — while it is
    /// unowned — the one purchase that flips the answer. No level and no <c>purchase</c> anywhere.
    /// </summary>
    [Fact]
    public void An_owned_book_names_the_pools_it_widens_and_offers_no_level()
    {
        var context = GameMcpTestHarness.Context(
            World(new WorldRecipeBook(ExpansionBookId, true)));

        var detail = GameMcpTestHarness.Detail(context, ExpansionBookId);
        var row = detail["row"]!;

        Assert.Equal("recipe-books", (string?)detail["category"]);
        Assert.True((bool)row["owned"]!);
        Assert.Null(row["ownedBy"]);
        Assert.Null(row["purchase"]);
        Assert.Null(row["level"]);
        Assert.Null(row["paidLevel"]);
        Assert.Null(row["usableCount"]);
        Assert.Equal(
            new[] { "Alchemy Discoveries", "Spell Discoveries" },
            row["widens"]!.Values<JObject>().Select(tree => (string?)tree!["name"]).OrderBy(
                name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// What makes an unowned book owned, read rather than assumed. Twenty-four of the thirty-four
    /// are held by a Learn upgrade, nine by a research and one — Storm — by a prerequisite link, so
    /// pinning "the Learn upgrade" would have been wrong on ten rows.
    /// </summary>
    [Fact]
    public void An_unowned_book_names_the_purchase_that_owns_it()
    {
        var noScaling = default(WorldRequirementScaling);
        var raw = new RawUpgradeSample(
            LearnExpandId,
            level: 0,
            maxLevel: 1,
            available: true,
            queuedLevels: 0,
            buildTime: BigDouble.Zero,
            developmentTime: 1d,
            cachedCostLevel: 0);
        var world = World(new WorldRecipeBook(ExpansionBookId, false)) with
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
                    ExpansionBookId,
                    WorldRequirementOwnerKind.RecipeBook,
                    ordinal: 0,
                    WorldRequirementConditionKind.Upgrade,
                    "UpgradeRequirement",
                    LearnExpandId,
                    reqType: 0,
                    baseValue: 0d,
                    in noScaling,
                    in noScaling),
            }),
        };

        var row = GameMcpTestHarness.Detail(
            GameMcpTestHarness.Context(world), ExpansionBookId)["row"]!;

        Assert.False((bool)row["owned"]!);
        Assert.Equal(
            GameMcpTestHarness.Handle(LearnExpandId), (string?)row["ownedBy"]!["uuid"]);
        Assert.Equal("Learn Expansion", (string?)row["ownedBy"]!["name"]);
    }

    /// <summary>
    /// Six books share a display name with a spell type, and a single spell-recipe response prints
    /// both — its books under <c>composedOf</c> and its types under <c>belongsTo</c>. The row names
    /// the twin so a caller reading "Expansion" twice knows they are two entities.
    /// </summary>
    [Fact]
    public void A_book_that_shares_its_name_with_a_spell_type_names_the_twin()
    {
        var context = GameMcpTestHarness.Context(World(
            new WorldRecipeBook(ExpansionBookId, true),
            new WorldRecipeBook(InsightBookId, true)));

        var twinned = GameMcpTestHarness.Detail(context, ExpansionBookId)["row"]!;
        Assert.Equal(
            GameMcpTestHarness.Handle(ExpansionSpellTypeId),
            (string?)twinned["nameSharedWith"]!["uuid"]);
        Assert.Equal("spell-types", (string?)twinned["nameSharedWith"]!["category"]);

        Assert.Null(GameMcpTestHarness.Detail(context, InsightBookId)["row"]!["nameSharedWith"]);
    }

    /// <summary>
    /// An id a caller still holds from the older wire. It is a loaded <c>GlyphSO</c>, so the
    /// native-type arm would have sent them to <c>augment-glyphs</c>, where its row will never be —
    /// which is the whole point of retiring it. The signpost names the book that answers instead.
    /// </summary>
    [Fact]
    public void A_retired_unlocker_id_gets_the_machinery_signpost_not_a_bare_not_found()
    {
        var context = GameMcpTestHarness.Context(World(new WorldRecipeBook(InsightBookId, true)));

        var block = GameMcpTestHarness.Detail(context, InsightGlyphId);

        Assert.Equal("unavailable", (string?)block["status"]);
        Assert.Equal("ERR_NOT_FOUND", (string?)block["reasonCode"]);
        Assert.Equal(
            "what answers to this id is internal machinery the world does not publish; the " +
            "Recipe Book it is the internal half of is Insight (a9a4dd)",
            (string?)block["reason"]);
        Assert.Equal("recipe-books", (string?)block["readWith"]!["category"]);
        Assert.Equal(GameMcpTestHarness.Handle(InsightBookId), (string?)block["readWith"]!["uuid"]);
    }

    /// <summary>
    /// Round 13's illegal move, refused: <c>game_level_up</c> on one of the twenty-five. The wire
    /// answered "yes, and it is free" on entities the game gives no button for. The refusal says
    /// what the id is and names the purchase that does what the caller wanted.
    /// </summary>
    [Fact]
    public void Levelling_a_retired_unlocker_id_refuses_in_player_words()
    {
        var world = World(new WorldRecipeBook(InsightBookId, false)) with
        {
            EntityIdentities = GameMcpTestHarness.EntityCatalog,
        };

        Assert.False(GameMcpEntityCapabilityMap.TryResolveGenericLevelType(
            world, InsightGlyphId, out _, out var reason));
        Assert.Equal(
            "Insight is a Recipe Book — there is nothing to level. It is owned by buying its one " +
            "prerequisite.",
            reason);
    }

    /// <summary>
    /// The name the wire used to answer to. `glyphs` was two player concepts under one native
    /// class, so the refusal sends the caller to both rather than to the category catalog.
    /// </summary>
    [Fact]
    public void The_retired_glyphs_category_name_refuses_by_naming_both_new_homes()
    {
        var context = GameMcpTestHarness.Context(
            World(new WorldRecipeBook(InsightBookId, true)));

        var page = GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(context, "glyphs", 0, 50));

        Assert.Equal("ERR_INPUT", (string?)page["reasonCode"]);
        Assert.Equal(
            "unknown category 'glyphs'; it named two things and is now two categories. " +
            "'augment-glyphs' is the twenty-two a caster sockets into a spell, whose level buys " +
            "slots. 'recipe-books' is the thirty-four tiles that widen a discovery pool",
            (string?)page["reason"]);
    }
}
