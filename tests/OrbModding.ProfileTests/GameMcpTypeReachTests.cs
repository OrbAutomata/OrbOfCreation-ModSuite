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
/// The nine taxonomies whose type assets the world walked but never published now answer to their
/// own id, so the worth block reaches the classes it was designed around.
/// </summary>
/// <remarks>
/// The design's worked example is a structure type — "is Workshop worth investing in" — and until
/// these categories existed a Workshop id resolved to nothing at all. The block itself is unchanged;
/// what changed is that there is now a page for it to sit on.
/// </remarks>
public sealed class GameMcpTypeReachTests
{
    private static readonly Guid Workshop = Guid.Parse("c0a00000-0000-4000-8000-000000000001");
    private static readonly Guid Primal = Guid.Parse("c0b00000-0000-4000-8000-000000000001");
    private static readonly Guid Arcanist = Guid.Parse("c0c00000-0000-4000-8000-000000000001");
    private static readonly Guid Forge = Guid.Parse("c0d00000-0000-4000-8000-000000000001");
    private static readonly Guid Anvil = Guid.Parse("c0e00000-0000-4000-8000-000000000001");
    private static readonly Guid Font = Guid.Parse("c0f00000-0000-4000-8000-000000000001");
    private static readonly Guid Insight = Guid.Parse("c1a00000-0000-4000-8000-000000000001");
    private static readonly Guid Study = Guid.Parse("c1b00000-0000-4000-8000-000000000001");
    private static readonly Guid Garden = Guid.Parse("c1c00000-0000-4000-8000-000000000001");
    private static readonly Guid Aura = Guid.Parse("c1d00000-0000-4000-8000-000000000001");
    private static readonly Guid Technology = Guid.Parse("c1e00000-0000-4000-8000-000000000001");
    private static readonly Guid Metallurgy = Guid.Parse("c1f00000-0000-4000-8000-000000000001");

    /// <summary>
    /// The whole Workshop page, line for line. <c>structurePower</c> is hand-computed off the pinned
    /// fold: seed 100, Raw +40 to 140, then one MultiDiminishing multiply of 1 + 0.25, which is 175.
    /// </summary>
    /// <remarks>
    /// The <c>row</c> is every scalar the class stores and no more: each of its thirteen records
    /// distributes into its structures, so their magnitudes are on the worth block below and appear
    /// as no column here.
    /// </remarks>
    [Fact]
    public void The_worked_example_answers_what_a_structure_type_is_worth()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "uuid: c0a000",
                "name: Workshop",
                "internalName: workshopStructures",
                "category: structure-types",
                "row: baseEffectLevel=1, baseBuildTime=12, overrideRankDefault=no, " +
                "overrideRank=0",
                "worth:",
                "  howToRead: These totals are already inside each member's own numbers: read them " +
                "to compare types, and never multiply one into a member.",
                "  members 1",
                "  [kind | count]",
                "  structures | 2",
                "  properties 1:",
                "    property: Power",
                "    distributedTotalPercent: 175",
                "    sources 2",
                "    [amount | effect | order | source]",
                "    40 | raw | 0 | Deep Insight c1a000",
                "    0.25 | diminishing | 0 | Focused Study c1b000",
                "  unmodified: Develop Speed",
            }),
            Render(Detail(Workshop)));
    }

    /// <summary>
    /// A parent structure type confers its records on its children, so the count of what a bonus on
    /// it reaches is the closure and not the parent's own members. Primal itself is worn by nothing;
    /// Arcanist's one structure is what it reaches.
    /// </summary>
    [Fact]
    public void A_parent_structure_type_answers_for_the_members_of_its_children()
    {
        var worth = Detail(Primal)["worth"]!;
        var members = Assert.Single(worth["members"]!.Values<JObject>())!;

        Assert.Equal("structures", (string?)members["kind"]);
        Assert.Equal(1, (int?)members["count"]);
    }

    /// <summary>
    /// A type asset in one of the nine taxonomies is a row of its own category, reachable by id, and
    /// the categories are declared with the same descriptor every other category has.
    /// </summary>
    [Theory]
    [InlineData("structure-types", "StructureTypeSO")]
    [InlineData("ritual-types", "RitualTypeSO")]
    [InlineData("agromancy-element-types", "HarvestTypeSO")]
    [InlineData("plot-node-types", "PlotNodeTypeSO")]
    [InlineData("research-types", "ResearchTypeSO")]
    [InlineData("consumable-types", "ConsumableTypeSO")]
    [InlineData("plot-node-action-types", "HarvestActionTypeSO")]
    [InlineData("passive-ability-types", "PassiveAbilityTypeSO")]
    [InlineData("time-rune-types", "TimeRuneTypeSO")]
    public void Every_taxonomy_the_worth_block_covers_is_a_category_of_its_own(
        string category,
        string nativeType)
    {
        Assert.Contains(category, GameMcpWorldQuery.RegisteredCategoryNames());
        Assert.Equal(nativeType, GameMcpEntityCapabilityMap.ExpectedNativeType(category));
        Assert.True(GameMcpEntityCapabilityMap.TryCategoryForNativeType(nativeType, out var found));
        Assert.Equal(category, found);
    }

    /// <summary>
    /// A taxonomy whose class holds a value record answers with the number that record holds, the
    /// same way an alchemy type's level does. Without the row the number lives on, the block would
    /// print a property with sources and no magnitude, which reads as a distributor whose total went
    /// missing.
    /// </summary>
    /// <remarks>
    /// The whole page, beside Workshop's: Workshop's records print "Develop Speed" and "Power"
    /// because the game authors those words, and this one prints <c>level</c> because the game
    /// authors none for it. The honest internal name is the answer, not a word invented to match.
    /// </remarks>
    [Fact]
    public void A_value_record_on_one_of_the_nine_answers_with_its_own_number()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "uuid: c1c000",
                "name: Garden",
                "internalName: gardenHarvestType",
                "category: agromancy-element-types",
                "row: level=6",
                "worth:",
                "  howToRead: These are this type's own numbers, and they apply on top of whatever " +
                "wears the type.",
                "  properties 1",
                "  [property | value]",
                "  level | 6",
            }),
            Render(Detail(Garden)));
    }

    /// <summary>
    /// The new categories are listable like any other, so a reader finds a type id by paging rather
    /// than by knowing it.
    /// </summary>
    [Fact]
    public void A_structure_type_page_lists_the_types_by_the_name_the_game_prints()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 3/3",
                "[id | name | baseEffectLevel]",
                "c0a000 | Workshop | 1",
                "c0b000 | Primal | 0",
                "c0c000 | Arcanist | 0",
            }),
            Render(Json(GameMcpWorldQuery.ListRows(
                Context(World()), "structure-types", 0, 50, limitFromCaller: false))));
    }

    /// <summary>
    /// The only thing search learns from nine new categories is nine new places to find a hit. A
    /// type answers in the same five things every other hit says, under its own category, and the
    /// page's shape is the shape it already had.
    /// </summary>
    [Fact]
    public void Search_finds_the_new_types_and_says_nothing_new_about_them()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 5/5",
                "[id | name | category | keywords | matchedOn]",
                "c0c000 | Arcanist | structure-types | - | name",
                "c1c000 | Garden | agromancy-element-types | - | name",
                "c1e000 | Technology | research-types | - | internalName",
                "c0f000 | Font | structures | Arcanist | keywords",
                "c1f000 | Metallurgy | research | Technology | category",
            }),
            Render(Json(GameMcpWorldQuery.Search(
                Context(World()), "ar", 0, 50, string.Empty, string.Empty, string.Empty,
                limitFromCaller: false))));
    }

    /// <summary>
    /// A research type names its members like its sibling taxonomies do. The edge is
    /// <c>ResearchSO.researchTypes</c> — the list <c>GetBaseDisplayType()</c> joins into the word
    /// line the game prints — published inside the research category because each type's investment
    /// levels ride there rather than in the keyword table.
    /// </summary>
    [Fact]
    public void A_research_type_names_its_members_like_every_other_taxonomy()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "uuid: c1e000",
                "name: Technology",
                "internalName: technologyResearchType",
                "category: research-types",
                "row:",
                "  linkedDevelopCost: no",
                "  linkedResourceCost: no",
                "  linkedResearchTime: no",
                "  ignoreWhenLevelDependent: no",
                "  persistThroughReset: no",
                "  cachedTotalLevel: 0",
                "  cachedPeakLevel: 0",
                "  cachedQueuedLevel: 0",
                "  cachedDevelopingLevel: 0",
                "  cachedQueuedValue: 0",
                "  cachedInvestmentLevel: 0",
                "  cachedPurchasedLevel: 0",
                "  freeBonusLevels: 0",
                "  usedBonusLevels: 0",
                "  maxInvestmentLevel: 0",
                "worth:",
                "  howToRead: These totals are already inside each member's own numbers: read them " +
                "to compare types, and never multiply one into a member.",
                "  members 1",
                "  [kind | count]",
                "  research | 1",
                "  properties 1:",
                "    property: Requirements",
                "    distributedTotalPercent: 130",
                "    sources 1",
                "    [amount | effect | order | source]",
                "    30 | raw | 0 | Deep Insight c1a000",
            }),
            Render(Detail(Technology)));
    }

    [Fact]
    public void A_type_the_game_stores_nothing_else_for_answers_with_its_worth_alone()
    {
        Assert.Null(Detail(Aura)["row"]);
        Assert.Equal(
            string.Join('\n', new[]
            {
                "uuid: c1d000",
                "name: Aura",
                "internalName: auraPassiveAbilityType",
                "category: passive-ability-types",
                // Nothing is loaded onto this type's one record, so the whole block is the one
                // line that says which record exists and that nothing modifies it. There is no
                // magnitude on the page, so there is no reading rule to give for one.
                "worth: unmodified=[Cooldown]",
            }),
            Render(Detail(Aura)));
        Assert.Equal(
            "rows: Aura c1d000",
            Render(Json(GameMcpWorldQuery.ListRows(
                Context(World()), "passive-ability-types", 0, 50, limitFromCaller: false))));
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
            new EntityIdentityName(Workshop, "StructureTypeSO", "Workshop", "workshopStructures"),
            new EntityIdentityName(Primal, "StructureTypeSO", "Primal", "primalStructures"),
            new EntityIdentityName(Arcanist, "StructureTypeSO", "Arcanist", "arcanistStructures"),
            new EntityIdentityName(Forge, "StructureSO", "Forge", "forge"),
            new EntityIdentityName(Anvil, "StructureSO", "Anvil", "anvil"),
            new EntityIdentityName(Font, "StructureSO", "Font", "font"),
            new EntityIdentityName(Insight, "UpgradeSO", "Deep Insight", "deepInsight"),
            new EntityIdentityName(Study, "ResearchSO", "Focused Study", "focusedStudy"),
            new EntityIdentityName(Garden, "HarvestTypeSO", "Garden", "gardenHarvestType"),
            new EntityIdentityName(
                Aura, "PassiveAbilityTypeSO", "Aura", "auraPassiveAbilityType"),
            new EntityIdentityName(
                Technology, "ResearchTypeSO", "Technology", "technologyResearchType"),
            new EntityIdentityName(Metallurgy, "ResearchSO", "Metallurgy", "metallurgy"),
        });

    private static GameMcpFrameContext Context(GameWorldState world)
    {
        var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(905));
        return GameMcpTestHarness.Context(publisher.ReadLatest());
    }

    /// <summary>
    /// Three structure types — one worn by two structures, one parent, one child worn by a third —
    /// plus one agromancy element type carrying a value record, one passive ability type carrying
    /// nothing at all, and one research type worn by one research entry. Totals and membership come
    /// off the real derivers rather than being asserted into the fixture, and every entity a keyword
    /// row names is published as a row of its own category, because a member a page counts is a
    /// member a caller can walk to.
    /// </summary>
    private static GameWorldState World()
    {
        var records = Records(
            (Workshop, WorldTypeModifierOwnerKind.StructureType, "structurePower",
                "OrderedMultiplierRecord"),
            (Workshop, WorldTypeModifierOwnerKind.StructureType, "buildSpeedMod",
                "OrderedMultiplierRecord"),
            (Primal, WorldTypeModifierOwnerKind.StructureType, "structurePower",
                "MergingModifierRecord"),
            (Garden, WorldTypeModifierOwnerKind.HarvestType, "level", "ValueModifierRecord"),
            (Aura, WorldTypeModifierOwnerKind.PassiveAbilityType, "cooldown",
                "OrderedMultiplierRecord"),
            (Technology, WorldTypeModifierOwnerKind.ResearchType, "levelRequirementAdjust",
                "ModifierRecord"));

        var contributions = Contributions(
            (Workshop, "structurePower", GameValueModifierType.Raw, 40d, Insight),
            (Workshop, "structurePower", GameValueModifierType.MultiDiminishing, 0.25d, Study),
            (Primal, "structurePower", GameValueModifierType.Raw, 10d, Insight),
            (Technology, "levelRequirementAdjust", GameValueModifierType.Raw, 30d, Insight));

        var keywords = Keywords(
            new WorldEntityKeyword(
                Forge, WorldKeywordOwnerKind.Structure, WorldKeywordSource.PrimaryType, 0, Workshop),
            new WorldEntityKeyword(
                Anvil, WorldKeywordOwnerKind.Structure, WorldKeywordSource.PrimaryType, 0, Workshop),
            new WorldEntityKeyword(
                Font, WorldKeywordOwnerKind.Structure, WorldKeywordSource.PrimaryType, 0, Arcanist));

        var subtypes = PublicationTable<WorldTypeSubtype>.Create(new[]
        {
            new WorldTypeSubtype(Primal, 0, Arcanist),
        });
        var research = PublicationTable<WorldResearch>.Create(new[] { Research(Metallurgy, Technology) });
        var totals = WorldTypeModifierTotalDeriver.Build(records, contributions);

        return new GameWorldState
        {
            EntityIdentities = Catalog,
            Structures = PublicationTable<WorldStructure>.Create(new[]
            {
                Structure(Forge), Structure(Anvil), Structure(Font),
            }),
            Research = research,
            ResearchTypes = PublicationTable<WorldResearchType>.Create(new[]
            {
                new WorldResearchType(
                    Technology, false, false, false, false, false, 0, 0, 0, 0, 0, 0, 0,
                    BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            }),
            StructureTypes = PublicationTable<WorldStructureType>.Create(Sorted(
                new WorldStructureType(Workshop, 1, 12d, overrideRankDefault: false, 0),
                new WorldStructureType(Primal, 0, 0d, overrideRankDefault: true, 3),
                new WorldStructureType(Arcanist, 0, 0d, overrideRankDefault: false, 0))),
            HarvestTypes = PublicationTable<WorldHarvestType>.Create(new[]
            {
                new WorldHarvestType(Garden, new BigDouble(6)),
            }),
            PassiveAbilityTypes = PublicationTable<WorldPassiveAbilityType>.Create(new[]
            {
                new WorldPassiveAbilityType(Aura),
            }),
            EntityKeywords = keywords,
            TypeModifiers = records,
            TypeModifierContributions = contributions,
            TypeModifierTotals = totals,
            TypeSubtypes = subtypes,
            KeywordModifiers = WorldKeywordModifierDeriver.Build(
                totals, keywords, research, subtypes),
            CollectionCategories =
                PublicationTable<WorldCollectionCategoryStatus>.Create(CleanReports()),
            CollectedAtEpoch = 62,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
    }

    /// <summary>One published structure row: the member a keyword count is a count of.</summary>
    private static WorldStructure Structure(Guid id)
    {
        var modifiers = new RawStructureModifiers(
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero);
        var reading = new RawStructureSample(
            id,
            Guid.Empty,
            BigDouble.Zero,
            BigDouble.Zero,
            unlocked: true,
            queuedEchos: 0,
            completedEchos: 0,
            selfBonusLevels: 0,
            queueTimeLeft: BigDouble.Zero,
            currentBuildTime: BigDouble.Zero,
            flagged: false,
            baseLevel: 0,
            queueTimeTotal: 0,
            debugStructure: false,
            disabled: false,
            observableId: 0,
            insufficientReqPenaltyActive: false,
            bufferDevelopedQuantity: 0,
            costPerQuantityId: Guid.Empty,
            in modifiers);
        return new WorldStructure(
            in reading, BigDouble.Zero, hasWorkInFlight: false, BigDouble.Zero,
            developmentProgress: 0);
    }

    /// <summary>
    /// One research entry wearing one research type, which is where that membership is published:
    /// the keyword table leaves the class out because the research row already carries each type's
    /// investment levels beside it.
    /// </summary>
    private static WorldResearch Research(Guid id, params Guid[] types) => new(
        id, 0, 0, 0, 0, 0, 0d, false, false, false, false, false, false, false, false, false,
        false, false, false, 0, 0, 0, 0, 0, false, 0, 0, BigDouble.Zero, 0, 0,
        PublicationTable<WorldResearchRequirementAdjustment>.Empty,
        default,
        new WorldResearchDecision(
            queueMode: false,
            multiBuy: 0,
            queuedLevels: 0,
            levelsAvailable: 0,
            currentInvestmentLevel: 0,
            currentTime: BigDouble.Zero,
            remainingTime: BigDouble.Zero,
            timeRatio: BigDouble.Zero,
            canApplyBonusLevel: false,
            freeBonusLevels: 0,
            developmentCostAffordable: false,
            PublicationTable<WorldResearchCost>.Empty,
            PublicationTable<WorldResearchInvestment>.Empty,
            PublicationTable<WorldResearchTypeDecision>.Create(
                types.Select(type => new WorldResearchTypeDecision(type, 0, 0, 0)).ToArray())));

    private static WorldStructureType[] Sorted(params WorldStructureType[] rows)
    {
        Array.Sort(rows, static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return rows;
    }

    private static WorldEntityKeyword[] SortedKeywords(params WorldEntityKeyword[] rows)
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

    private static PublicationTable<WorldEntityKeyword> Keywords(params WorldEntityKeyword[] rows) =>
        PublicationTable<WorldEntityKeyword>.Create(SortedKeywords(rows));

    private static PublicationTable<WorldTypeModifier> Records(
        params (Guid TypeId, WorldTypeModifierOwnerKind Kind, string Property,
            string RecordNativeType)[] records)
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
                    Guid.Parse("c2" + index.ToString("D6") + "-0000-4000-8000-000000000001"),
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
