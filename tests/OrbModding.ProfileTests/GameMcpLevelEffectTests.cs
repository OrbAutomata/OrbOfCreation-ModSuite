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
/// What one more level of a thing buys, on that thing's own answer. Six classes author per-level
/// modifier tuples and until now the wire carried none of them: a reader deciding whether to buy
/// another Raise Druidry Lv could see its price and its level and had to hover the game's own
/// tooltip to learn what the level was for.
/// </summary>
/// <remarks>
/// <para>
/// The worked case is the pinned build's own. <c>RaiseMaxDruidryLevel</c> authors three tuples in
/// <c>UpgradeSO.permanentEffects</c> — one <c>TupleMod&lt;NumberVariable&gt;</c> and two
/// <c>UpgradeableObject.UpgradeEffectModifier</c>s — and the game's tooltip prints them as
/// <c>+1 Max Druidry Lv</c>, <c>x1.02 All Plot Yield</c> and <c>x1.75 All Plot Recovery Size</c>.
/// The published block is those three lines in the one modifier vocabulary this surface already
/// speaks.
/// </para>
/// <para>
/// The three record classes name their target three ways and the block absorbs all three: an
/// upgradeable-object tuple's key is its <c>propertyType</c> string, a resource tuple's key is its
/// <c>ModifiableType</c> member name, and a number-variable tuple has no key at all because the
/// variable is the whole of what moves — which is why the game prints <c>+1 Max Druidry Lv</c> with
/// no property word in front of the name.
/// </para>
/// </remarks>
[Collection(NativeRegistryCollection.Name)]
public sealed class GameMcpLevelEffectTests : IDisposable
{
    private static readonly Guid Druidry = Guid.Parse("1c6da5b3-041b-40a8-a45a-feb174cde507");
    private static readonly Guid MaxDruidryLevel = Guid.Parse("a2c95017-f333-4ff9-9c57-6c6093f4f988");
    private static readonly Guid AllPlot = Guid.Parse("33d4f5d6-c128-4b45-8a02-2ec94af23f8a");

    private static readonly Guid BloomingGlyph = Guid.Parse("0813deee-cb53-4cf0-8f45-6b48b7c43595");
    private static readonly Guid StockRestore = Guid.Parse("8d8a1b5b-ba8e-419d-81f5-38ca7e61e137");

    private static readonly Guid BloomingType = Guid.Parse("1e1d96f4-9af2-4c92-ba9c-b0b629d337ad");
    private static readonly Guid PlotCapacity = Guid.Parse("46eea2ec-c478-4ac3-960d-09ec96138d0b");

    private static readonly Guid Amulet = Guid.Parse("1a1d55b9-f079-4e79-8ee6-c1b9c7705e12");
    private static readonly Guid AuraStructures = Guid.Parse("efcd91f5-0016-4ee6-8034-08ee86d980dd");

    private static readonly Guid Arcane = Guid.Parse("bcfc9f02-a723-4d64-9f10-8a9db98c6af0");
    private static readonly Guid ArcanistStructures = Guid.Parse("e8866561-6ccc-42eb-b62b-1f56e7beafcb");

    private static readonly Guid AbilityPersist = Guid.Parse("a98e5e7d-3bf5-46cf-a6df-73747ed57797");

    public GameMcpLevelEffectTests() => global::IdScriptableObject.RuntimeLookup.Clear();

    public void Dispose() => global::IdScriptableObject.RuntimeLookup.Clear();

    /// <summary>
    /// The ruling's worked case, whole: the three lines the game's own tooltip prints, on the read a
    /// reader was already making.
    /// </summary>
    /// <remarks>
    /// The magnitude is the modifier's <c>adjustReal</c> and never a pre-multiplied product: the
    /// authored 0.02 and 0.75 are multiplicative, so <c>ConvertToReal</c> makes them the 1.02 and
    /// 1.75 the screen prints, while the additive 1 stays 1. The kind rides beside the number
    /// instead of being folded into it, because two modifiers on one property combine by kind.
    /// </remarks>
    [Fact]
    public void What_a_level_buys_rides_the_owners_own_answer()
    {
        var effects = Effects("upgrades", Druidry);
        Assert.Equal(3, effects.Length);

        // No property word: the variable is the whole of what moves, which is why the game prints
        // "+1 Max Druidry Lv" rather than "+1 something of Max Druidry Lv".
        Assert.Null(effects[0]["property"]);
        Assert.Equal("a2c950", (string?)effects[0]["modifies"]!["uuid"]);
        Assert.Equal("Max Druidry Lv", (string?)effects[0]["modifies"]!["name"]);
        Assert.Equal("raw", (string?)effects[0]["modifierType"]);
        Assert.Equal("1", (string?)effects[0]["amount"]);
        Assert.Equal(0, (int?)effects[0]["order"]);

        Assert.Equal("Yield", (string?)effects[1]["property"]);
        Assert.Equal("33d4f5", (string?)effects[1]["modifies"]!["uuid"]);
        Assert.Equal("All Plot", (string?)effects[1]["modifies"]!["name"]);
        Assert.Equal("stacking", (string?)effects[1]["modifierType"]);
        Assert.Equal("1.02", (string?)effects[1]["amount"]);
        Assert.Equal(0, (int?)effects[1]["order"]);

        Assert.Equal("RecoverySizeMod", (string?)effects[2]["property"]);
        Assert.Equal("33d4f5", (string?)effects[2]["modifies"]!["uuid"]);
        Assert.Equal("All Plot", (string?)effects[2]["modifies"]!["name"]);
        Assert.Equal("stacking", (string?)effects[2]["modifierType"]);
        Assert.Equal("1.75", (string?)effects[2]["amount"]);
        Assert.Equal(0, (int?)effects[2]["order"]);
    }

    /// <summary>
    /// The other four holders that author a tuple on this build read the same block, so a reader
    /// learns one shape and it answers on every levelable thing.
    /// </summary>
    /// <remarks>
    /// Three vocabularies reach it. The glyph and the equipment type author
    /// <c>UpgradeEffectModifier</c>s, whose key is the <c>propertyType</c> string; the resource type
    /// authors a <c>ResourceSO.PersistentEffect</c>, whose key is its <c>ModifiableType</c> member
    /// name; the spell type authors an <c>UpgradeEffectModifier</c> again. One column carries all
    /// three because they are the same sentence about the same arithmetic.
    /// </remarks>
    [Fact]
    public void The_same_block_answers_on_every_holder_that_authors_one()
    {
        var glyph = Assert.Single(Effects("augment-glyphs", BloomingGlyph));
        Assert.Equal("Value", (string?)glyph["property"]);
        Assert.Equal("diminishing", (string?)glyph["modifierType"]);
        Assert.Equal("1.15", (string?)glyph["amount"]);

        var resourceType = Assert.Single(Effects("resource-types", BloomingType));
        Assert.Equal("MaxQuantity", (string?)resourceType["property"]);
        Assert.Equal("46eea2", (string?)resourceType["modifies"]!["uuid"]);
        Assert.Equal("Plot Capacity", (string?)resourceType["modifies"]!["name"]);
        Assert.Equal("raw", (string?)resourceType["modifierType"]);
        Assert.Equal("5", (string?)resourceType["amount"]);

        var equipmentType = Assert.Single(Effects("equipment-types", Amulet));
        Assert.Equal("EffectLevel", (string?)equipmentType["property"]);
        Assert.Equal("efcd91", (string?)equipmentType["modifies"]!["uuid"]);
        Assert.Equal("raw", (string?)equipmentType["modifierType"]);
        Assert.Equal("5", (string?)equipmentType["amount"]);

        var spellType = Assert.Single(Effects("spell-types", Arcane));
        Assert.Equal("EffectLevel", (string?)spellType["property"]);
        Assert.Equal("e88665", (string?)spellType["modifies"]!["uuid"]);
        Assert.Equal("raw", (string?)spellType["modifierType"]);
        Assert.Equal("2", (string?)spellType["amount"]);

        // The sixth holder is walked and authors no modifier on this build: every
        // TimeRuneSO.onLevelEffects block carries advancement grants rather than tuples, so the
        // owner's answer carries no block at all rather than an empty one.
        Assert.Null(Row("time-runes", AbilityPersist)["levelEffects"]);
    }

    /// <summary>
    /// A target the identity catalog cannot name keeps its handle and says so, rather than being
    /// dropped or dressed in a name nothing published. Five of the six holders point at scene
    /// <c>UpgradeableObject</c>s, so this is the ordinary case rather than a corner of it.
    /// </summary>
    [Fact]
    public void A_target_nothing_published_names_keeps_its_key_and_no_named_edge()
    {
        var glyph = Assert.Single(Effects("augment-glyphs", BloomingGlyph));

        // The key survives: the property the tuple moves is the whole of what a reader can act on
        // when the target has no published row.
        Assert.Equal("Value", (string?)glyph["property"]);
        Assert.Equal("8d8a1b", (string?)glyph["modifies"]!["uuid"]);
        Assert.Equal("(unnamed 8d8a1b)", (string?)glyph["modifies"]!["name"]);
    }

    /// <summary>
    /// "What raises my Druidry cap" is answerable without knowing the name of the thing that does
    /// it, and the row says the reason it answered.
    /// </summary>
    /// <remarks>
    /// Two of the three hits are the words in the query, and the third is the effect: the upgrade is
    /// called Raise Druidry Lv, the variable it moves is called Max Druidry Lv, and the resource
    /// type has neither word anywhere in its identity. The effect band sorts last on purpose, so
    /// adding it moved no hit this surface already returned.
    /// </remarks>
    [Fact]
    public void Search_finds_a_thing_by_what_it_does()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 1/1",
                "[id | name | category | keywords | matchedOn]",
                "1c6da5 | Raise Druidry Lv | upgrades | - | name",
            }),
            Render(Search("Druidry")));

        // The word the effect carries and the owner does not: nothing about a Blooming resource
        // type is called "Plot Capacity", and it is what one more level of it buys.
        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 1/1",
                "[id | name | category | keywords | matchedOn]",
                "1e1d96 | Blooming | resource-types | - | effects",
            }),
            Render(Search("Plot Capacity")));

        // The property word is an effect word too, so the glyph answers for what it modifies.
        Assert.Equal(
            new[] { "1a1d55", "bcfc9f" },
            Rows(Search("EffectLevel")).Select(row => (string?)row["uuid"]).ToArray());
    }

    private static JObject[] Effects(string category, Guid uuid) =>
        Assert.IsType<JArray>(Row(category, uuid)["levelEffects"])
            .Values<JObject>().Select(row => row!).ToArray();

    private static JObject Row(string category, Guid uuid) =>
        (JObject)Assert.Single(
            Json(GameMcpWorldQuery.GetRows(
                Context(), category, new[] { uuid.ToString("D") }))
            ["results"]!.Values<JObject>())!["row"]!;

    private static string Render(JObject page) => GameMcpTextPage.Render(page).TrimEnd('\n');

    private static JObject Search(string query) =>
        Json(GameMcpWorldQuery.Search(Context(), query, 0, 50, limitFromCaller: false));

    private static JObject[] Rows(JObject page) =>
        page["rows"]!.Values<JObject>().Select(row => row!).ToArray();

    private static JObject Json(GameMcpObjectBuilder value) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value.Freeze(), Catalog));

    private static GameMcpFrameContext Context()
    {
        var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(World(), new WorldGeneration(983));
        return GameMcpTestHarness.Context(publisher.ReadLatest());
    }

    private static GameWorldState World()
    {
        var upgrade = new RawUpgradeSample(
            Druidry, level: 3, maxLevel: -1, available: true, queuedLevels: 0,
            buildTime: BigDouble.Zero, developmentTime: 0d, cachedCostLevel: 3);
        return new GameWorldState
        {
            EntityIdentities = Catalog,
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                new WorldUpgrade(in upgrade, false, false, 0, 3, false, 0d),
            }),
            AugmentGlyphs = PublicationTable<WorldGlyph>.Create(new[]
            {
                new WorldGlyph(
                    BloomingGlyph, 2, 0, 0, true, true, true, true, false, false, 0,
                    BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            }),
            ResourceTypes = PublicationTable<WorldResourceType>.Create(new[]
            {
                new WorldResourceType(BloomingType, 4, 0, false, false, false, true),
            }),
            EquipmentTypes = PublicationTable<WorldEquipmentType>.Create(new[]
            {
                new WorldEquipmentType(Amulet, 3, 1, 2, new BigDouble(5), new BigDouble(2)),
            }),
            SpellTypes = PublicationTable<WorldSpellType>.Create(new[] { SpellType(Arcane) }),
            TimeRunes = PublicationTable<WorldTimeRune>.Create(new[]
            {
                new WorldTimeRune(
                    AbilityPersist, true, 2, 0, BigDouble.Zero, 0, false, true,
                    BigDouble.Zero, new BigDouble(3), BigDouble.Zero, BigDouble.Zero),
            }),
            LevelEffects = PublicationTable<WorldLevelEffect>.Create(Sorted(new[]
            {
                // RaiseMaxDruidryLevel, verbatim: one TupleMod<NumberVariable> and two
                // UpgradeEffectModifiers, in the order the asset authors them.
                Effect(Druidry, 0, string.Empty, MaxDruidryLevel,
                    GameValueModifierType.Raw, 1d),
                Effect(Druidry, 1, "Yield", AllPlot, GameValueModifierType.MultiStacking, 1.02d),
                Effect(Druidry, 2, "RecoverySizeMod", AllPlot,
                    GameValueModifierType.MultiStacking, 1.75d),

                Effect(BloomingGlyph, 0, "Value", StockRestore,
                    GameValueModifierType.MultiDiminishing, 1.15d),
                Effect(BloomingType, 0, "MaxQuantity", PlotCapacity,
                    GameValueModifierType.Raw, 5d),
                Effect(Amulet, 0, "EffectLevel", AuraStructures, GameValueModifierType.Raw, 5d),
                Effect(Arcane, 0, "EffectLevel", ArcanistStructures,
                    GameValueModifierType.Raw, 2d),
            })),
            CollectionCategories =
                PublicationTable<WorldCollectionCategoryStatus>.Create(CleanReports()),
            CollectedAtEpoch = 91,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
    }

    private static WorldLevelEffect Effect(
        Guid ownerId,
        int ordinal,
        string property,
        Guid targetId,
        GameValueModifierType type,
        double amount) =>
        new(ownerId, ordinal, property, targetId, (int)type, new BigDouble(amount), 0);

    private static WorldLevelEffect[] Sorted(WorldLevelEffect[] rows)
    {
        Array.Sort(rows, static (left, right) =>
        {
            var owner = left.OwnerId.CompareTo(right.OwnerId);
            return owner != 0 ? owner : left.Ordinal.CompareTo(right.Ordinal);
        });
        return rows;
    }

    private static WorldSpellType SpellType(Guid id) =>
        new(
            id, 2, default, 0d, 0d, false, false, false, false, true, false,
            default, new BigDouble(150), new BigDouble(100), default, new BigDouble(80),
            default, default, new BigDouble(200), default, default, default, default, default,
            default, default, default, default, default, default, default,
            new BigDouble(35), new BigDouble(240));

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
        EntityIdentityCatalogSnapshot.Bound(1, SortedNames(new[]
        {
            new EntityIdentityName(Druidry, "UpgradeSO", "Raise Druidry Lv", "RaiseMaxDruidryLevel"),
            new EntityIdentityName(
                MaxDruidryLevel, "IntVariable", "Max Druidry Lv", "MaxDruidryLevel"),
            new EntityIdentityName(AllPlot, "PlotNodeType", "All Plot", "GlobalNodeType"),
            new EntityIdentityName(BloomingGlyph, "GlyphSO", "Blooming", "Blooming"),
            new EntityIdentityName(
                BloomingType, "ResourceTypeSO", "Blooming", "BloomingResourceType"),
            new EntityIdentityName(PlotCapacity, "ResourceSO", "Plot Capacity", "WeightPlots"),
            new EntityIdentityName(Amulet, "EquipmentTypeSO", "Amulet", "AmuletEquipmentType"),
            new EntityIdentityName(Arcane, "SpellTypeSO", "Arcane", "Arcane"),
            new EntityIdentityName(
                AbilityPersist, "TimeRuneSO", "Ability Persist", "TimeAbilityPersist"),
        }));

    private static EntityIdentityName[] SortedNames(EntityIdentityName[] rows)
    {
        Array.Sort(rows, static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return rows;
    }
}
