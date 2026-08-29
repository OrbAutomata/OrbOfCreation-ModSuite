using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// What a glyph does, on the wire. The gap this closes was observed in a live round: a glyph's
/// published row carried its price and its levels and said nothing whatever about its effect, so the
/// only way to learn that Quick trades 15% more cost for 30% less cooldown was to hover it.
/// </summary>
/// <remarks>
/// <para>
/// Every slot, kind and number here is the pinned build's own — <c>data/game-data.json</c>,
/// <c>objects.GlyphSO</c> and <c>objects.AttributeSO</c>. Quick is the round-observed case: its
/// tooltip prints <c>x1.15 Cost</c> and <c>x0.700 Cooldown</c>, which are its authored
/// <c>spellCost</c> and <c>spellCooldown</c> at MultiStacking with <c>adjust</c> 0.15 and -0.30.
/// </para>
/// <para>
/// The factors used to be a listable table of their own beside this. They are not any more: one
/// glyph's factors are one glyph's answer, and the cross-glyph question the table existed for
/// ("which glyph touches Cooldown") is a <c>world_search</c> query, which is the question a reader
/// actually asks and the table never answered without being paged by hand. Both facts the table
/// pinned are still pinned — here, on the surfaces that survived it.
/// </para>
/// </remarks>
[Collection(NativeRegistryCollection.Name)]
public sealed class GameMcpGlyphEffectTests : IDisposable
{
    private static readonly Guid Quick = Guid.Parse("0eff1e6f-8e7d-4bb9-a94b-0d58b12935ba");
    private static readonly Guid Fortunate = Guid.Parse("11111111-2222-4333-8444-555555555555");
    private static readonly Guid Cost = Guid.Parse("bb3cd428-054a-4e36-9f5e-7e83cbd9c5bb");
    private static readonly Guid Cooldown = Guid.Parse("c13b6286-6afa-42d1-b887-05b2f79ca973");
    private static readonly Guid CritRating = Guid.Parse("69e869dc-628e-4968-b739-d516cc4d58c5");
    private static readonly Guid EchoRating = Guid.Parse("3cd7a634-f132-4c4e-bbfc-2525f6c6ac8e");
    private static readonly Guid Plain = Guid.Parse("99999999-8888-4777-8666-555555555555");

    public GameMcpGlyphEffectTests() => global::IdScriptableObject.RuntimeLookup.Clear();

    public void Dispose() => global::IdScriptableObject.RuntimeLookup.Clear();

    /// <summary>
    /// The round-observed case, whole. Three rows, each naming the slot it fills, the statistic the
    /// game prints it under, the kind of arithmetic, the magnitude and the merge order.
    /// </summary>
    /// <remarks>
    /// The magnitude published is the modifier's <c>adjustReal</c>, which is what the screen prints:
    /// the game's <c>ConvertToReal</c> adds one for the multiplicative kinds, so the authored 0.15
    /// and -0.30 are the 1.15 and 0.7 here and the <c>x1.15</c> and <c>x0.700</c> on the tooltip.
    /// The kind is never folded into the number — two glyphs on one spell combine by kind, and a
    /// pre-multiplied number would say the wrong thing about every pairing.
    /// </remarks>
    [Fact]
    public void A_glyphs_own_answer_carries_what_it_does()
    {
        var effects = Effects(Quick);
        Assert.Equal(3, effects.Length);

        Assert.Equal("spellCost", (string?)effects[0]!["property"]);
        Assert.Equal("bb3cd4", (string?)effects[0]!["statistic"]!["uuid"]);
        Assert.Equal("Cost", (string?)effects[0]!["statistic"]!["name"]);
        Assert.Equal("stacking", (string?)effects[0]!["modifierType"]);
        Assert.Equal("1.15", (string?)effects[0]!["amount"]);
        Assert.Equal(0, (int?)effects[0]!["order"]);

        Assert.Equal("spellCooldown", (string?)effects[1]!["property"]);
        Assert.Equal("c13b62", (string?)effects[1]!["statistic"]!["uuid"]);
        Assert.Equal("Cooldown", (string?)effects[1]!["statistic"]!["name"]);
        Assert.Equal("stacking", (string?)effects[1]!["modifierType"]);
        Assert.Equal("0.7", (string?)effects[1]!["amount"]);
        Assert.Equal(0, (int?)effects[1]!["order"]);

        // The one slot the game points at neither a statistic nor a player variable: it discounts a
        // cost list, which is not an entity this surface publishes. The row is here and both edges
        // are not, which is the honest shape rather than a silently dropped factor or an invented
        // handle.
        Assert.Equal("creationCostMod", (string?)effects[2]!["property"]);
        Assert.Equal("stacking", (string?)effects[2]!["modifierType"]);
        Assert.Equal("4", (string?)effects[2]!["amount"]);
        Assert.Null(effects[2]!["statistic"]);
        Assert.Null(effects[2]!["variable"]);
    }

    /// <summary>
    /// The four slots that print against a player variable rather than a statistic name it, so every
    /// factor the game gives a target now carries the edge to that target.
    /// </summary>
    /// <remarks>
    /// The variable a slot prints against is a serialized field on the <c>Player</c> singleton, read
    /// through its own accessor — so the edge follows whatever the scene assigns rather than a name
    /// matched here. The names are the ones the pinned build's identity catalog carries for those
    /// two variables.
    /// </remarks>
    [Fact]
    public void The_slots_that_print_against_a_player_variable_carry_that_edge()
    {
        var effects = Effects(Fortunate);
        Assert.Equal(2, effects.Length);

        Assert.Equal("spellCriticalRating", (string?)effects[0]!["property"]);
        Assert.Equal("69e869", (string?)effects[0]!["variable"]!["uuid"]);
        Assert.Equal("Spell Crit Rating", (string?)effects[0]!["variable"]!["name"]);
        Assert.Equal("diminishing", (string?)effects[0]!["modifierType"]);
        Assert.Equal("0.17", (string?)effects[0]!["amount"]);
        Assert.Null(effects[0]!["statistic"]);

        Assert.Equal("spellDoubleCastRating", (string?)effects[1]!["property"]);
        Assert.Equal("3cd7a6", (string?)effects[1]!["variable"]!["uuid"]);
        Assert.Equal("Echo Cast Rating", (string?)effects[1]!["variable"]!["name"]);
        Assert.Equal("diminishing", (string?)effects[1]!["modifierType"]);
        Assert.Equal("0.17", (string?)effects[1]!["amount"]);
        Assert.Null(effects[1]!["statistic"]);
    }

    /// <summary>
    /// A glyph that fills no slot carries no block at all, rather than an empty one. Absence is what
    /// absence means everywhere else on this surface.
    /// </summary>
    [Fact]
    public void A_glyph_with_no_authored_factor_names_no_edge()
    {
        Assert.Null(Detail(Plain)["row"]!["effects"]);
    }

    /// <summary>
    /// "Which glyph touches Cooldown" is a search, and the answer says that is why it is here. This
    /// is the question the retired table was paged by hand to answer.
    /// </summary>
    [Fact]
    public void The_cross_glyph_question_is_a_search_that_says_why_it_matched()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 1/1",
                "[id | name | category | keywords | matchedOn]",
                "0eff1e | Quick | augment-glyphs | - | effects",
            }),
            Render(Search("Cooldown")));

        // The name of the variable behind a slot is an effect word too, so the reader who knows
        // "Echo" and not "Fortunate" finds it.
        Assert.Equal(
            new[] { "111111" },
            Rows(Search("Echo")).Select(row => (string?)row["uuid"]).ToArray());
    }

    /// <summary>
    /// The standalone table is gone: not listable, not a category any read answers for, and not a
    /// name the detail read has a refusal for.
    /// </summary>
    [Fact]
    public void The_factor_table_is_retired()
    {
        Assert.DoesNotContain("glyph-effects", GameMcpWorldQuery.RegisteredCategoryNames());
        Assert.Equal(
            "MCP world category 'glyph-effects' has no entity-capability descriptor",
            Assert.Throws<InvalidOperationException>(
                () => GameMcpEntityCapabilityMap.ExpectedNativeType("glyph-effects")).Message);
        Assert.Equal(
            "unavailable (ERR_INPUT): unknown category 'glyph-effects'; call world_categories " +
            "for the exact discoverable names",
            Render(Json(GameMcpWorldQuery.ListRows(
                Context(World()), "glyph-effects", 0, 50, limitFromCaller: false))));
    }

    private static JObject[] Effects(Guid glyphId) =>
        Assert.IsType<JArray>(Detail(glyphId)["row"]!["effects"])
            .Values<JObject>().Select(row => row!).ToArray();

    private static string Render(JObject page) => GameMcpTextPage.Render(page).TrimEnd('\n');

    private static JObject Search(string query) =>
        Json(GameMcpWorldQuery.Search(Context(World()), query, 0, 50, limitFromCaller: false));

    private static JObject[] Rows(JObject page) =>
        page["rows"]!.Values<JObject>().Select(row => row!).ToArray();

    private static JObject Detail(Guid uuid) =>
        Assert.Single(
            Json(GameMcpWorldQuery.GetRows(
                Context(World()), "augment-glyphs", new[] { uuid.ToString("D") }))
            ["results"]!.Values<JObject>())!;

    private static JObject Json(GameMcpObjectBuilder value) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value.Freeze(), Catalog));

    private static GameMcpFrameContext Context(GameWorldState world)
    {
        var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(982));
        return GameMcpTestHarness.Context(publisher.ReadLatest());
    }

    private static GameWorldState World() =>
        new()
        {
            EntityIdentities = Catalog,
            AugmentGlyphs = PublicationTable<WorldGlyph>.Create(SortedGlyphs(
                Glyph(Quick),
                Glyph(Fortunate),
                Glyph(Plain))),
            GlyphEffects = PublicationTable<WorldGlyphFactor>.Create(new[]
            {
                // Quick, verbatim: 0.15 and -0.30 authored, 1.15 and 0.7 real.
                Factor(Quick, "spellCost", Cost, Guid.Empty,
                    GameValueModifierType.MultiStacking, 1.15d),
                Factor(Quick, "spellCooldown", Cooldown, Guid.Empty,
                    GameValueModifierType.MultiStacking, 0.7d),
                Factor(Quick, "creationCostMod", Guid.Empty, Guid.Empty,
                    GameValueModifierType.MultiStacking, 4d),

                // Fortunate fills two of the four slots the game points at a player variable rather
                // than at a statistic, so each carries the variable edge instead.
                Factor(Fortunate, "spellCriticalRating", Guid.Empty, CritRating,
                    GameValueModifierType.MultiDiminishing, 0.17d),
                Factor(Fortunate, "spellDoubleCastRating", Guid.Empty, EchoRating,
                    GameValueModifierType.MultiDiminishing, 0.17d),
            }),
            CollectionCategories =
                PublicationTable<WorldCollectionCategoryStatus>.Create(CleanReports()),
            CollectedAtEpoch = 82,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

    private static WorldGlyphFactor Factor(
        Guid glyphId,
        string property,
        Guid statisticId,
        Guid variableId,
        GameValueModifierType type,
        double amount) =>
        new(glyphId, property, statisticId, variableId, (int)type, new BigDouble(amount), 0);

    private static WorldGlyph Glyph(Guid glyphId) =>
        new(glyphId, 1, 0, 0, true, true, true, true, false, false, 0,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero);

    private static WorldGlyph[] SortedGlyphs(params WorldGlyph[] rows)
    {
        Array.Sort(rows, static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return rows;
    }

    private static WorldCollectionCategoryStatus[] CleanReports() =>
        GameMcpWorldQuery.RegisteredCategoryNames().Concat(new[]
            {
                "harvest-actions", "harvest-elements", "harvest-types", "harvest-action-types",
                "plot-node-actions", "concept-instances", "loadouts", "consumable-families",
                "consumable-inventory", "crafting-recipe-state", "crafting-decisions",
                "ordinary-alchemy-loadout", "plot-authoring", "plot-actions", "action-queues",
                "crafting-stations", "harvest-lifecycle", "spell-slots", "targeting",
                "attribute-group-members",
            })
            .Distinct(StringComparer.Ordinal)
            .Select(name => new WorldCollectionCategoryStatus(
                name, WorldCategoryOutcome.Collected, 0, 0, string.Empty))
            .ToArray();

    private static readonly EntityIdentityCatalogSnapshot Catalog =
        EntityIdentityCatalogSnapshot.Bound(1, Sorted(new[]
        {
            new EntityIdentityName(Quick, "GlyphSO", "Quick", "Quick"),
            new EntityIdentityName(Fortunate, "GlyphSO", "Fortunate", "Fortunate"),
            new EntityIdentityName(Plain, "GlyphSO", "Plain", "Plain"),
            new EntityIdentityName(Cost, "AttributeSO", "Cost", "Cost"),
            new EntityIdentityName(Cooldown, "AttributeSO", "Cooldown", "Cooldown"),
            new EntityIdentityName(
                CritRating, "DoubleVariable", "Spell Crit Rating", "SpellCriticalRating"),
            new EntityIdentityName(
                EchoRating, "DoubleVariable", "Echo Cast Rating", "SpellEchoRating"),
        }));

    private static EntityIdentityName[] Sorted(EntityIdentityName[] rows)
    {
        Array.Sort(rows, static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return rows;
    }
}
