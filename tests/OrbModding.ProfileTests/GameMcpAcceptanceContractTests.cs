using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpPublicationConsistencyTests
{
    [Fact]
    public void QueryRemainsPinnedWhenANewerWorldPublishes()
    {
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(GameMcpAcceptanceFixture.SpellWorld(3, 31), new WorldGeneration(1001));
        var pinned = GameMcpAcceptanceFixture.Snapshot(publisher.ReadLatest());
        publisher.Publish(GameMcpAcceptanceFixture.SpellWorld(9, 32), new WorldGeneration(1002));

        var result = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            pinned,
            "spell-recipes",
            GameMcpAcceptanceFixture.SpellId.ToString("D")));

        Assert.Null(result["worldGeneration"]);
        Assert.Null(result["lifecycleGeneration"]);
        Assert.Equal(3, (int)result["row"]!["masteryLevel"]!);
    }
}

/// <summary>
/// A refusal is read by an agent, so it says what went wrong in the words the game uses. The id it
/// was handed is already the argument the caller sent, and the batch form repeats it in its own
/// field; reciting thirty-six characters of GUID mid-sentence buried the part that was news.
/// </summary>
public sealed class GameMcpRefusalSentenceTests
{
    private static readonly Guid Absent = Guid.Parse("3f2a6c18-9b41-4f0e-8d77-1c5a2e6b90d4");

    [Fact]
    public void An_id_no_row_carries_is_refused_without_reciting_the_id()
    {
        var state = GameMcpAcceptanceFixture.SpellSnapshot(4);

        var result = GameMcpTestHarness.Json(
            GameMcpWorldQuery.GetRow(state, "spell-recipes", Absent.ToString("D")));

        Assert.Equal("ERR_NOT_FOUND", (string?)result["reasonCode"]);
        var reason = (string?)result["reason"] ?? string.Empty;
        Assert.DoesNotContain(Absent.ToString("D"), reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("spell-recipes", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_all_zero_id_is_refused_as_an_id_that_names_nothing()
    {
        var state = GameMcpAcceptanceFixture.SpellSnapshot(4);

        var result = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRows(
            state, "spell-recipes", new[] { Guid.Empty.ToString("D") }));

        var row = Assert.Single(result["results"]!.Values<JObject>())!;
        Assert.Equal("ERR_INPUT", (string?)row["reasonCode"]);
        Assert.Contains("names nothing", (string?)row["reason"]!, StringComparison.Ordinal);
    }
}

public sealed class GameMcpWorldQueryTests
{
    [Fact]
    public void ResourceRowsUseOnlyNamedPlayerFacingSpendableFacts()
    {
        var resourceId = Guid.Parse("eda26ca0-afcc-4fc3-9d8a-eb279123353d");
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            resourceId,
            new BigDouble(5d, 24),
            new BigDouble(8d, 26),
            visible: true,
            lifetimeQuantity: new BigDouble(1d, 28),
            discoveryTime: BigDouble.Zero,
            quality: new BigDouble(100d),
            gainRate: new BigDouble(100d),
            drain: BigDouble.Zero,
            reservation: BigDouble.Zero,
            usage: BigDouble.Zero,
            inLossMode: false,
            inRestMode: true,
            inRallyMode: false,
            appliedLevels: 0,
            levelVariableId: Guid.Empty,
            in rateInputs,
            in traits,
            in modifiers);
        var resource = new WorldResource(
            in reading,
            isCapped: true,
            headroom: new BigDouble(7.5d, 26),
            fillFraction: 0.00625d,
            isAtCapacity: false,
            trueQuantity: new BigDouble(5.63d, 24),
            trueRate: new BigDouble(1.4d, 21));
        var world = new GameWorldState
        {
            Resources = PublicationTable<WorldResource>.Create(new[] { resource }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    new WorldCollectionCategoryStatus(
                        "resources", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
                }),
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRows(
            GameMcpTestHarness.Context(world, generation: 1003),
            "resources",
            new[] { resourceId.ToString("D") }));
        var block = Assert.Single(response["results"]!.Values<JObject>())!;
        var row = block["row"]!;

        // Identity is said once, on the block, and the row under it carries only its own columns.
        // The row used to repeat the same handle, name and category the block already published.
        // `nativeType` is gone from a block a category names: `resources` is one native class and
        // `world_categories` publishes which, so the block said the same fact twice.
        Assert.Equal(
            new[] { "uuid", "name", "category", "row", "predicates" },
            block.Children<JProperty>().Select(property => property.Name));
        Assert.Equal(
            new[] { "amount", "capacity", "netRatePerSecond", "atCapacity" },
            row.Children<JProperty>().Select(property => property.Name));
        Assert.Equal("Knowledge", (string?)block["name"]);
        Assert.Equal("resources", (string?)block["category"]);
        Assert.Equal("5e24", (string?)row["amount"]);
        Assert.Equal("8e26", (string?)row["capacity"]);
        Assert.Equal("1.4e21", (string?)row["netRatePerSecond"]);
        Assert.False((bool)row["atCapacity"]!);
        Assert.Null(row["reading"]);
        Assert.Null(row["quantity"]);
        Assert.Null(row["trueQuantity"]);
        Assert.Null(row["rateInputs"]);
        Assert.Null(row["traits"]);
        Assert.Null(row["modifiers"]);
        // The detail read costs what the detail costs: identity said once and the decisions the
        // merge folded in, on top of the row a list page would have shown. It costs 26 bytes less
        // than it did for saying `nativeType: ResourceSO` beside a category that means exactly that.
        Assert.Equal(237, System.Text.Encoding.UTF8.GetByteCount(
            response.ToString(Newtonsoft.Json.Formatting.None)));

        var list = GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(world, generation: 1003),
            "resources",
            0,
            10));
        var listed = Assert.Single(list["rows"]!.Values<JObject>())!;
        Assert.Equal((string?)row["amount"], (string?)listed["amount"]);
        Assert.Equal("5e24", (string?)listed["amount"]);
    }

    [Fact]
    public void UncappedResourceNamesThatInsteadOfTheNativeNegativeCapacitySentinel()
    {
        var resourceId = Guid.Parse("67acd892-3260-47b7-aaca-23e49c5903d4");
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            resourceId,
            new BigDouble(5d, 24),
            new BigDouble(-9.48d, 9),
            visible: true,
            lifetimeQuantity: BigDouble.Zero,
            discoveryTime: BigDouble.Zero,
            quality: new BigDouble(100d),
            gainRate: new BigDouble(100d),
            drain: BigDouble.Zero,
            reservation: BigDouble.Zero,
            usage: BigDouble.Zero,
            inLossMode: false,
            inRestMode: true,
            inRallyMode: false,
            appliedLevels: 0,
            levelVariableId: Guid.Empty,
            in rateInputs,
            in traits,
            in modifiers);
        var resource = new WorldResource(
            in reading,
            isCapped: false,
            headroom: BigDouble.Zero,
            fillFraction: 0d,
            isAtCapacity: false,
            trueQuantity: new BigDouble(9.83d, 24),
            trueRate: BigDouble.Zero);
        var world = new GameWorldState
        {
            Resources = PublicationTable<WorldResource>.Create(new[] { resource }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    new WorldCollectionCategoryStatus(
                        "resources", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
                }),
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRows(
            GameMcpTestHarness.Context(world, generation: 1004),
            "resources",
            new[] { resourceId.ToString("D") }));
        var row = Assert.Single(response["results"]!.Values<JObject>())!["row"]!;

        Assert.Equal("5e24", (string?)row["amount"]);
        Assert.Equal("0", (string?)row["netRatePerSecond"]);

        // The native ceiling here is -9.48e9. Neither that number nor a plain `atCapacity: no`
        // belongs on a resource that cannot fill, and dropping the pair let a page of uncapped
        // resources read as a page whose reader was never told capacities exist.
        Assert.Equal("uncapped", (string?)row["capacity"]);
        Assert.Equal("uncapped", (string?)row["atCapacity"]);
    }

    [Fact]
    public void BoundedLevelsUseJsonCardinalsEvenWhenProjectedFromBigDouble()
    {
        var encoded = GameMcpDocumentJsonEncoder.Encode(
            new GameMcpObjectBuilder
            {
                ["rows"] = new GameMcpArrayBuilder(
                    new GameMcpObjectBuilder { ["effectiveLevel"] = 1 },
                    new GameMcpObjectBuilder
                    {
                        ["effectiveLevel"] = new GameMcpDomainValue(new BigDouble(1.57d, 2)),
                    }),
            }.Freeze(),
            GameMcpTestHarness.EntityCatalog);
        var rows = encoded["rows"]!.OfType<JObject>().ToArray();

        Assert.Equal(1, (int)rows[0]["effectiveLevel"]!);
        Assert.Equal(157, (int)rows[1]["effectiveLevel"]!);
    }

    [Fact]
    public void DomainProjectionFieldsNormalizeToOneWireDialect()
    {
        var uuid = Guid.Parse("eda26ca0-afcc-4fc3-9d8a-eb279123353d");
        var encoded = Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            new GameMcpObjectBuilder
            {
                ["entityId"] = uuid,
                ["mcpCategory"] = "resources",
                ["quantity"] = new GameMcpDomainValue(new BigDouble(2.5d, 3)),
                ["unlocked"] = true,
                ["outcome"] = "PostconditionFailed",
                ["execution"] = "OneShotQueue",
            }.Freeze(),
            GameMcpTestHarness.EntityCatalog));

        Assert.Equal(GameMcpTestHarness.Handle(uuid), (string?)encoded["uuid"]);
        Assert.Equal("Knowledge", (string?)encoded["name"]);
        Assert.Equal("resources", (string?)encoded["category"]);
        Assert.Equal("2.5e3", (string?)encoded["amount"]);
        Assert.True((bool)encoded["available"]!);
        Assert.Equal("postcondition_failed", (string?)encoded["outcome"]);
        Assert.Equal("one_shot_queue", (string?)encoded["execution"]);
        Assert.Null(encoded["entityId"]);
        Assert.Null(encoded["mcpCategory"]);
        Assert.Null(encoded["quantity"]);
        Assert.Null(encoded["unlocked"]);
    }

    [Fact]
    public void RecursiveWireAuditRejectsBareEntityUuidsAndLegacyIdentifierAliases()
    {
        var tree = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var resource = Guid.Parse("eda26ca0-afcc-4fc3-9d8a-eb279123353d");
        var weak = Guid.Parse("168e3734-1ecb-4938-bd4a-d011ff13e201");
        var magnified = Guid.Parse("b0387ddd-2bd8-4799-8cd0-f8c624458930");
        var improvedCasting = Guid.Parse("21628be0-4377-4b13-b28c-171ab29324bf");
        var encoded = GameMcpDocumentJsonEncoder.Encode(new GameMcpObjectBuilder
        {
            ["tree"] = new GameMcpObjectBuilder { ["entityId"] = tree },
            ["cost"] = new GameMcpObjectBuilder { ["resourceUuid"] = resource },
            ["offers"] = new GameMcpArrayBuilder(weak, magnified),
            ["implicated"] = new GameMcpObjectBuilder { ["ownerUuid"] = improvedCasting },
        }.Freeze(), GameMcpTestHarness.EntityCatalog);

        var banned = new HashSet<string>(StringComparer.Ordinal)
        {
            "entityId", "resourceUuid", "resourceId", "glyphId", "treeUuid",
            "offerUuid", "selectedUuid",
        };
        var document = Assert.IsType<JObject>(encoded);
        Assert.DoesNotContain(
            document.DescendantsAndSelf().OfType<JProperty>(),
            property => banned.Contains(property.Name));
        var references = document.DescendantsAndSelf()
            .OfType<JObject>()
            .Where(item => item["uuid"] is not null)
            .ToArray();
        Assert.Equal(5, references.Length);
        Assert.All(references, reference =>
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)reference["name"]));
            if (reference["internalName"] is JToken internalName)
            {
                Assert.NotEqual(
                    (string?)reference["name"],
                    (string?)internalName);
            }
        });
    }

    [Fact]
    public void OverviewIsCompactAndExactReadDerivesNativeType()
    {
        var state = GameMcpAcceptanceFixture.SpellSnapshot(4);
        var overview = GameMcpTestHarness.Json(GameMcpWorldQuery.Overview(state));
        Assert.Equal("available", (string?)overview["status"]);
        Assert.NotNull(overview["economy"]);
        Assert.NotNull(overview["progression"]);
        Assert.NotNull(overview["running"]);
        Assert.Null(overview["detailCategories"]);
        Assert.Null(overview["unlocks"]);
        Assert.Null(overview["harvest"]);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(
            overview.ToString(Newtonsoft.Json.Formatting.None)) < 1_650);

        var exact = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            state,
            "spell-recipes",
            GameMcpAcceptanceFixture.SpellId.ToString("D")));
        Assert.Equal("available", (string?)exact["status"]);
        Assert.Null(exact["expectedNativeType"]);
        Assert.Equal(4, (int)exact["row"]!["masteryLevel"]!);
    }

    [Fact]
    public void ListIsLeanAndGetCarriesTheCuratedDecisionDetail()
    {
        var state = GameMcpAcceptanceFixture.SpellSnapshot(4);

        var list = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(state, "spell-recipes", 0, 10));
        var scan = Assert.Single(list["rows"]!.Values<JObject>())!;
        Assert.Equal(GameMcpTestHarness.Handle(GameMcpAcceptanceFixture.SpellId), (string?)scan["uuid"]);
        Assert.Null(scan["nameEvidence"]);
        Assert.Equal(4, (int)scan["masteryLevel"]!);
        Assert.Null(scan["spellPowerMod"]);
        Assert.Null(scan["category"]);
        Assert.Null(scan["loadoutAdd"]);
        Assert.Equal(99, System.Text.Encoding.UTF8.GetByteCount(
            list.ToString(Newtonsoft.Json.Formatting.None)));

        var reportNames = GameMcpWorldQuery.RegisteredCategoryNames().Concat(new[]
            {
                "plot-node-actions", "concept-instances", "plot-authoring",
                "crafting-recipe-state", "crafting-decisions", "consumable-inventory",
                "loadouts", "harvest-elements", "harvest-actions", "plot-actions",
                "action-queue-slots",
                            // The three type rosters whose wire name is not their collector's name.
                "harvest-types", "harvest-action-types", "consumable-families",
})
            .Distinct(StringComparer.Ordinal)
            .Select(name => new WorldCollectionCategoryStatus(
                name, WorldCategoryOutcome.Collected, 0, 0, string.Empty))
            .ToArray();
        var searchState = GameMcpAcceptanceFixture.Snapshot(
            GameMcpAcceptanceFixture.SpellWorld(4, 30) with
            {
                CollectionCategories =
                    PublicationTable<WorldCollectionCategoryStatus>.Create(reportNames),
            });
        var search = GameMcpTestHarness.Json(GameMcpWorldQuery.Search(
            searchState,
            GameMcpAcceptanceFixture.SpellId.ToString("D"),
            0,
            5));
        // A match names the entity, the category that reads the rest of it, and the words the game
        // prints on it. Borrowing each category's scan columns unioned every category's headings
        // onto one page and left nine cells in ten empty, on the tool whose whole job is routing
        // the caller to the right read.
        var match = Assert.Single(search["rows"]!.Values<JObject>())!;
        Assert.Equal((string?)scan["uuid"], (string?)match["uuid"]);
        Assert.Equal((string?)scan["name"], (string?)match["name"]);
        Assert.Equal("spell-recipes", (string?)match["category"]);
        Assert.Equal("id", (string?)match["matchedOn"]);
        Assert.Equal(5, match.Properties().Count());

        var exact = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            state,
            "spell-recipes",
            GameMcpAcceptanceFixture.SpellId.ToString("D")));
        Assert.Null(exact["row"]!["spellPowerMod"]);
        Assert.Equal("spell-recipes", (string?)exact["row"]!["category"]);
        Assert.NotNull(exact["row"]!["loadoutAdd"]);
    }
}

public sealed class GameMcpActionAdmissionTests
{
    [Fact]
    public void ActionAdmissionHasNoWorldGenerationGate()
    {
        var command = GameMcpAcceptanceFixture.NativeCommand();
        Assert.False(GameMcpNativeActionAdmission.TryReject(
            command,
            currentLifecycleGeneration: command.ExpectedLifecycleGeneration,
            currentConfigurationGeneration: command.ExpectedConfigurationGeneration,
            emergencyStopEngaged: false,
            out _));
        Assert.DoesNotContain(
            typeof(GameMcpCommand).GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            property => property.Name.Contains("WorldGeneration", StringComparison.Ordinal));
    }

    [Fact]
    public void LifecycleConfigurationAndEmergencyStopRemainLiveGates()
    {
        var command = GameMcpAcceptanceFixture.NativeCommand();
        Assert.True(GameMcpNativeActionAdmission.TryReject(
            command,
            command.ExpectedLifecycleGeneration + 1,
            command.ExpectedConfigurationGeneration,
            false,
            out var lifecycle));
        Assert.Equal("lifecycle_replaced", lifecycle.Code);

        Assert.True(GameMcpNativeActionAdmission.TryReject(
            command,
            command.ExpectedLifecycleGeneration,
            command.ExpectedConfigurationGeneration,
            true,
            out var stop));
        Assert.Equal("emergency_stop", stop.Code);
    }
}

public sealed class GameMcpInlineCompletionTests
{
    [Fact]
    public void FinalGameplayCompletionIsFlatOnlyAfterObservedPostStateIsAttached()
    {
        var command = GameMcpAcceptanceFixture.NativeCommand();
        var evidence = ServiceNativeMutationEvidence.Observed(
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1));
        var native = ServiceActionResult.Committed(CommonActionResultCodes.Committed, evidence);
        var terminal = GameMcpCommandResult.FromAction(
            in native,
            GameMcpCommandKind.Purchase,
            12,
            7).WithDetails(new GameMcpObjectBuilder
            {
                ["level"] = 4,
                ["available"] = false,
            }.Freeze());
        var projected = GameMcpTestHarness.Json(terminal.Project(command));
        Assert.Equal("committed", (string?)projected["status"]);
        Assert.Null(projected["code"]);
        Assert.Equal(new[] { "status", "level", "available" },
            projected.Properties().Select(property => property.Name));
        Assert.Null(projected["receiptId"]);
    }

    [Fact]
    public void InboxClaimsEveryAcceptedOperationWithoutAnArbitraryCapacity()
    {
        var operations = new GameMcpFrameInbox();
        for (var index = 0; index < 128; index++)
            GameMcpAcceptanceFixture.SubmitHarvest(operations);

        Assert.Equal(128, operations.ClaimPending().Length);
        Assert.Empty(operations.ClaimPending());
    }
}

public sealed class GameMcpProtocolSurfaceTests
{
    [Fact]
    public void DiscoveryHasNoReceiptToolAndExposesGenericVisualTools()
    {
        var names = GameMcpAcceptanceFixture.ToolNames();
        Assert.DoesNotContain("action_receipt", names);
        Assert.DoesNotContain("decision_journal", names);
        Assert.Contains("trace_health", names);
        Assert.Contains("game_screen_catalog", names);
        Assert.Contains("game_navigate", names);
        Assert.Contains("game_screen_elements", names);
        Assert.Contains("game_tooltip", names);
        Assert.Contains("game_screenshot", names);

        // A retired name stays retired: no alias, no tombstone verb, no second door onto
        // the detail read. The tool list simply stops carrying it.
        Assert.DoesNotContain("explain_entity", names);
        Assert.Contains("world_get", names);
    }

    /// <summary>
    /// Four verbs were named for something other than the button they press. <c>game_level</c> read
    /// as a noun as easily as a verb; <c>game_spell_level</c> was named for Spell Lv, a screen
    /// number it never moves, while the number it does move is Mastery Lv; <c>game_tooltips</c>
    /// differed from the reader beside it by one letter, and a round that could not tell them apart
    /// never called the reader at all; and <c>suite_automation</c> named a family rather than the
    /// seven switches it flips. Every old name is retired outright — no alias, no second door.
    /// </summary>
    [Fact]
    public void Every_renamed_verb_answers_only_to_its_new_name_and_its_title_says_what_it_presses()
    {
        var tools = GameMcpAcceptanceFixture.Tools();
        var names = GameMcpAcceptanceFixture.ToolNames();

        Assert.DoesNotContain("game_level", names);
        Assert.DoesNotContain("game_spell_level", names);
        Assert.DoesNotContain("game_tooltips", names);
        Assert.DoesNotContain("suite_automation", names);

        Assert.Equal(
            "Level a glyph, artifact type, resource type or Time Rune",
            Title(tools, "game_level_up"));
        Assert.Equal("Confirm a spell's mastery", Title(tools, "game_spell_mastery"));

        // The pair teaches itself: one lists what is on the screen and mints the addresses, the
        // other reads what one of them says.
        Assert.Equal("List the screen's hoverable elements", Title(tools, "game_screen_elements"));
        Assert.Equal("Read one element's tooltip text", Title(tools, "game_tooltip"));

        Assert.Equal("Read or flip the suite's seven breakers", Title(tools, "suite_breakers"));
    }

    /// <summary>
    /// The verb is named for Mastery Lv and says so: Spell Lv is the global casting dial, and a
    /// description that let the two share a word is what put the wrong screen number on the tool.
    /// </summary>
    [Fact]
    public void Confirming_mastery_names_the_button_it_presses_and_the_dial_it_does_not_move()
    {
        var description = Description(GameMcpAcceptanceFixture.Tools(), "game_spell_mastery");

        Assert.Equal(
            "Press Confirm Mastery for one spell, which raises the Mastery Lv its card shows. " +
            "mode=all presses the native Level All Spells sweep instead, which walks the whole " +
            "spellbook and skips only the spells it cannot afford. Spell Lv is the global casting " +
            "dial and is never moved here; that is game_casting_dial.",
            description);
    }

    private static string Title(IReadOnlyList<JObject> tools, string name) =>
        (string)Assert.Single(tools, tool => (string?)tool["name"] == name)["title"]!;

    private static string Description(IReadOnlyList<JObject> tools, string name) =>
        (string)Assert.Single(tools, tool => (string?)tool["name"] == name)["description"]!;

    [Fact]
    public void ActionSchemasRequireIdentityButNotGenerationKindOrNativeType()
    {
        var tools = GameMcpAcceptanceFixture.Tools();
        var purchase = Assert.Single(
            tools,
            tool => (string?)tool["name"] == "game_purchase");
        Assert.Equal("Purchase an attribute or upgrade", (string?)purchase["title"]);
        Assert.Contains("native StructureSO", (string?)purchase["description"]);
        var required = purchase["inputSchema"]!["required"]!.Values<string>().ToArray();
        Assert.Equal(new[] { "uuid", "amount" }, required);
        var properties = (JObject)purchase["inputSchema"]!["properties"]!;
        Assert.Null(properties["worldGeneration"]);
        Assert.Null(properties["expectedNativeType"]);
        Assert.Null(properties["kind"]);
        Assert.Null(properties["count"]);

        var screenshot = Assert.Single(
            tools,
            tool => (string?)tool["name"] == "game_screenshot");
        Assert.Null(screenshot["inputSchema"]!["required"]);
    }

    [Fact]
    public void EveryActionSchemaRejectsWorldGenerationAndVerbosityCeremony()
    {
        var actionNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "game_purchase", "game_cast", "game_concept", "game_agromancy",
            "game_structure",
            "game_return_to_menu",
            "game_modal",
            "game_spell_mastery", "game_casting_dial", "game_spell_loadout", "game_discover",
            "game_equipment", "game_alchemy", "game_ritual", "suite_config_set",
            "game_loadout",
            "suite_emergency_stop", "game_screenshot", "game_continue",
            "game_navigate", "game_tooltip",
            "game_targeting",
        };

        foreach (var tool in GameMcpAcceptanceFixture.Tools().Where(tool =>
                     actionNames.Contains((string)tool["name"]!)))
        {
            var properties = Assert.IsType<JObject>(tool["inputSchema"]!["properties"]);
            Assert.Null(properties["worldGeneration"]);
            Assert.Null(properties["detail"]);
            Assert.Null(properties["verbosity"]);
        }
    }

    /// <summary>
    /// The detail read takes an id and nothing else is required. A caller holding an id from a
    /// search, a refusal or an action response can read it without first learning which table it
    /// lives in, which is the whole of what naming a category used to cost them.
    /// </summary>
    [Fact]
    public void DetailReadRequiresNoCategoryAndHasNoIdAlias()
    {
        var detail = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            tool => (string?)tool["name"] == "world_get");
        Assert.Null(detail["inputSchema"]!["required"]);
        var properties = Assert.IsType<JObject>(detail["inputSchema"]!["properties"]);
        Assert.NotNull(properties["uuid"]);
        Assert.NotNull(properties["uuids"]);
        Assert.NotNull(properties["category"]);
        Assert.Null(properties["id"]);

        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var invalid = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = "world_get",
                ["arguments"] = new JObject { ["uuid"] = "not-a-guid" },
            }));
        Assert.Equal(
            "refused (ERR_INPUT): uuid must be a whole canonical UUID or an id handle " +
            "that names one published entity", GameMcpTestHarness.Page(invalid));
    }

    [Fact]
    public void TraceHealthIsWriterHealthOnly()
    {
        var result = GameMcpAcceptanceFixture.CallText("trace_health");
        Assert.StartsWith("unavailable\n", result, StringComparison.Ordinal);
        Assert.Contains("reason: the decision journal writer is not active", result,
            StringComparison.Ordinal);
        Assert.DoesNotContain("scope", result, StringComparison.Ordinal);
        Assert.DoesNotContain("events", result, StringComparison.Ordinal);
        Assert.DoesNotContain("worldGeneration", result, StringComparison.Ordinal);
        Assert.DoesNotContain("cursor", result, StringComparison.Ordinal);
    }

    [Fact]
    public void HealthHasOneCanonicalShapeAndRejectsDetailOptions()
    {
        var feature = new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, "AutoBuy"),
            "Auto Buy",
            configuredEnabled: false,
            FeatureStatusState.ConfigurationDisabled,
            new FeatureStatusReason(
                FeatureStatusReasonCode.ConfigurationDisabled,
                "disabled"),
            lifecycleGeneration: 9);
        var mentor = new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, "Mentor"),
            "Orb Mentor",
            configuredEnabled: true,
            FeatureStatusState.Operational,
            new FeatureStatusReason(FeatureStatusReasonCode.None, string.Empty),
            lifecycleGeneration: 9);
        var context = GameMcpTestHarness.Context(features: new[] { feature, mentor });
        var compact = Plugin.ProjectGameMcpHealthText(context);
        Assert.StartsWith("available\n", compact, StringComparison.Ordinal);
        // The fingerprint answers "same DLL or not", which twelve hex characters settle as well as
        // sixty-four did — and this is a line every health call pays for.
        Assert.Matches(
            @"(?m)^build: \S+ dll sha256 [0-9a-f]{12}$",
            compact);
        // One name per feature across the two verbs that list features, so the seven suite_breakers
        // takes as arguments are recognisable inside the nine health reports on.
        Assert.Contains("features configuration_disabled: auto_buy", compact, StringComparison.Ordinal);
        Assert.Contains("features operational: mentor", compact, StringComparison.Ordinal);
        Assert.Contains("game_craft: unavailable", compact, StringComparison.Ordinal);
        Assert.Contains("game_modal: unavailable", compact, StringComparison.Ordinal);
        Assert.DoesNotContain("Orb Mentor", compact, StringComparison.Ordinal);
        Assert.DoesNotContain("mailbox", compact, StringComparison.Ordinal);

        var modalAvailable = new GameMcpFrameContext(
            world: null,
            runtime: null,
            configuration: context.Configuration,
            lifecycleGeneration: 9,
            sceneName: "Main",
            nativeContractsAvailable: true,
            featureStatuses: Array.Empty<FeatureStatusSnapshot>(),
            traceWriterStatus: DecisionJournalStatus.Unavailable,
            traceWriterRevision: 0,
            writableConfiguration: Array.Empty<GameMcpWritableSettingDescriptor>(),
            modalDismissAvailable: true);
        var withoutRuntime = Plugin.ProjectGameMcpHealthText(modalAvailable);
        // Health names exceptions, the way it already does for features and services. A capability
        // that works is covered by the leading verdict, so it costs no line at all.
        Assert.DoesNotContain("game_modal", withoutRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("native contracts", withoutRuntime, StringComparison.Ordinal);
        // The settings the load normalizes are ones no caller chose, so a normalization that landed
        // says nothing and one that did not names itself here rather than only in the log.
        Assert.DoesNotContain("agent settings", withoutRuntime, StringComparison.Ordinal);
        Assert.Contains(
            "agent settings: Research Queue Mode did not stay on after the setting was written",
            Plugin.ProjectGameMcpHealthText(new GameMcpFrameContext(
                world: null,
                runtime: null,
                configuration: context.Configuration,
                lifecycleGeneration: 9,
                sceneName: "Main",
                nativeContractsAvailable: true,
                featureStatuses: Array.Empty<FeatureStatusSnapshot>(),
                traceWriterStatus: DecisionJournalStatus.Unavailable,
                traceWriterRevision: 0,
                writableConfiguration: Array.Empty<GameMcpWritableSettingDescriptor>(),
                modalDismissAvailable: true,
                agentSettingsFailure:
                    "Research Queue Mode did not stay on after the setting was written")),
            StringComparison.Ordinal);

        // The runtime outlives every scene change, so a scene name alone answered the same question
        // both ways in one session. The absent runtime is named as a session fact, and the world the
        // verdict describes is identified.
        Assert.Contains("world: not published", withoutRuntime, StringComparison.Ordinal);
        Assert.Contains(
            "runtime reason: the ServiceCycle runtime has not been created in this session yet",
            withoutRuntime,
            StringComparison.Ordinal);
        // A collected world always carries the moment it was read; health answers published exactly
        // when the world readers do, so the fixture has to be a world they would serve.
        var withWorld = Plugin.ProjectGameMcpHealthText(
            GameMcpTestHarness.Context(
                new GameWorldState { CollectedAtUtcTicks = DateTime.UtcNow.Ticks },
                generation: 1207));
        Assert.Contains("lifecycle: Playing, generation 9", withWorld, StringComparison.Ordinal);
        Assert.Contains("world: publication 1207", withWorld, StringComparison.Ordinal);
        Assert.DoesNotContain("world: generation", withWorld, StringComparison.Ordinal);

        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "suite_health");
        Assert.Empty((JObject)tool["inputSchema"]!["properties"]!);

        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var rejected = router.Handle(GameMcpAcceptanceFixture.Request(
            99,
            "tools/call",
            new JObject
            {
                ["name"] = "suite_health",
                ["arguments"] = new JObject { ["detail"] = "AutoBuy" },
            }));
        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: field " +
            "'detail' is not accepted by suite_health", GameMcpTestHarness.Page(rejected));
    }

    [Fact]
    public void TextToolsReturnOnlyTheAgentReadableTextContent()
    {
        var result = GameMcpToolExecution.Text(
            "scene: Main\ntabs:\n    Magic\n  * Scholar\n    subtabs:\n      * Discover")
            .ToProtocolResult();

        Assert.Null(result["structuredContent"]);
        Assert.Null(result["isError"]);
        var content = Assert.Single(result["content"]!.Values<JObject>())!;
        Assert.Equal("text", (string?)content["type"]);
        Assert.Equal(
            "scene: Main\ntabs:\n    Magic\n  * Scholar\n    subtabs:\n      * Discover",
            (string?)content["text"]);
    }
}

public sealed class GameMcpCommandPrimitiveTests
{
    [Fact]
    public void ImmutableCommandCrossesNoJsonOrUnityObjects()
    {
        var command = GameMcpAcceptanceFixture.NativeCommand();
        var properties = typeof(GameMcpCommand).GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.DoesNotContain(
            properties,
            property =>
                typeof(JToken).IsAssignableFrom(property.PropertyType) ||
                property.PropertyType.FullName?.StartsWith("UnityEngine.", StringComparison.Ordinal) == true);
        GameMcpNativeActionAdmission.AssertNativeType(command, "StructureSO");
        Assert.Throws<ArgumentException>(() =>
            GameMcpNativeActionAdmission.AssertNativeType(command, "UpgradeSO"));
    }
}

public sealed class GameMcpConfigurationTests
{
    [Fact]
    public void QueryReturnsOnlyTheWritableCatalog()
    {
        var writable = new GameMcpWritableSettingDescriptor(
            "AutoCast",
            "Mode",
            "Mode",
            string.Empty,
            new GameMcpConfigurationConstraint(
                "exact_parse_and_domain",
                string.Empty,
                string.Empty));
        var context = GameMcpTestHarness.Context(writable: new[] { writable });
        var listed = GameMcpAcceptanceFixture.CallText("suite_configuration", context: context);
        var described = GameMcpAcceptanceFixture.CallText(
            "suite_configuration", new JObject { ["mode"] = "describe" }, context);

        // The ordinary read is one line per setting and nothing else: what it does and what it takes
        // are the same words on every call, so they live in the tool's own documentation.
        Assert.Equal("AutoCast/Mode: Disabled", listed);
        Assert.Contains("AutoCast/Mode", described);
        Assert.Contains("type", described);
        Assert.Contains("description", described);
    }

    [Fact]
    public void WritableSchemaIsStaticAndValuesComeFromThePinnedPublication()
    {
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var schema = configuration.CreateGameMcpWritableSchema();
        var entries = typeof(BepInExAutomataConfiguration)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(property => property.GetValue(configuration))
            .OfType<ConfigEntryBase>()
            .ToDictionary(
                entry => (entry.Definition.Section, entry.Definition.Key));

        Assert.Equal(29, schema.Length);
        Assert.Equal(29, schema.Select(item => (item.Section, item.Key)).Distinct().Count());
        foreach (var descriptor in schema)
        {
            var entry = entries[(descriptor.Section, descriptor.Key)];
            Assert.Equal(
                entry.GetSerializedValue(),
                GameMcpConfigurationSchema.SerializePublishedValue(
                    configuration.Current,
                    descriptor.Section,
                    descriptor.Key));
        }

        var pinned = configuration.Current;
        configuration.AutoCastMode.Value = AutoCastOperationMode.Active;
        var result = GameMcpTestHarness.Json(OrbModding.Plugin.ProjectGameMcpConfiguration(
            GameMcpTestHarness.Context(
                configurationGeneration: 12,
                writable: schema,
                configuration: pinned),
            describe: false));

        Assert.Null(result["configurationGeneration"]);
        Assert.Null(result["configuration"]);
        Assert.DoesNotContain(
            result.DescendantsAndSelf().OfType<JProperty>(),
            property => property.Name == "equalityContract");
        Assert.Equal("Disabled", (string?)result["AutoCast/Mode"]);
        Assert.Equal("Active", configuration.AutoCastMode.GetSerializedValue());
        Assert.Same(schema, GameMcpTestHarness.Context(writable: schema).WritableConfiguration);
    }

    [Fact]
    public void ValidWritePublishesOnceAndStaleWriteDoesNot()
    {
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var publications = 0;
        var store = new AutomataConfigurationStore(configuration, (_, _) => publications++);
        var before = store.CurrentGeneration;
        Assert.True(store.TrySetGameMcp(
            configuration.AutoCastMode.Definition.Section,
            configuration.AutoCastMode.Definition.Key,
            "Active",
            before,
            out _,
            out _));
        Assert.False(store.TrySetGameMcp(
            configuration.AutoCastMode.Definition.Section,
            configuration.AutoCastMode.Definition.Key,
            "Disabled",
            before,
            out _,
            out _));
        Assert.Equal(1, publications);
    }

    /// <remarks>
    /// BepInEx writes its own domain for a config-file comment, and splicing that text into a
    /// refusal made the surface say "must be From 0 to 60" — the game's file format leaking into a
    /// player-facing sentence, with no machine field a caller could retry against.
    /// </remarks>
    [Fact]
    public void A_write_outside_the_declared_domain_answers_with_the_domain_as_numbers()
    {
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var store = new AutomataConfigurationStore(configuration, (_, _) => { });

        Assert.False(store.TrySetGameMcp(
            "AutoCast",
            "ManualPauseSeconds",
            "600",
            store.CurrentGeneration,
            out var reason,
            out var bound));

        Assert.Equal("AutoCast/ManualPauseSeconds must be from 0 to 60", reason);
        Assert.Equal(0d, bound.Minimum);
        Assert.Equal(60d, bound.Maximum);
    }
}

public sealed class GameMcpEmergencyStopTests
{
    [Fact]
    public void StopAndGameplayRetainSubmissionOrderForFrameExecution()
    {
        var operations = new GameMcpFrameInbox();
        var before = GameMcpAcceptanceFixture.SubmitHarvest(operations);
        var stop = operations.Submit(new GameMcpOperationRequestBuilder
        {
            ToolName = "suite_emergency_stop",
            Classification = GameMcpOperationClass.SuiteAdministration,
            RequiredData = GameMcpFrameData.Configuration,
            Mode = "engage",
        }.Freeze());
        var after = GameMcpAcceptanceFixture.SubmitHarvest(operations);

        Assert.Equal(new[] { before, stop, after }, operations.ClaimPending());
    }
}

public sealed class GameMcpForbiddenSurfaceTests
{
    [Fact]
    public void SurfaceContainsNoArbitraryInputOrSaveResetTools()
    {
        var names = GameMcpAcceptanceFixture.ToolNames();
        var combined = string.Join("|", names).ToLowerInvariant();
        Assert.DoesNotContain("save", combined);
        Assert.DoesNotContain("reset", combined);
        Assert.DoesNotContain("keyboard", combined);
        Assert.DoesNotContain("mouse", combined);
        Assert.DoesNotContain("invoke_native", combined);
    }
}

internal static class GameMcpAcceptanceFixture
{
    internal static readonly Guid SpellId =
        Guid.Parse("01234567-89ab-4cde-8f01-23456789abcd");

    internal static JObject Request(int id, string method, JObject parameters) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["method"] = method,
        ["params"] = parameters,
    };

    internal static IReadOnlyList<JObject> Tools()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var response = router.Handle(Request(1, "tools/list", new JObject()));
        return response.Body!["result"]!["tools"]!.Values<JObject>().OfType<JObject>().ToArray();
    }

    internal static string[] ToolNames() =>
        Tools().Select(tool => (string)tool["name"]!).ToArray();

    /// <summary>
    /// One tool call as a caller sees it: the page of text the protocol returns, and nothing beside
    /// it. Every tool answers this way now, so there is one helper rather than one per shape.
    /// </summary>
    internal static string CallText(
        string tool,
        JObject? arguments = null,
        GameMcpFrameContext? context = null)
    {
        var inbox = new GameMcpFrameInbox();
        var router = new GameMcpProtocolRouter(inbox);
        var pinned = context ?? GameMcpTestHarness.Context();
        var response = GameMcpTestHarness.Handle(router, inbox, Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = tool,
                ["arguments"] = arguments ?? new JObject(),
            }), operation => operation.Request.ToolName switch
            {
                "suite_health" => GameMcpToolExecution.Text(
                    Plugin.ProjectGameMcpHealthText(pinned)),
                "suite_configuration" => GameMcpToolExecution.Read(
                    Plugin.ProjectGameMcpConfiguration(
                        pinned, operation.Request.Mode == "describe")),
                "trace_health" => GameMcpToolExecution.Text(
                    Plugin.ProjectGameMcpTraceHealthText(pinned)),
                _ => GameMcpTestHarness.ExecuteRead(operation, pinned),
            });
        Assert.Equal(200, response.StatusCode);
        Assert.Null(response.Body?["error"]);
        Assert.Null(response.Body!["result"]!["structuredContent"]);
        var content = Assert.Single(response.Body["result"]!["content"]!.Values<JObject>());
        Assert.Equal("text", (string?)content["type"]);
        return (string)content["text"]!;
    }

    internal static GameMcpFrameContext SpellSnapshot(int masteryLevel) =>
        Snapshot(SpellWorld(masteryLevel, 30));

    internal static GameMcpFrameContext Snapshot(GameWorldState world)
    {
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(
            world with { EntityIdentities = GameMcpTestHarness.EntityCatalog },
            new WorldGeneration(1001));
        return Snapshot(publisher.ReadLatest());
    }

    internal static GameMcpFrameContext Snapshot(
        WorldPublication<GameWorldState> publication) =>
        GameMcpTestHarness.Context(publication);

    internal static GameWorldState SpellWorld(int masteryLevel, long epoch) => new()
    {
        CollectedAtEpoch = epoch,
        CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
            new[]
            {
                new WorldCollectionCategoryStatus(
                    "spell-recipes",
                    WorldCategoryOutcome.Collected,
                    sampled: 1,
                    skipped: 0,
                    firstFailure: string.Empty),
            },
            1),
        SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(
            new[]
            {
                new WorldSpellRecipe(
                    SpellId,
                    discovered: true,
                    discRarityLevel: 0,
                    masteryXp: new BigDouble(12),
                    masteryLevel,
                    masteryLevelReady: true,
                    masteryLevelAffordable: true,
                    hiddenDiscovery: false,
                    isRequiredDiscovery: true,
                    penaltyUsageCost: 1,
                    castSpeed: 1,
                    baseCharges: 1,
                    repeatInstantEffects: false,
                    spellPowerMod: new BigDouble(1),
                    spellCostMod: new BigDouble(1),
                    spellCdSpeedMod: new BigDouble(1),
                    spellDurationMod: new BigDouble(1),
                    spellSpecialMod: new BigDouble(1),
                    spellXpMod: new BigDouble(1),
                    hasAlertedThisMastery: false),
            },
            1),
    };

    internal static GameMcpCommand NativeCommand() =>
        new(
            sequence: 1,
            GameMcpCommandKind.Purchase,
            expectedLifecycleGeneration: 12,
            expectedConfigurationGeneration: 7,
            mode: "structure",
            Guid.NewGuid(),
            Guid.Empty,
            derivedNativeType: "StructureSO",
            amount: 1,
            payloadKey: string.Empty,
            payloadValue: string.Empty,
            saveCapture: false);

    internal static GameMcpFrameOperation SubmitHarvest(GameMcpFrameInbox operations) =>
        operations.Submit(new GameMcpOperationRequestBuilder
        {
            ToolName = "game_agromancy",
            Classification = GameMcpOperationClass.Gameplay,
            RequiredData = GameMcpFrameData.World | GameMcpFrameData.Configuration,
            Uuid = KnownEntities.FruitTreePlot.Uuid,
            SecondaryUuid = KnownEntities.FruitTreeCollect.Uuid,
            Mode = "add",
        }.Freeze());
}
