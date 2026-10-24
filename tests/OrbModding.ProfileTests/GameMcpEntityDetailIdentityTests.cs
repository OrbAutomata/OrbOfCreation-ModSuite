using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;
using static OrbModding.ProfileTests.GameMcpEntityDetailFixture;

namespace OrbModding.ProfileTests;

[Collection(NativeRegistryCollection.Name)]
public sealed class GameMcpEntityDetailIdentityTests : IDisposable
{
    public GameMcpEntityDetailIdentityTests() => ClearRegistries();

    public void Dispose() => ClearRegistries();

    [Fact]
    public void ExplanationSeparatesUnknownFromKnownButUnprojectedIdentity()
    {
        var known = Guid.Parse("b4505524-ad2f-4a5a-9d28-df0c30937748");
        var unknown = Guid.Parse("00000000-0000-4000-8000-000000000099");
        var world = new GameWorldState
        {
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var context = GameMcpTestHarness.Context(world, generation: 911);

        var knownResult = GameMcpTestHarness.Detail(context, known);
        var unknownResult = GameMcpTestHarness.Detail(context, unknown);

        Assert.Equal("ERR_NOT_FOUND", (string?)knownResult["reasonCode"]);
        // The catalog knows this id and the game authors no word for it, so the block names it by
        // the only label there is — in the `name` cell, once, with no second line repeating it.
        Assert.Equal("InventoryUnlocked", (string?)knownResult["name"]);
        Assert.Null(knownResult["internalName"]);
        // A prerequisite link is this build's own machinery, and the block points nowhere: a remedy
        // names a verb that will answer, there is none for an id no published row covers, and the
        // identity such a remedy could promise is on this block already.
        Assert.Null(knownResult["readWith"]);
        Assert.Contains("internal machinery", (string?)knownResult["reason"]);
        Assert.Null(knownResult["nameEvidence"]);
        Assert.Equal("ERR_NOT_FOUND", (string?)unknownResult["reasonCode"]);
        // The name is the missing thing, so a name search is the one remedy that cannot work.
        Assert.Equal("world_categories", (string?)unknownResult["readWith"]!["tool"]);
        Assert.DoesNotContain("entity_catalog", (string?)unknownResult["reason"]);
        Assert.DoesNotContain("entity_catalog", (string?)knownResult["reason"]);
        // Nothing carries this id, so the block hands it back whole and names nothing. It used to
        // shorten the id to a handle — an address into a published set this id is not in — and add
        // `name: (unnamed 000000)`, which reads as a row whose name went missing rather than as an
        // id with no row at all.
        Assert.Equal(unknown.ToString("D"), (string?)unknownResult["uuid"]);
        Assert.Null(unknownResult["name"]);
        Assert.Null(unknownResult["nameEvidence"]);
        // The known-but-unprojected block is the other case and keeps both: the catalog really does
        // hold that name, and the id really is one this build published.
        Assert.Equal(known.ToString("D").Substring(0, 6), (string?)knownResult["uuid"]);
    }

    /// <summary>
    /// The legacy Brewing Station, on the one arm an unprojected id has left. It is machinery — the
    /// game builds no instance of it — so the block names no verb to follow and says what is true
    /// of the id instead: its identity is the whole of what there is to read, and it is right here.
    /// </summary>
    [Fact]
    public void TheLegacyStationTakesTheMachineryArmNowThatItIsMachinery()
    {
        var station = Guid.Parse("d76565b1-8e2b-44fe-9cf3-995d6f666305");
        var world = new GameWorldState
        {
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var result = GameMcpTestHarness.Detail(
            GameMcpTestHarness.Context(world, generation: 913), station);

        Assert.Equal("ERR_NOT_FOUND", (string?)result["reasonCode"]);
        Assert.Equal("BrewingStation", (string?)result["name"]);
        Assert.Null(result["internalName"]);
        Assert.Null(result["readWith"]);
        Assert.Contains("internal machinery", (string?)result["reason"]);
    }

    /// <remarks>
    /// An equipped spell instance is a runtime object, not a loaded asset, so the asset catalog
    /// does not know it — and the explanation answered "nothing in this process knows this UUID"
    /// and pointed at that same catalog. The world had published the UUID inside a spell-slot row,
    /// so both the claim and the remedy were wrong.
    /// </remarks>
    [Fact]
    public void A_runtime_member_of_a_published_row_is_answered_with_the_row_that_owns_it()
    {
        var instance = Guid.Parse("4e551da3-262b-4bee-9f92-904cb81bf25a");
        var world = new GameWorldState
        {
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            SpellSlots = PublicationTable<WorldSpellSlot>.Create(new[]
            {
                new WorldSpellSlot(
                    0, instance, Guid.Empty, true, false, false, false, false,
                    false, false, false, false, false, 0, 0, BigDouble.Zero),
            }),
        };

        var result = GameMcpTestHarness.Detail(
            GameMcpTestHarness.Context(world, generation: 913),
            instance);

        Assert.Equal("ERR_NOT_FOUND", (string?)result["reasonCode"]);
        Assert.Equal("world_list", (string?)result["readWith"]!["tool"]);
        Assert.Equal("spell-slots", (string?)result["readWith"]!["category"]);
    }

    /// <summary>
    /// A description read that throws is a failed read, not an authored-empty description: the page
    /// says the text is unavailable and the fault reaches the suite log under a reference.
    /// </summary>
    [Fact]
    public void A_description_read_that_throws_says_so_and_names_the_fault()
    {
        var glyphId = Guid.Parse("168e3734-1ecb-4938-bd4a-d011ff13e202");
        var native = new global::GlyphSO { DisplayName = "Weak", ThrowOnGetDescription = true };
        native.SetGuid(glyphId);
        global::IdScriptableObject.RuntimeLookup[glyphId] = native;
        var logged = new List<string>();
        GameActionFaultLog.ConfigureLog(logged.Add);
        try
        {
            var world = new GameWorldState
            {
                AugmentGlyphs = PublicationTable<WorldGlyph>.Create(new[]
                {
                    new WorldGlyph(
                        glyphId, 0, 0, 1, false, true, false, false, false, false,
                        0, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
                }),
                CollectedAtEpoch = 38,
                CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            };

            var description = (string?)Explain(world, glyphId, 925)["description"];

            Assert.StartsWith("Description unavailable. Reference MCP-", description);
            Assert.Contains(logged, line => line.Contains("GetDescription failed", StringComparison.Ordinal));
        }
        finally
        {
            GameActionFaultLog.ConfigureLog(null);
        }
    }

    [Fact]
    public void AuthoredTooltipDescriptionLeadsDiscoverableExplanationWhenNativelyAvailable()
    {
        var glyphId = Guid.Parse("168e3734-1ecb-4938-bd4a-d011ff13e201");
        var native = new global::GlyphSO
        {
            DisplayName = "Weak",
            Description = "Reduces the strength of a spell glyph effect.",
        };
        native.SetGuid(glyphId);
        global::IdScriptableObject.RuntimeLookup[glyphId] = native;
        var world = new GameWorldState
        {
            AugmentGlyphs = PublicationTable<WorldGlyph>.Create(new[]
            {
                new WorldGlyph(
                    glyphId, 0, 0, 1, false, true, false, false, false, false,
                    0, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            }),
            CollectedAtEpoch = 38,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var result = Explain(world, glyphId, 924);

        Assert.Equal("Weak", (string?)result["name"]);
        Assert.Equal(
            "Reduces the strength of a spell glyph effect.",
            (string?)result["description"]);
        Assert.True(
            result.Properties().TakeWhile(property => property.Name != "description")
                .All(property => property.Name is "worldGeneration" or "status" or "uuid" or
                    "name" or "category" or "nativeType"));
        // Every predicate that applies ships, passing or not: dropping the passing ones would make
        // absence mean "true" on one key and "no such predicate" on the next.
        var predicates = Assert.IsType<JObject>(result["predicates"]);
        Assert.All(
            predicates.Properties(),
            predicate => Assert.Equal(
                JTokenType.Boolean, predicate.Value["available"]?.Type));
        Assert.False((bool)predicates["visible"]!["available"]!);

        // The discovery verdict is the row's, once. Native discoverability said yes while the
        // screen that would draw the press is not unlocked in this world — the round-14 shape,
        // where the coarser predicate survived precisely where it contradicted the row.
        Assert.Null(predicates["canDiscover"]);
        Assert.False((bool)result["row"]!["discover"]!["available"]!);
        Assert.Null(result["row"]!["discover"]!["reasonCode"]);
    }

    /// <summary>
    /// One call, three ids, three blocks in the order they were asked. The id nothing carries
    /// refuses on its own and takes neither of its neighbours down with it, and no block echoes an
    /// index back because the order is the correlation.
    /// </summary>
    [Fact]
    public void A_batch_answers_every_id_it_can_and_refuses_only_the_one_it_could_not()
    {
        var upgradeId = Guid.Parse("2442ff7c-5630-4f7f-ac8f-1ea5f3b7a7cc");
        var glyphId = Guid.Parse("cc1cb602-2427-41c3-a2f4-421b4eef2ab4");
        var missing = Guid.Parse("00000000-0000-4000-8000-0000000000aa");
        var rawUpgrade = new RawUpgradeSample(
            upgradeId,
            level: 1,
            maxLevel: 10,
            available: true,
            queuedLevels: 0,
            buildTime: BigDouble.Zero,
            developmentTime: 1,
            cachedCostLevel: 1);
        var world = new GameWorldState
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                GameWorldStateDeriver.Derive(in rawUpgrade),
            }),
            UpgradeListMemberships = GameMcpTestHarness.OnTheUpgradesList(upgradeId),
            AugmentGlyphs = PublicationTable<WorldGlyph>.Create(new[]
            {
                new WorldGlyph(
                    glyphId, 0, 0, 1, false, true, false, false, false, false,
                    0, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            }),
            CollectedAtEpoch = 51,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var response = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRows(
            GameMcpTestHarness.Context(world, 949),
            string.Empty,
            new[]
            {
                glyphId.ToString("D"), missing.ToString("D"), upgradeId.ToString("D"),
            }));
        var blocks = response["results"]!.Values<JObject>().ToArray();

        Assert.Equal(3, blocks.Length);
        Assert.DoesNotContain(
            "inputIndex", response.ToString(Newtonsoft.Json.Formatting.None),
            StringComparison.Ordinal);

        // Two categories in one call: neither block was told which table to look in.
        Assert.Equal("augment-glyphs", (string?)blocks[0]!["category"]);
        Assert.Equal("Accursed", (string?)blocks[0]!["name"]);
        Assert.NotNull(blocks[0]!["predicates"]);
        Assert.Equal("upgrades", (string?)blocks[2]!["category"]);
        Assert.Equal("Alchemist", (string?)blocks[2]!["name"]);
        Assert.Equal("available", (string?)blocks[2]!["row"]!["state"]);
        Assert.NotNull(blocks[2]!["requirements"]);

        Assert.Equal("unavailable", (string?)blocks[1]!["status"]);
        Assert.Equal("ERR_NOT_FOUND", (string?)blocks[1]!["reasonCode"]);
        // The one block in the batch that answers "nothing carries this" echoes the whole id the
        // caller sent and names nothing. A six-character stub of a thirty-six character argument is
        // not something a caller can match against what they typed, and the `(unnamed …)` name it
        // used to carry made an id with no row read as a row missing its name.
        Assert.Equal(missing.ToString("D"), (string?)blocks[1]!["uuid"]);
        Assert.Null(blocks[1]!["name"]);
        Assert.Equal("world_categories", (string?)blocks[1]!["readWith"]!["tool"]);
        Assert.Null(blocks[1]!["row"]);
        Assert.Null(blocks[1]!["predicates"]);
    }

    /// <summary>
    /// An id carries its own table, so a caller holding one from a search, a refusal or an action
    /// response reads it without first learning where it lives — and gets the same block either way.
    /// </summary>
    [Fact]
    public void An_id_resolves_its_own_table_and_naming_that_table_changes_nothing()
    {
        var glyphId = Guid.Parse("cc1cb602-2427-41c3-a2f4-421b4eef2ab4");
        var world = new GameWorldState
        {
            AugmentGlyphs = PublicationTable<WorldGlyph>.Create(new[]
            {
                new WorldGlyph(
                    glyphId, 0, 0, 1, false, true, false, false, false, false,
                    0, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "augment glyphs", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
                new WorldCollectionCategoryStatus(
                    "upgrades", WorldCategoryOutcome.Collected, 0, 0, string.Empty),
            }),
            CollectedAtEpoch = 52,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var state = GameMcpTestHarness.Context(world, 950);

        var resolved = GameMcpTestHarness.Detail(state, glyphId);
        var addressed = GameMcpTestHarness.Detail(state, "augment-glyphs", glyphId);

        Assert.Equal(
            resolved.ToString(Newtonsoft.Json.Formatting.None),
            addressed.ToString(Newtonsoft.Json.Formatting.None));
        Assert.Equal("augment-glyphs", (string?)resolved["category"]);

        // A table the id is not in is the caller insisting, and it is answered as a miss in that
        // table rather than quietly resolved into the one they did not name.
        var elsewhere = GameMcpTestHarness.Detail(state, "upgrades", glyphId);
        Assert.Equal("unavailable", (string?)elsewhere["status"]);
        Assert.Equal("ERR_NOT_FOUND", (string?)elsewhere["reasonCode"]);
        Assert.Equal("upgrades", (string?)elsewhere["readWith"]!["category"]);
    }

    /// <summary>
    /// The round's costliest miss. A glyph's detail block published state, discovery, visibility
    /// and price and never said `keywords: Elemental` — the fact that decides which family the
    /// glyph belongs to and therefore which page renders it. A reader working from that block
    /// concluded the read surface contradicted the screen and held the wrong finding for two hours,
    /// while the search row ten minutes earlier had carried the word plainly. One fact, one name,
    /// both verbs.
    /// </summary>
    [Fact]
    public void A_detail_block_carries_the_same_keywords_line_the_search_row_prints()
    {
        var glyphId = Guid.Parse("cc1cb602-2427-41c3-a2f4-421b4eef2ab4");
        var elemental = Guid.Parse("61ee89dd-f896-4863-b3ff-1d07e7cf8896");
        var augment = Guid.Parse("12eb2437-5bd7-4069-b02a-e6f1eee8f0c6");
        var world = new GameWorldState
        {
            AugmentGlyphs = PublicationTable<WorldGlyph>.Create(new[]
            {
                new WorldGlyph(
                    glyphId, 0, 0, 1, false, true, false, false, false, false,
                    0, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            }),
            EntityKeywords = PublicationTable<WorldEntityKeyword>.Create(new[]
            {
                new WorldEntityKeyword(
                    glyphId, WorldKeywordOwnerKind.Glyph,
                    WorldKeywordSource.PrimaryType, 0, elemental),
                new WorldEntityKeyword(
                    glyphId, WorldKeywordOwnerKind.Glyph,
                    WorldKeywordSource.TypeList, 0, augment),
            }),
            CollectedAtEpoch = 44,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var block = Explain(world, glyphId, 944);

        Assert.Equal("Accursed", (string?)block["name"]);
        Assert.Equal("Elemental, Spell Augment", (string?)block["keywords"]);
    }

    /// <summary>
    /// A detail page publishes the game's own words for the thing it describes. The description was
    /// reachable only through the evaluated-detail resolver, whose thirteen kinds are the set this
    /// build evaluates predicates for — a shared entry point, not a rule about descriptions — so a
    /// live round walked the agromancy-action and plot-node-action graphs to a detail page and found
    /// no description on either, though both native types carry one and the page already holds the
    /// type name its category declares.
    /// </summary>
    [Fact]
    public void AnActionDetailPageCarriesTheAuthoredDescriptionItsCategoryDeclaresTheTypeFor()
    {
        var mining = Guid.Parse("af40da52-4a75-420c-a88d-008c3f5fc443");
        var watering = Guid.Parse("0a1b2c3d-4e5f-4a6b-8c9d-0e1f2a3b4c5d");
        var action = new global::HarvestActionSO
        {
            displayName = "Mining",
            description = "Digs the node for ore.",
        };
        action.SetGuid(mining);
        global::IdScriptableObject.RuntimeLookup[mining] = action;
        var plotAction = new global::PlotNodeActionSO
        {
            displayName = "Water",
            description = "Waters the node so it grows.",
        };
        plotAction.SetGuid(watering);
        global::IdScriptableObject.RuntimeLookup[watering] = plotAction;

        var world = new GameWorldState
        {
            HarvestActions = PublicationTable<WorldHarvestAction>.Create(new[]
            {
                new WorldHarvestAction(
                    mining, new BigDouble(100), new BigDouble(100), new BigDouble(100)),
            }),
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(watering, "PlotNodeActionSO", "Water", "WaterPlotAction"),
                new EntityIdentityName(mining, "HarvestActionSO", "Mining", "MiningHarvestAction"),
            }.OrderBy(row => row.EntityId).ToArray()),
            CollectedAtEpoch = 71,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var page = Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            GameMcpWorldDetail.GetRows(
                    GameMcpTestHarness.Context(world),
                    string.Empty,
                    new[] { mining.ToString("D") })
                .Freeze(),
            world.EntityIdentities));

        Assert.Equal(
            "Digs the node for ore.",
            (string?)Assert.Single(page["results"]!.Values<JObject>())!["description"]);

        // The plot-node-action half of the same fix, at the read the page performs: the category
        // declares the native type, and the type is what the authored text is read through.
        Assert.Equal(
            "Waters the node so it grows.",
            GameMcpEntityIdentity.ReadDescription(
                watering, GameMcpEntityCapabilityMap.ExpectedNativeType("plot-node-actions")));
        Assert.Equal(
            "Digs the node for ore.",
            GameMcpEntityIdentity.ReadDescription(
                mining, GameMcpEntityCapabilityMap.ExpectedNativeType("agromancy-actions")));
    }
}
