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
/// Every id, slot, kind and number here is the pinned build's own — <c>data/game-data.json</c>,
/// <c>objects.GlyphSO</c> and <c>objects.AttributeSO</c>. Quick is the round-observed case: its
/// tooltip prints <c>x1.15 Cost</c> and <c>x0.700 Cooldown</c>, which are its authored
/// <c>spellCost</c> and <c>spellCooldown</c> at MultiStacking with <c>adjust</c> 0.15 and -0.30.
/// </remarks>
[Collection(NativeRegistryCollection.Name)]
public sealed class GameMcpGlyphEffectTests : IDisposable
{
    private static readonly Guid Quick = Guid.Parse("0eff1e6f-8e7d-4bb9-a94b-0d58b12935ba");
    private static readonly Guid Fortunate = Guid.Parse("11111111-2222-4333-8444-555555555555");
    private static readonly Guid Cost = Guid.Parse("bb3cd428-054a-4e36-9f5e-7e83cbd9c5bb");
    private static readonly Guid Cooldown = Guid.Parse("c13b6286-6afa-42d1-b887-05b2f79ca973");

    public GameMcpGlyphEffectTests() => global::IdScriptableObject.RuntimeLookup.Clear();

    public void Dispose() => global::IdScriptableObject.RuntimeLookup.Clear();

    /// <summary>
    /// The round-observed case, whole. Two rows, each naming the slot it fills, the statistic the
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
    public void The_page_says_what_each_glyph_does_and_which_statistic_it_moves()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 5/5",
                "these 5 share: order=0",
                "[property | modifierType | amount | glyph | statistic]",
                "spellCost | stacking | 1.15 | Quick 0eff1e | Cost bb3cd4",
                "spellCooldown | stacking | 0.7 | Quick 0eff1e | Cooldown c13b62",
                "creationCostMod | stacking | 4 | Quick 0eff1e | -",
                "spellCriticalRating | diminishing | 0.17 | Fortunate 111111 | -",
                "spellDoubleCastRating | diminishing | 0.17 | Fortunate 111111 | -",
            }),
            Render(Json(GameMcpWorldQuery.ListRows(
                Context(World()), "glyph-effects", 0, 50, limitFromCaller: false))));
    }

    /// <summary>
    /// The node names its edge by carrying it. A reader deciding whether to socket a glyph asks one
    /// question, and the answer used to cost a hover; it now rides the answer they were already
    /// reading.
    /// </summary>
    [Fact]
    public void A_glyphs_own_answer_carries_what_it_does()
    {
        var row = Detail(Quick)["row"]!;
        var effects = Assert.IsType<JArray>(row["effects"]).Values<JObject>().ToArray();
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
        Assert.Equal("0.7", (string?)effects[1]!["amount"]);

        // The slot the game names no statistic for. The row is here and its edge is not, which is
        // the honest shape rather than a silently dropped factor.
        Assert.Equal("creationCostMod", (string?)effects[2]!["property"]);
        Assert.Null(effects[2]!["statistic"]);
    }

    /// <summary>
    /// A glyph that fills no slot carries no block at all, rather than an empty one. Absence is what
    /// absence means everywhere else on this surface.
    /// </summary>
    [Fact]
    public void A_glyph_with_no_authored_factor_names_no_edge()
    {
        var plain = Guid.Parse("99999999-8888-4777-8666-555555555555");
        Assert.Null(Detail(plain)["row"]!["effects"]);
    }

    /// <summary>
    /// The rows are enumerable with the primitive that already enumerates every other table, and the
    /// category answers for the type it reads. No new tool, and no filter this surface did not
    /// already have.
    /// </summary>
    [Fact]
    public void The_factor_table_is_an_ordinary_listable_category()
    {
        Assert.Contains("glyph-effects", GameMcpWorldQuery.RegisteredCategoryNames());
        Assert.Equal("GlyphSO", GameMcpEntityCapabilityMap.ExpectedNativeType("glyph-effects"));

        // A relation row answers to no single id, so the detail read refuses it by name and points
        // at the read that does answer — the same refusal every other composite makes.
        Assert.Equal(
            "unavailable (ERR_INPUT): Rows in glyph-effects are not addressed by one id; " +
            "read them with world_list.",
            Render(Json(GameMcpWorldQuery.GetRows(
                Context(World()), "glyph-effects", new[] { Quick.ToString("D") }))));
    }

    private static string Render(JObject page) => GameMcpTextPage.Render(page).TrimEnd('\n');

    private static JObject Detail(Guid uuid) =>
        Assert.Single(
            Json(GameMcpWorldQuery.GetRows(
                Context(World()), "glyphs", new[] { uuid.ToString("D") }))
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
            Glyphs = PublicationTable<WorldGlyph>.Create(SortedGlyphs(
                Glyph(Quick),
                Glyph(Fortunate),
                Glyph(Guid.Parse("99999999-8888-4777-8666-555555555555")))),
            GlyphEffects = PublicationTable<WorldGlyphFactor>.Create(new[]
            {
                // Quick, verbatim: 0.15 and -0.30 authored, 1.15 and 0.7 real.
                Factor(Quick, "spellCost", Cost, GameValueModifierType.MultiStacking, 1.15d),
                Factor(Quick, "spellCooldown", Cooldown, GameValueModifierType.MultiStacking, 0.7d),
                Factor(Quick, "creationCostMod", Guid.Empty,
                    GameValueModifierType.MultiStacking, 4d),

                // Fortunate fills two of the four slots the game points at a player variable rather
                // than at a statistic, so neither carries an edge.
                Factor(Fortunate, "spellCriticalRating", Guid.Empty,
                    GameValueModifierType.MultiDiminishing, 0.17d),
                Factor(Fortunate, "spellDoubleCastRating", Guid.Empty,
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
        GameValueModifierType type,
        double amount) =>
        new(glyphId, property, statisticId, (int)type, new BigDouble(amount), 0);

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
            new EntityIdentityName(
                Guid.Parse("99999999-8888-4777-8666-555555555555"), "GlyphSO", "Plain", "Plain"),
            new EntityIdentityName(Cost, "AttributeSO", "Cost", "Cost"),
            new EntityIdentityName(Cooldown, "AttributeSO", "Cooldown", "Cooldown"),
        }));

    private static EntityIdentityName[] Sorted(EntityIdentityName[] rows)
    {
        Array.Sort(rows, static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return rows;
    }
}
