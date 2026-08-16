using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// The ritual layer's own vocabularies on the wire, and the Statistics tab's headings beside them.
/// </summary>
/// <remarks>
/// Every id, word, number and sentence here is the pinned build's own
/// (<c>data/game-data.json</c>). These 84 rows were the last thing <c>entity_catalog</c> listed and
/// the published world had nothing to say about, so the assertions are about what one page now says
/// without a second call.
/// </remarks>
public sealed class GameMcpRitualGlossaryTests
{
    private static readonly Guid Barrier = Guid.Parse("5efcba09-6cc4-46a0-94ff-8c12f974442f");
    private static readonly Guid Burning = Guid.Parse("ff1240a4-22f7-46b2-9a81-4de7728b86b8");
    private static readonly Guid Fire = Guid.Parse("c608e1c8-e5f5-4de3-becc-87f3174987d3");
    private static readonly Guid Area = Guid.Parse("d6b0f19c-1c2f-4a75-9c96-3a9a1d5c88aa");
    private static readonly Guid Power = Guid.Parse("3559f6a0-c6e3-4c9f-bd26-35ac9dc904f6");
    private static readonly Guid SummonReinforcements =
        Guid.Parse("f8a9326a-2f98-4f05-889e-3078635fd714");
    private static readonly Guid Advancement = Guid.Parse("0796ee25-e1f6-4c5c-abba-aad46e02318b");
    private static readonly Guid Echoing = Guid.Parse("d854b177-865f-45ee-97a3-23d904df1ba1");
    private static readonly Guid Refund = Guid.Parse("8fb5fcdb-83cf-4d7e-843b-5ed38b906905");
    private static readonly Guid Herbalism = Guid.Parse("1a000000-0000-4000-8000-000000000001");
    private static readonly Guid Gathering = Guid.Parse("2b000000-0000-4000-8000-000000000002");

    /// <summary>
    /// The status vocabulary on one page: whether each one helps, how long it lasts, whether a
    /// second application runs beside the first, and the sentence the game prints under it. The
    /// negative duration is the game's own — those statuses last as long as their stack count.
    /// </summary>
    [Fact]
    public void The_status_glossary_pages_the_word_the_rules_and_the_sentence_together()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 2/2",
                "[id | name | isBuff | maxDuration | stacksSeparately | description]",
                "5efcba | Barrier | yes | -1 | no | +500 Damage Reduction",
                "ff1240 | Burning | no | 5 | no | Deals fire damage over time.",
            }),
            Render(Json(GameMcpWorldQuery.ListRows(Context(World()), "status-effects", 0, 20))));
    }

    /// <summary>
    /// A vocabulary whose whole published fact is its word and its sentence is still worth a page:
    /// the eight words the scribing screen prints were readable on no verb at all before this.
    /// </summary>
    [Fact]
    public void A_vocabulary_whose_only_fact_is_its_sentence_still_pages_that_sentence()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 2/2",
                "[id | name | description]",
                "0796ee | Advancment | Amplifying an attributes levels.",
                "d854b1 | Echoing | Amplifies an attribute's echo rating.",
            }),
            Render(Json(GameMcpWorldQuery.ListRows(Context(World()), "enchantments", 0, 20))));
    }

    /// <summary>
    /// The one edge inside the glossary, followed rather than restated: a combatant's stat names the
    /// damage type it is about, and that type is a row of its own with its own sentence.
    /// </summary>
    /// <remarks>
    /// On the pinned build the game authors <c>damageType</c> null on all eight, so the live column
    /// reads empty everywhere. The fixture fills it because the assertion is about the edge
    /// travelling, and a test that only ever saw the empty case could not tell a published edge from
    /// a dropped one.
    /// </remarks>
    [Fact]
    public void A_combatants_stat_names_the_damage_type_it_is_about()
    {
        var row = Detail(Power);

        Assert.Equal("Power", (string?)row["name"]);
        Assert.Equal("character-attributes", (string?)row["category"]);
        Assert.Equal(
            GameMcpEntityHandle.Format(Fire), (string?)row["row"]!["damageType"]!["uuid"]);
        Assert.Equal("Fire", (string?)row["row"]!["damageType"]!["name"]);
    }

    /// <summary>
    /// A stat group answers with the distribution the game stores on it: which record on which
    /// entity its bonus is merged into, and at what ratio. Nothing on the far side names its group,
    /// so this is the only direction there is and no reverse edge is derived.
    /// </summary>
    [Fact]
    public void A_stat_group_answers_with_what_its_bonus_reaches()
    {
        var row = Detail(Refund);
        var members = row["row"]!["members"]!.Values<JObject>().ToArray();

        Assert.Equal("Agromancy Refund", (string?)row["name"]);
        Assert.Equal(2, members.Length);
        Assert.Equal("Herbalism", (string?)members[0]!["modifies"]!["name"]);
        Assert.Equal(
            GameMcpEntityHandle.Format(Herbalism),
            (string?)members[0]!["modifies"]!["uuid"]);
        Assert.Equal("RefundRating", (string?)members[0]!["property"]);
        Assert.Equal(0, (int)members[0]!["propertyIndex"]!);
        Assert.Equal(1d, (double)members[0]!["ratio"]!);
        Assert.Equal(1d, (double)members[0]!["ratioExp"]!);
        Assert.Equal(0, (int)members[0]!["orderAdjust"]!);
        Assert.Equal("Gathering", (string?)members[1]!["modifies"]!["name"]);
    }

    /// <summary>
    /// The member rows are the group's own and no one else's. A second group's distribution sits in
    /// the same table, and a range lookup that ran off its end would hand a reader another
    /// heading's bonuses under this one's word.
    /// </summary>
    [Fact]
    public void A_group_that_distributes_nothing_carries_no_members_block()
    {
        var row = Detail(Area, "damage-types");

        Assert.Equal("Area", (string?)row["name"]);
        Assert.Null(row["row"]!["members"]);
    }

    /// <summary>
    /// W11.4, and it was already true before this lane. <c>world_search</c> matches an entity's
    /// internal asset name in the name band and says <c>internalName</c> when that is what the query
    /// hit, so a newly published glossary row is reachable by the identifier the data files spell it
    /// with — not only by the word with the space in it that the screen prints.
    /// </summary>
    [Fact]
    public void A_glossary_row_is_reachable_by_its_internal_asset_name()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 1/1",
                "[id | name | category | keywords | matchedOn]",
                "f8a932 | Summon Reinforcements | character-actions | - | internalName",
            }),
            Render(Json(GameMcpWorldQuery.Search(
                Context(World()),
                "SummonReinforcements",
                0,
                20,
                string.Empty,
                string.Empty))));
    }

    /// <summary>
    /// The eleven vocabularies are eleven categories rather than one glossary table, because the
    /// game names each of them separately and their columns are different facts.
    /// </summary>
    [Fact]
    public void Each_authored_vocabulary_is_its_own_category_under_the_games_own_word()
    {
        var pairs = new[]
        {
            ("status-effects", "CombatStatusSO"),
            ("character-attributes", "CharacterAttributeSO"),
            ("damage-types", "DamageTypeSO"),
            ("character-modifiers", "CharacterModifierSO"),
            ("character-actions", "CharacterActionSO"),
            ("character-types", "CharacterTypeSO"),
            ("enchantments", "EnchantmentSO"),
            ("glyph-types", "GlyphTypeSO"),
            ("rune-stones", "RuneStoneSO"),
            ("display-types", "DisplayTypeSO"),
            ("attribute-groups", "AttributeGroupSO"),
        };

        foreach (var (category, nativeType) in pairs)
        {
            Assert.Equal(nativeType, GameMcpEntityCapabilityMap.ExpectedNativeType(category));
            Assert.True(
                GameMcpEntityCapabilityMap.TryCategoryForNativeType(nativeType, out var derived));
            Assert.Equal(category, derived);
            Assert.Contains(category, GameMcpWorldQuery.RegisteredCategoryNames());
        }
    }

    private static string Render(JObject page) => GameMcpTextPage.Render(page).TrimEnd('\n');

    private static JObject Detail(Guid uuid, string category = "") =>
        Assert.Single(
            Json(GameMcpWorldQuery.GetRows(
                Context(World()), category, new[] { uuid.ToString("D") }))
            ["results"]!.Values<JObject>())!;

    private static JObject Json(GameMcpObjectBuilder value) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value.Freeze(), Catalog));

    private static readonly EntityIdentityCatalogSnapshot Catalog =
        EntityIdentityCatalogSnapshot.Bound(1, Sorted(new[]
        {
            new EntityIdentityName(Barrier, "CombatStatusSO", "Barrier", "Barrier"),
            new EntityIdentityName(Burning, "CombatStatusSO", "Burning", "Burning"),
            new EntityIdentityName(Fire, "DamageTypeSO", "Fire", "FireDamage"),
            new EntityIdentityName(Area, "DamageTypeSO", "Area", "AreaDamage"),
            new EntityIdentityName(Power, "CharacterAttributeSO", "Power", "ActionPower"),
            new EntityIdentityName(
                SummonReinforcements,
                "CharacterActionSO",
                "Summon Reinforcements",
                "SummonReinforcements"),
            new EntityIdentityName(
                Advancement, "EnchantmentSO", "Advancment", "EnchantAdvancement"),
            new EntityIdentityName(Echoing, "EnchantmentSO", "Echoing", "EnchantEcho"),
            new EntityIdentityName(
                Refund, "AttributeGroupSO", "Agromancy Refund", "AgromancyRefundGroup"),
            new EntityIdentityName(
                Herbalism, "HarvestActionTypeSO", "Herbalism", "HarvestActionType"),
            new EntityIdentityName(
                Gathering, "HarvestActionTypeSO", "Gathering", "GatheringActionType"),
        }));

    private static EntityIdentityName[] Sorted(EntityIdentityName[] rows)
    {
        Array.Sort(rows, static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return rows;
    }

    private static GameMcpFrameContext Context(GameWorldState world)
    {
        var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(991));
        return GameMcpTestHarness.Context(publisher.ReadLatest());
    }

    private static GameWorldState World() =>
        new()
        {
            EntityIdentities = Catalog,
            CollectedAtEpoch = 91,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            StatusEffects = PublicationTable<WorldStatusEffect>.Create(new[]
            {
                new WorldStatusEffect(
                    Barrier, true, -1d, false, false, 0.1d,
                    "<emph>+500 Damage Reduction</emph>"),
                new WorldStatusEffect(
                    Burning, false, 5d, false, true, 1d, "Deals fire damage over time."),
            }.OrderBy(row => row.EntityId).ToArray()),
            DamageTypes = PublicationTable<WorldDamageType>.Create(new[]
            {
                new WorldDamageType(Fire, 1d, false, "High temperature damage."),
                new WorldDamageType(Area, 1.2d, true, "Damage dealt to multiple units."),
            }.OrderBy(row => row.EntityId).ToArray()),
            CharacterAttributes = PublicationTable<WorldCharacterAttribute>.Create(new[]
            {
                new WorldCharacterAttribute(
                    Power, Fire, "How much effect this character's actions have."),
            }),
            CharacterActions = PublicationTable<WorldCharacterAction>.Create(new[]
            {
                new WorldCharacterAction(
                    SummonReinforcements, 0.67d, 12d, 1d, "Summons two more combatants."),
            }),
            Enchantments = PublicationTable<WorldEnchantment>.Create(new[]
            {
                new WorldEnchantment(Advancement, "Amplifying an attributes levels."),
                new WorldEnchantment(
                    Echoing, "Amplifies an attribute's <emph>echo rating</emph>."),
            }.OrderBy(row => row.EntityId).ToArray()),
            AttributeGroups = PublicationTable<WorldAttributeGroup>.Create(new[]
            {
                new WorldAttributeGroup(Refund, "Chance for agromancy actions to refund."),
            }),
            AttributeGroupMembers = PublicationTable<WorldAttributeGroupMember>.Create(new[]
            {
                new WorldAttributeGroupMember(
                    Refund, 0, Herbalism, "RefundRating", 0, 1d, 1d, 0),
                new WorldAttributeGroupMember(
                    Refund, 1, Gathering, "RefundRating", 0, 1d, 1d, 0),
            }),
            CollectionCategories =
                PublicationTable<WorldCollectionCategoryStatus>.Create(CleanReports()),
        };

    /// <summary>
    /// Every category read clean, so a page carries its rows rather than a list of the categories
    /// this fixture happens not to populate.
    /// </summary>
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
}
