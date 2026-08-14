using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
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
/// A keyword is a type asset, and a detail read on one now answers what it is currently worth and
/// who paid in. The two magnitudes never share a key, because one of them is already inside the
/// member numbers a caller is holding beside it.
/// </summary>
/// <remarks>
/// Each record is named by the word the game's own tooltip prints for it, so a reader compares the
/// wire against the screen rather than against a field name only the assembly says. A record the
/// game words nowhere keeps its internal name and is pinned as such — see
/// <see cref="GameMcpModifierPropertyWordsTests"/> for the whole census.
/// </remarks>
public sealed class GameMcpTypeWorthTests
{
    private static readonly Guid Focus = Guid.Parse("a0a00000-0000-4000-8000-000000000001");
    private static readonly Guid Wand = Guid.Parse("a0b00000-0000-4000-8000-000000000001");
    private static readonly Guid Rod = Guid.Parse("a0c00000-0000-4000-8000-000000000001");
    private static readonly Guid Insight = Guid.Parse("a0d00000-0000-4000-8000-000000000001");
    private static readonly Guid Study = Guid.Parse("a0e00000-0000-4000-8000-000000000001");
    private static readonly Guid Ember = Guid.Parse("a0f00000-0000-4000-8000-000000000001");
    private static readonly Guid Tonic = Guid.Parse("a1a00000-0000-4000-8000-000000000001");
    private static readonly Guid Bench = Guid.Parse("a1b00000-0000-4000-8000-000000000001");
    private static readonly Guid Ward = Guid.Parse("a1c00000-0000-4000-8000-000000000001");

    /// <summary>
    /// The whole block on a type whose records hand their bonuses down: one line per record, the
    /// total under the name that says it is already spent, and every modifier under the name of
    /// whoever placed it.
    /// </summary>
    /// <remarks>
    /// "Artifact Power" is <c>powerMod</c> under the game's own word, hand-computed off the pinned
    /// fold: seed 100, Raw +20, then one MultiDiminishing multiply of 1 + 0.5, which is 180 and not
    /// 100 × 1.2 × 1.5 read in the other order. <c>experienceRateMod</c> carries nothing and totals
    /// to a flat 100, which is a reading rather than an absence, and keeps its internal name because
    /// this class authors no display word for it. <c>masteryLevel</c> is the fourth record this type
    /// carries and is not among the properties: nothing in the game reads it, so no purchase can
    /// move it, and worth says only what a purchase can move. The row above still carries it,
    /// because the row is what the build holds.
    /// </remarks>
    [Fact]
    public void A_type_says_what_each_record_is_worth_and_names_everyone_who_paid_in()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "uuid: a0a000",
                "name: Focus",
                "internalName: focusType",
                "category: equipment-types",
                "row:",
                "  baseUsage: 2",
                "  masteryLevel: 5",
                "  maximumSlots: 2",
                "  paidLevel: 0",
                "  totalLevel: 0",
                "  purchase: no (ERR_REFUSED): The game refuses to level this right now.",
                "worth:",
                "  howToRead: A distributedTotalPercent is already inside each member's own " +
                "numbers and must never be multiplied into one again; a value is this type's own " +
                "number and applies on top of whatever wears the type.",
                "  members 1",
                "  [kind | count]",
                "  equipment | 2",
                "  properties 2:",
                "    property: Type Slots",
                "    value: 2",
                "",
                "    property: Artifact Power",
                "    distributedTotalPercent: 180",
                "    sources 2",
                "    [amount | effect | order | source]",
                "    20 | raw | 0 | Deep Insight a0d000",
                "    0.5 | diminishing | 0 | Focused Study a0e000",
                "  unmodified: experienceRateMod",
            }),
            Render(Detail(Focus)));
    }

    /// <summary>
    /// A spell type hands nothing down, so it never borrows the wording that says it did. Its
    /// numbers are its own, they appear nowhere else on the wire, and the block says so under a
    /// different key.
    /// </summary>
    /// <remarks>
    /// This is the class the game words most fully: all four records here print the tooltip's own
    /// name rather than <c>cooldownSpeed</c>, <c>costMod</c>, <c>elementalResonance</c> and
    /// <c>power</c>. Six records are captured on this type, not four — <c>bonusFlashRate</c> and
    /// <c>flashEffectMod</c> hold 35 and 240 and appear nowhere, because the pinned build routes
    /// neither name and reads neither field, so nothing a player buys can change either number.
    /// </remarks>
    [Fact]
    public void A_spell_type_answers_with_its_own_numbers_and_never_a_handed_down_total()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "uuid: a0f000",
                "name: Ember",
                "internalName: emberType",
                "category: spell-types",
                "row: typeLevel=0, typeXp=0, isVisible=yes, isElemental=no, " +
                "isLoadoutUnique=no",
                "worth:",
                "  howToRead: These are this type's own numbers, and they apply on top of " +
                "whatever wears the type.",
                "  properties 4:",
                "    property: Cooldown Speed",
                "    value: 100",
                "",
                "    property: Spell Cost",
                "    value: 80",
                "",
                "    property: Elemental Resonance",
                "    value: 200",
                "",
                "    property: Spell Power",
                "    value: 150",
                "    sources 1",
                "    [amount | effect | order | source]",
                "    50 | diminishing | 0 | Deep Insight a0d000",
            }),
            Render(Detail(Ember)));

        var worth = Detail(Ember)["worth"]!;

        // The one key the pull case must never carry: nothing here is inside a member's number, so
        // nothing here may be read as a bonus that was already spent, and the sentence above it
        // never borrows the wording that says it was.
        Assert.All(
            worth["properties"]!.Values<JObject>(),
            row => Assert.Null(row!["distributedTotalPercent"]));
        Assert.DoesNotContain(
            "distributedTotalPercent", (string?)worth["howToRead"]!, StringComparison.Ordinal);

        // Membership is a keyword edge, and a spell's types are published as spell-graph relations
        // instead. Saying "0 things" would be a count of a table that does not describe it.
        Assert.Null(worth["members"]);
    }

    /// <summary>
    /// A record the game cannot read stays captured and is priced nowhere. Publication carries what
    /// the build holds; worth carries what a purchase can move, and these two are not the same set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole shape of the ruling in one place. Dropping the rows from the capture would
    /// have been the other way to make the page right, and it would have made the world lie about
    /// the assembly; printing them was the way it was wrong before, and it offered a number no
    /// investment could ever change.
    /// </para>
    /// <para>
    /// The derived side needs no liveness rule of its own today, and the last assertion is what says
    /// so: every dead record on the pinned build is a <c>ValueModifierRecord</c>, which the total
    /// deriver already passes over because such a record is folded where it is published. A build
    /// that leaves a <i>distributor</i> dead breaks this rather than quietly publishing a total for
    /// machinery nothing reads, which is why the outcome is asserted instead of a branch being added
    /// that cannot fire.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_record_the_game_cannot_read_is_captured_and_priced_nowhere()
    {
        var world = World();
        Assert.True(WorldTypeModifierLookup.TryFind(
            world.TypeModifiers, Ember, out var start, out var count));
        var captured = Enumerable.Range(start, count)
            .Select(index => world.TypeModifiers[index].Property)
            .ToArray();
        Assert.Contains("bonusFlashRate", captured);
        Assert.Contains("flashEffectMod", captured);

        var printed = Detail(Ember)["worth"]!["properties"]!.Values<JObject>()
            .Select(row => (string?)row!["property"])
            .ToArray();
        Assert.DoesNotContain("bonusFlashRate", printed);
        Assert.DoesNotContain("flashEffectMod", printed);

        for (var index = 0; index < world.TypeModifierTotals.Count; index++)
        {
            var total = world.TypeModifierTotals[index];
            Assert.True(
                WorldTypeModifierLiveness.IsLive(total.OwnerKind, total.Property),
                total.OwnerKind + "." + total.Property + " is dead and was given a derived total.");
        }
    }

    /// <summary>The twenty spell type records the game can read, from the world's own census.</summary>
    public static TheoryData<string> LiveSpellTypeRecords()
    {
        var data = new TheoryData<string>();
        foreach (var property in WorldTypeModifierBindings.Records(
                     WorldTypeModifierOwnerKind.SpellType))
        {
            if (WorldTypeModifierLiveness.IsLive(WorldTypeModifierOwnerKind.SpellType, property))
                data.Add(property);
        }

        return data;
    }

    /// <summary>
    /// Every value record the five detail-readable type classes carry resolves to the number the
    /// record actually holds. A record whose class is not mapped here would answer with sources and
    /// no magnitude, which reads as a distributor whose total went missing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The expected name comes from the word table rather than a second list here, so each record
    /// answers under whatever disposition the census gave it.
    /// </para>
    /// <para>
    /// The cases come from the world's own record census filtered by liveness rather than from a
    /// hand-kept list, so a build that routes a flash name adds its case here the moment the
    /// liveness table is re-censused — and the case fails until this block learns to read that
    /// record's value. A hand-kept list would have let the record go live and stay unpriced.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(LiveSpellTypeRecords))]
    public void Every_spell_type_record_the_game_authors_has_a_number_on_the_wire(string property)
    {
        var results = Json(GameMcpWorldQuery.GetRows(
            Context(World(SpellTypeRecords(property))),
            string.Empty,
            new[] { Ember.ToString("D") }))["results"]!;
        var block = Assert.Single(results.Values<JObject>())!;
        var row = Assert.Single(block["worth"]!["properties"]!.Values<JObject>())!;

        Assert.Equal(
            GameMcpModifierPropertyWords.Word(WorldTypeModifierOwnerKind.SpellType, property),
            (string?)row["property"]);
        Assert.NotNull(row["value"]);
    }

    /// <summary>
    /// The two other classes whose records include a value: an alchemy type's level and a crafting
    /// type's magnitude increment are numbers those types hold, not bonuses they handed anywhere.
    /// The crafting record prints "Magnitude" because the game prints it; the alchemy level stands
    /// under its own name because the game prints nothing for it.
    /// </summary>
    [Fact]
    public void The_other_value_records_answer_with_the_number_their_own_type_holds()
    {
        var alchemy = Assert.Single(
            Detail(Tonic)["worth"]!["properties"]!.Values<JObject>())!;
        Assert.Equal("level", (string?)alchemy["property"]);
        Assert.Equal("4", (string?)alchemy["value"]);

        var crafting = Assert.Single(
            Detail(Bench)["worth"]!["properties"]!.Values<JObject>())!;
        Assert.Equal("Magnitude", (string?)crafting["property"]);
        Assert.Equal("7", (string?)crafting["value"]);
    }

    /// <summary>
    /// A type's row is the scalars the type itself stores. How loaded each of its records is is not
    /// among them: that is one row per record on <c>type modifiers</c>, and the block under the row
    /// is where a reader meets it.
    /// </summary>
    [Fact]
    public void A_type_row_carries_the_type_s_own_scalars_and_never_its_modifier_load()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "uuid: a1a000",
                "name: Tonic",
                "internalName: tonicType",
                "category: alchemy-types",
                "row: level=4, maxUsageByMastery=no",
                "worth:",
                "  howToRead: These are this type's own numbers, and they apply on top of " +
                "whatever wears the type.",
                "  properties 1",
                "  [property | value]",
                "  level | 4",
            }),
            Render(Detail(Tonic)));

        Assert.Equal(
            string.Join('\n', new[]
            {
                "uuid: a1b000",
                "name: Bench",
                "internalName: benchType",
                "category: crafting-recipe-types",
                "row: startingLevel=1, maxStartingLevel=4, craftVerb=craft, initiated=yes",
                "worth:",
                "  howToRead: These are this type's own numbers, and they apply on top of " +
                "whatever wears the type.",
                "  properties 1",
                "  [property | value]",
                "  Magnitude | 7",
            }),
            Render(Detail(Bench)));
    }

    /// <summary>
    /// An entity that is not a type is answered exactly as it was answered before types learned to
    /// say what they are worth — same keys, same bytes, no empty block standing in for the one it
    /// has no reason to carry.
    /// </summary>
    [Fact]
    public void An_entity_that_is_no_type_is_answered_byte_for_byte_as_it_was()
    {
        var detail = Detail(Ward);

        Assert.Null(detail["worth"]);
        Assert.Equal(
            "{\"uuid\":\"a1c000\",\"name\":\"Focus Ward\",\"internalName\":\"focusWard\"," +
            "\"category\":\"glyphs\",\"row\":{" +
            "\"population\":\"augment\",\"screen\":\"unreadable\",\"state\":\"locked\"," +
            "\"discovered\":false,\"usableCount\":0,\"reasonCode\":\"ERR_LOCKED\"," +
            "\"reason\":\"This has not been discovered yet.\"," +
            "\"paidLevel\":0,\"totalLevel\":0,\"purchase\":{\"available\":false," +
            "\"reasonCode\":\"ERR_LOCKED\",\"reason\":\"The game has not unlocked this yet.\"}," +
            "\"discover\":{\"available\":false,\"reasonCode\":\"ERR_LOCKED\"," +
            "\"reason\":\"The game is not showing this yet.\"}},\"predicates\":{" +
            "\"visible\":{\"available\":false,\"reasonCode\":\"ERR_LOCKED\"," +
            "\"reason\":\"This has not been discovered yet.\"},\"available\":{\"available\":false," +
            "\"reasonCode\":\"ERR_LOCKED\",\"reason\":\"This has not been discovered yet.\"}," +
            "\"canDiscover\":{\"available\":true}}}",
            detail.ToString(Formatting.None));
    }

    /// <summary>
    /// One word per concept across the type taxonomies. The list column read <c>level</c> over the
    /// number the type's own page spells <c>totalLevel</c>, and a round could not settle from the
    /// list which of the page's four level fields it had been handed.
    /// </summary>
    [Fact]
    public void An_equipment_type_list_row_names_the_level_it_carries_the_way_its_page_does()
    {
        var world = World();
        var listed = Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(world, generation: 6101), "equipment-types", 0, 50));
        var row = Assert.Single(listed["rows"]!.Values<JObject>())!;
        var page = Detail(Focus);

        Assert.Equal((int)page["row"]!["totalLevel"]!, (int)row["totalLevel"]!);
        Assert.Null(row["level"]);
    }

    /// <summary>
    /// Search rows say the same things about every hit and gain nothing here. What a type is worth
    /// is volatile, it belongs to the reader who asked a detail question, and a list stays durable
    /// facts only.
    /// </summary>
    [Fact]
    public void A_search_row_says_nothing_new_now_that_a_type_can_price_itself()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 2/2",
                "[id | name | category | keywords | matchedOn]",
                "a0a000 | Focus | equipment-types | - | name",
                "a1c000 | Focus Ward | glyphs | - | name",
            }),
            Render(Json(GameMcpWorldQuery.Search(
                Context(World()), "focus", 0, 50, string.Empty, string.Empty, string.Empty,
                limitFromCaller: false))));
    }

    private static string Render(JObject page) => GameMcpTextPage.Render(page).TrimEnd('\n');

    private static JObject Detail(Guid uuid) =>
        Assert.Single(
            Json(GameMcpWorldQuery.GetRows(
                Context(World()), string.Empty, new[] { uuid.ToString("D") }))
            ["results"]!.Values<JObject>())!;

    private static JObject Json(GameMcpObjectBuilder value) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value.Freeze(), Catalog));

    private static readonly EntityIdentityCatalogSnapshot Catalog =
        EntityIdentityCatalogSnapshot.Bound(1, new[]
        {
            new EntityIdentityName(Focus, "EquipmentTypeSO", "Focus", "focusType"),
            new EntityIdentityName(Wand, "EquipmentSO", "Wand", "wandPiece"),
            new EntityIdentityName(Rod, "EquipmentSO", "Rod", "rodPiece"),
            new EntityIdentityName(Insight, "UpgradeSO", "Deep Insight", "deepInsight"),
            new EntityIdentityName(Study, "ResearchSO", "Focused Study", "focusedStudy"),
            new EntityIdentityName(Ember, "SpellTypeSO", "Ember", "emberType"),
            new EntityIdentityName(Tonic, "AlchemyTypeSO", "Tonic", "tonicType"),
            new EntityIdentityName(Bench, "CraftingRecipeTypeSO", "Bench", "benchType"),
            new EntityIdentityName(Ward, "GlyphSO", "Focus Ward", "focusWard"),
        });

    private static GameMcpFrameContext Context(GameWorldState world)
    {
        var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(904));
        return GameMcpTestHarness.Context(publisher.ReadLatest());
    }

    /// <summary>
    /// One world holding all five detail-readable type classes, their records, and the modifiers
    /// currently sitting on them. Totals and membership come off the real derivers rather than
    /// being asserted into the fixture, so the wire is pinned against the fold and not against a
    /// second copy of it.
    /// </summary>
    private static GameWorldState World(params (Guid TypeId, WorldTypeModifierOwnerKind Kind,
        string Property, string RecordNativeType)[] extraRecords)
    {
        var records = Records(new[]
        {
            (Focus, WorldTypeModifierOwnerKind.EquipmentType, "powerMod", "MergingModifierRecord"),
            (Focus, WorldTypeModifierOwnerKind.EquipmentType, "experienceRateMod",
                "OrderedMultiplierRecord"),
            (Focus, WorldTypeModifierOwnerKind.EquipmentType, "masteryLevel",
                "ValueModifierRecord"),
            (Focus, WorldTypeModifierOwnerKind.EquipmentType, "maxTypeSlots",
                "ValueModifierRecord"),
            (Tonic, WorldTypeModifierOwnerKind.AlchemyType, "level", "ValueModifierRecord"),
            (Bench, WorldTypeModifierOwnerKind.CraftingRecipeType, "magnitudeIncrement",
                "ValueModifierRecord"),
        }.Concat(extraRecords.Length == 0
            ? new[]
            {
                (Ember, WorldTypeModifierOwnerKind.SpellType, "power", "ValueModifierRecord"),
                (Ember, WorldTypeModifierOwnerKind.SpellType, "costMod", "ValueModifierRecord"),
                (Ember, WorldTypeModifierOwnerKind.SpellType, "cooldownSpeed",
                    "ValueModifierRecord"),
                (Ember, WorldTypeModifierOwnerKind.SpellType, "elementalResonance",
                    "ValueModifierRecord"),

                // Captured exactly like the four above, and printed by nothing: the pinned build
                // routes neither, so no purchase can move either.
                (Ember, WorldTypeModifierOwnerKind.SpellType, "bonusFlashRate",
                    "ValueModifierRecord"),
                (Ember, WorldTypeModifierOwnerKind.SpellType, "flashEffectMod",
                    "ValueModifierRecord"),
            }
            : extraRecords).ToArray());

        var contributions = Contributions(
            (Focus, "powerMod", GameValueModifierType.Raw, 20d, Insight),
            (Focus, "powerMod", GameValueModifierType.MultiDiminishing, 0.5d, Study),
            (Focus, "masteryLevel", GameValueModifierType.Raw, 2d, Insight),
            (Ember, "power", GameValueModifierType.MultiDiminishing, 50d, Insight));

        var keywords = PublicationTable<WorldEntityKeyword>.Create(Sorted(
            new WorldEntityKeyword(
                Wand, WorldKeywordOwnerKind.Equipment, WorldKeywordSource.PrimaryType, 0, Focus),
            new WorldEntityKeyword(
                Rod, WorldKeywordOwnerKind.Equipment, WorldKeywordSource.PrimaryType, 0, Focus)));
        var totals = WorldTypeModifierTotalDeriver.Build(records, contributions);

        return new GameWorldState
        {
            EntityIdentities = Catalog,
            EquipmentTypes = PublicationTable<WorldEquipmentType>.Create(new[]
            {
                new WorldEquipmentType(Focus, 3, 1, 2, new BigDouble(5), new BigDouble(2)),
            }),
            Glyphs = PublicationTable<WorldGlyph>.Create(new[]
            {
                new WorldGlyph(
                    Ward, 0, 0, 1, false, true, false, false, false, false,
                    0, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            }),
            SpellTypes = PublicationTable<WorldSpellType>.Create(new[]
            {
                SpellType(Ember),
            }),
            AlchemyTypes = PublicationTable<WorldAlchemyType>.Create(new[]
            {
                new WorldAlchemyType(
                    Tonic, Guid.Empty, maxUsageByMastery: false, level: new BigDouble(4)),
            }),
            CraftingRecipeTypes = PublicationTable<WorldCraftingRecipeType>.Create(new[]
            {
                new WorldCraftingRecipeType(
                    Bench, 1, 4, "craft", isLevelType: true, initiated: true, 0d, 0d,
                    new BigDouble(7)),
            }),
            EntityKeywords = keywords,
            TypeModifiers = records,
            TypeModifierContributions = contributions,
            TypeModifierTotals = totals,
            KeywordModifiers = WorldKeywordModifierDeriver.Build(
                totals, keywords, PublicationTable<WorldTypeSubtype>.Empty),
            CollectionCategories =
                PublicationTable<WorldCollectionCategoryStatus>.Create(CleanReports()),
            CollectedAtEpoch = 61,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
    }

    private static (Guid, WorldTypeModifierOwnerKind, string, string)[] SpellTypeRecords(
        string property) =>
        new[] { (Ember, WorldTypeModifierOwnerKind.SpellType, property, "ValueModifierRecord") };

    private static PublicationTable<WorldTypeModifier> Records(
        (Guid TypeId, WorldTypeModifierOwnerKind Kind, string Property, string RecordNativeType)[]
            records)
    {
        var rows = records
            .Select(record => new WorldTypeModifier(
                record.TypeId, record.Kind, record.Property, record.RecordNativeType, 0, 0))
            .ToArray();
        Array.Sort(rows, static (left, right) =>
        {
            var type = left.TypeId.CompareTo(right.TypeId);
            return type != 0 ? type : string.CompareOrdinal(left.Property, right.Property);
        });
        return PublicationTable<WorldTypeModifier>.Create(rows, rows.Length);
    }

    private static PublicationTable<WorldTypeModifierContribution> Contributions(
        params (Guid TypeId, string Property, GameValueModifierType Kind, double Amount,
            Guid Source)[] entries)
    {
        var rows = entries
            .Select((entry, index) => new WorldTypeModifierContribution(
                entry.TypeId,
                entry.Property,
                new WorldResearchRequirementAdjustment(
                    Guid.Parse("b0" + index.ToString("D6") + "-0000-4000-8000-000000000001"),
                    entry.Source,
                    "UpgradeSO",
                    (int)entry.Kind,
                    new BigDouble(entry.Amount),
                    0,
                    passive: false)))
            .ToArray();
        Array.Sort(rows, static (left, right) =>
        {
            var type = left.TypeId.CompareTo(right.TypeId);
            if (type != 0) return type;
            var property = string.CompareOrdinal(left.Property, right.Property);
            return property != 0
                ? property
                : left.Contribution.ModifierId.CompareTo(right.Contribution.ModifierId);
        });
        return PublicationTable<WorldTypeModifierContribution>.Create(rows, rows.Length);
    }

    private static WorldEntityKeyword[] Sorted(params WorldEntityKeyword[] rows)
    {
        Array.Sort(rows, static (left, right) =>
        {
            var owner = left.OwnerId.CompareTo(right.OwnerId);
            if (owner != 0) return owner;
            var source = ((int)left.Source).CompareTo((int)right.Source);
            return source != 0 ? source : left.Ordinal.CompareTo(right.Ordinal);
        });
        return rows;
    }

    /// <summary>
    /// The two flash records carry numbers on purpose: the point is that a captured magnitude the
    /// game cannot read is still not printed, which a pair of zeroes would not have shown.
    /// </summary>
    private static WorldSpellType SpellType(Guid id) =>
        new(
            id, 0, default, 0d, 0d, false, false, false, false, true, false,
            default, new BigDouble(150), new BigDouble(100), default, new BigDouble(80),
            default, default, new BigDouble(200), default, default, default, default, default,
            default, default, default, default, default, default, default,
            new BigDouble(35), new BigDouble(240));

    private static WorldCollectionCategoryStatus[] CleanReports() =>
        GameMcpWorldQuery.RegisteredCategoryNames().Concat(new[]
            {
                "plot-node-actions", "concept-instances", "plot-authoring",
                "crafting-recipe-state", "crafting-decisions", "consumable-inventory",
                "loadouts", "harvest-elements", "plot-actions", "action-queue-slots",
                            // The three type rosters whose wire name is not their collector's name.
                "harvest-types", "harvest-action-types", "consumable-families",
})
            .Distinct(StringComparer.Ordinal)
            .Select(name => new WorldCollectionCategoryStatus(
                name, WorldCategoryOutcome.Collected, 0, 0, string.Empty))
            .ToArray();
}
