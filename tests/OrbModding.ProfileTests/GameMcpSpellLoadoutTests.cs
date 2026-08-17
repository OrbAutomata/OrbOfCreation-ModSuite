using System;
using System.Linq;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpSpellLoadoutTests
{
    private static readonly Guid FirstRecipeId =
        Guid.Parse("36375616-7476-4748-8c20-ba628933bea5");
    private static readonly Guid SecondRecipeId =
        Guid.Parse("02f55f76-bdba-4fa4-841b-da3a62b0d6db");
    private static readonly Guid FirstInstanceId =
        Guid.Parse("13b37dd5-44f7-4eb5-af6b-168454578466");
    private static readonly Guid SecondInstanceId =
        Guid.Parse("f40dfa54-2b96-4aee-97ec-5a8e8392a771");
    private static readonly Guid CoreGlyphId =
        Guid.Parse("f3000000-0000-0000-0000-000000000001");
    private static readonly Guid AugmentGlyphId =
        Guid.Parse("f3000000-0000-0000-0000-000000000002");
    private static readonly Guid SpellWeightResourceId =
        Guid.Parse("c1a20f2f-2a0b-4f0e-9f1a-2c7d51b6a4e3");

    [Fact]
    public void ToolUsesOneStagedPreviewAddRemoveMoveShapeAndBakesGlyphsOnlyOnAdd()
    {
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_spell_loadout");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(
            new[] { "mode" },
            schema["required"]!.Values<string>().ToArray());
        Assert.Equal(
            new[] { "staged", "preview", "add", "remove", "move" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>().ToArray());
        Assert.NotNull(schema["properties"]!["uuid"]);
        Assert.NotNull(schema["properties"]!["glyphs"]);

        // An equipped spell is addressed by the slot the screen numbers, not by a runtime instance
        // id the game never shows, and the wire counts those slots from 1.
        Assert.Equal(1, (int)schema["properties"]!["slot"]!["minimum"]!);
        Assert.Equal(1, (int)schema["properties"]!["destination"]!["minimum"]!);
        Assert.Null(schema["properties"]!["worldGeneration"]);
        Assert.Null(schema["properties"]!["detail"]);
        Assert.Null(schema["properties"]!["receipt"]);
    }

    [Fact]
    public void ConditionalDestinationValidationNamesTheExactField()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = "game_spell_loadout",
                ["arguments"] = new JObject
                {
                    ["mode"] = "move",
                    ["slot"] = 1,
                },
            }));
        var unexpected = router.Handle(GameMcpAcceptanceFixture.Request(
            2,
            "tools/call",
            new JObject
            {
                ["name"] = "game_spell_loadout",
                ["arguments"] = new JObject
                {
                    ["mode"] = "remove",
                    ["slot"] = 1,
                    ["destination"] = 2,
                },
            }));

        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: required " +
            "field 'destination' is missing for mode 'move'", GameMcpTestHarness.Page(missing));
        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: field " +
            "'destination' is accepted only for mode 'move'", GameMcpTestHarness.Page(unexpected));
    }

    [Theory]
    [InlineData("add")]
    [InlineData("preview")]
    public void AddAndPreviewRequireARecipeAndExplicitPossiblyEmptyGlyphLayout(string mode)
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = "game_spell_loadout",
                ["arguments"] = new JObject { ["mode"] = mode },
            }));

        var page = GameMcpTestHarness.Page(missing);
        Assert.StartsWith("refused (ERR_INPUT): ", page, StringComparison.Ordinal);
        Assert.Contains("'uuid'", page, StringComparison.Ordinal);
        Assert.Contains("'glyphs'", page, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewRequiresTheExplicitLayoutAndIsClassifiedAsReadOnly()
    {
        var operation = GameMcpProtocolRouter.BuildOperation(
            "game_spell_loadout",
            new JObject
            {
                ["mode"] = "preview",
                ["uuid"] = FirstRecipeId.ToString("D"),
                ["glyphs"] = new JArray(),
            });

        Assert.Equal(GameMcpOperationClass.ReadOnly, operation.Classification);
        Assert.Equal(FirstRecipeId, operation.Uuid);
        Assert.Empty(operation.UuidCounts);
    }

    [Fact]
    public void StagedReadIsParameterlessReadOnlyAndRejectsMutationArguments()
    {
        var operation = GameMcpProtocolRouter.BuildOperation(
            "game_spell_loadout",
            new JObject { ["mode"] = "staged" });
        Assert.Equal(GameMcpOperationClass.ReadOnly, operation.Classification);
        Assert.Equal(GameMcpFrameData.None, operation.RequiredData);

        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var rejected = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = "game_spell_loadout",
                ["arguments"] = new JObject
                {
                    ["mode"] = "staged",
                    ["uuid"] = FirstRecipeId.ToString("D"),
                    ["glyphs"] = new JArray(),
                },
            }));
        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: field " +
            "'uuid' is accepted only for modes 'preview' and 'add'; field 'glyphs' is " +
            "accepted only for modes 'preview' and 'add'", GameMcpTestHarness.Page(rejected));
    }

    [Fact]
    public void StagedReadReturnsTheExactNamedOrderedLayout()
    {
        var layout = SpellWorkbenchStagedLayout.Captured(
            new[] { new SpellWorkbenchGlyphStack(CoreGlyphId, 2) },
            new[] { new SpellWorkbenchGlyphStack(AugmentGlyphId, 1) });
        var catalog = EntityIdentityCatalogSnapshot.Bound(9, new[]
        {
            new EntityIdentityName(CoreGlyphId, "GlyphSO", "Channel", "channelGlyph"),
            new EntityIdentityName(AugmentGlyphId, "GlyphSO", "Bright", "brightGlyph"),
        });

        var response = Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            GameMcpSpellWorkbenchProjection.ProjectStagedLayout(in layout),
            catalog));

        Assert.Equal("available", (string?)response["status"]);
        var core = Assert.Single(response["core"]!).Value<JObject>()!;
        var augment = Assert.Single(response["augments"]!).Value<JObject>()!;
        Assert.Equal("Channel", (string?)core["glyph"]!["name"]);
        Assert.Equal(2, (int)core["count"]!);
        Assert.Equal("Bright", (string?)augment["glyph"]!["name"]);
        Assert.Equal(1, (int)augment["count"]!);
    }

    [Fact]
    public void SpellSlotListIsLeanAndPrintsNoIdNobodyCanResolve()
    {
        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(World()),
            "spell-slots",
            0,
            10));
        var rows = response["rows"]!.Values<JObject>().ToArray();
        Assert.Equal(3, rows.Length);

        var firstSummary = Assert.IsType<JObject>(rows[0]);
        // The number on the row is the number the verbs take. It used to be the internal index, so
        // a caller reading slot 5 off the list unequipped what the same list called slot 4.
        Assert.Equal(1, (int)firstSummary["slot"]!);
        Assert.Null(firstSummary["slotIndex"]);

        // A slot is not the recipe it holds, so it publishes no uuid of its own — the absence of
        // a handle is how a row says there is nothing to hand back — and the category it belongs to
        // is the one the caller named to get this page.
        Assert.Null(firstSummary["uuid"]);
        Assert.Null(firstSummary["addressable"]);
        Assert.Null(firstSummary["category"]);
        // Neither is the runtime instance it holds an address: that handle is in no catalog, so
        // every tool refused it. The recipe names the spell and does resolve.
        Assert.Null(firstSummary["spellInstance"]);
        Assert.Equal("Gather Knowledge", (string?)firstSummary["spellRecipe"]!["name"]);
        Assert.Null(firstSummary["remove"]);

        // The spell column says whether the slot is filled, so `occupied` is not a second column
        // for the same bit — and a bar with nothing on it keeps the column a full one has.
        Assert.Null(firstSummary["occupied"]);
        var second = Assert.IsType<JObject>(rows[1]);
        var empty = Assert.IsType<JObject>(rows[2]);
        Assert.Equal(2, (int)second["slot"]!);
        Assert.Equal("Whirling Sorcery", (string?)second["spellRecipe"]!["name"]);
        Assert.Equal(3, (int)empty["slot"]!);
        Assert.Equal("empty", (string?)empty["spellRecipe"]);
        Assert.Null(response["moveDestinations"]);
    }

    /// <summary>
    /// A slot's row says what is in it, and never what it is doing this instant. Whether a spell is
    /// mid-cast turns over on its own between two reads — a live round caught the column on one page
    /// of a scan and gone from the next with nothing about the request changed — so a page of it
    /// plans nothing and is not a page. The whole row is two columns, asserted here in full.
    /// </summary>
    [Fact]
    public void A_spell_slot_row_says_what_the_slot_holds_and_not_what_it_is_doing()
    {
        var world = World();
        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(world),
            "spell-slots",
            0,
            10));
        var rows = response["rows"]!.Values<JObject>().ToArray();

        Assert.All(rows, row => Assert.Equal(
            new[] { "slot", "spellRecipe" },
            row.Children<JProperty>().Select(property => property.Name)));

        // The fact itself is not deleted, only the column: the slot's own detail row still answers
        // for the instant, and so does every cast response. Slot two of this bar is mid-cast.
        var detail = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectEntityState(
            world, "spell-slots", world.SpellSlots[1]));
        Assert.True((bool)detail["casting"]!);
    }

    /// <summary>
    /// An equipped spell says whether the game holds it loadout-unique, so the rule stops being
    /// something a caller can only learn by being refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fact's home is <c>SpellTypeSO.isLoadoutUnique</c>; <c>Spell.IsUniqueSpell()</c> is the
    /// game's own reading of it over the spell's types, and
    /// <c>Spell.GetEquipRequirements(SpellListVariable)</c> is what refuses a candidate that finds an
    /// already-equipped spell of the same recipe. So the answer belongs on the equipped row, which
    /// the recipe's own <c>equipped</c> list already carries — a caller planning a second copy reads
    /// it there before it calls anything.
    /// </para>
    /// <para>
    /// Asserted as the row's exact key set rather than as a lookup, because the point of the column
    /// is that it is always present: a flag published only where it is true would leave the recipes
    /// that are not loadout-unique indistinguishable from the ones nobody read.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_equipped_spell_publishes_the_games_own_loadout_uniqueness_answer()
    {
        var world = World(uniqueFirst: true);

        var unique = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectEntityState(
            world, "spell-slots", world.SpellSlots[0]));
        var ordinary = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectEntityState(
            world, "spell-slots", world.SpellSlots[1]));

        Assert.True((bool)unique["isLoadoutUnique"]!);
        Assert.False((bool)ordinary["isLoadoutUnique"]!);

        Assert.Equal(
            new[]
            {
                "slot",
                "effectiveLevel",
                "requiredMasteryLevel",
                "recipeMasteryLevel",
                "duration",
                "toggleable",
                "usageRequirementsMet",
                "isLoadoutUnique",
                "casts",
                "remove",
                "move",
                "glyphs",
                "occupied",
                "spellRecipe",
            },
            unique.Children<JProperty>().Select(property => property.Name));
    }

    /// <summary>
    /// A move onto an occupied slot is a swap, and the answer names both halves. Reporting only
    /// the spell the caller asked about left the other one somewhere the caller's model did not
    /// have it, and the next cast at the old address was refused with nothing explaining it.
    /// </summary>
    [Fact]
    public void CommittedMutationNamesBothHalvesOfASwap()
    {
        var submission = new SpellLoadoutSubmission(
            SpellLoadoutPreflight.Proceeded,
            SpellLoadoutNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(2, 1, 1),
            "the exact requested swap is observable");
        var mapped = SpellLoadoutActionResultMapper.Map(in submission);
        var command = Command(
            "move",
            destinationSlot: 1,
            frameContext: GameMcpTestHarness.Context(World()));
        var terminal = GameMcpCommandResult.FromAction(
            in mapped,
            command.Kind,
            9,
            3,
            submission.Reason,
            GameMcpSpellLoadoutProjection.Project(in submission));
        terminal = terminal.WithDetails(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(World(moved: true)), command, terminal));

        var success = GameMcpTestHarness.Json(terminal.Project(command));

        Assert.Equal(
            new[] { "status", "uuid", "name", "slot", "displaced" },
            success.Properties().Select(property => property.Name));
        Assert.Equal("committed", (string?)success["status"]);
        Assert.Equal("Gather Knowledge", (string?)success["name"]);
        Assert.Equal(1, (int)success["slot"]!["before"]!);
        Assert.Equal(2, (int)success["slot"]!["after"]!);
        Assert.Equal(
            GameMcpTestHarness.Handle(SecondRecipeId),
            (string?)success["displaced"]!["uuid"]);
        Assert.Equal(2, (int)success["displaced"]!["slot"]!["before"]!);
        Assert.Equal(1, (int)success["displaced"]!["slot"]!["after"]!);
        Assert.Null(success["code"]);
        Assert.Null(success["loadout"]);
        Assert.Null(success["preflight"]);
        Assert.Null(success["before"]);
        Assert.Null(success["receipt"]);
        Assert.DoesNotContain("payment", success.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attempt", success.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A removal answers with the two budgets it moved and says the door it closed.
    /// </summary>
    /// <remarks>
    /// A round removed a spell specifically to free upkeep and got back one slot number with no
    /// <c>after</c> beside it and no budget at all — so when the next add refused for an unrelated
    /// reason, the reader concluded the verb was sensitive to unrelated state and spent the rest of
    /// the round working around a wall that was not there. Both budgets are the answer, and the
    /// removal names itself as one-way because nothing else on the wire says the instance is gone.
    /// </remarks>
    [Fact]
    public void CommittedRemovalAnswersWithBothBudgetsItFreedAndNamesTheOneWayDoor()
    {
        var submission = new SpellLoadoutSubmission(
            SpellLoadoutPreflight.Proceeded,
            SpellLoadoutNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1),
            "the exact runtime spell is absent from the loadout");
        var mapped = SpellLoadoutActionResultMapper.Map(in submission);
        var command = Command("remove", frameContext: GameMcpTestHarness.Context(World()));
        var terminal = GameMcpCommandResult.FromAction(
            in mapped,
            command.Kind,
            9,
            3,
            submission.Reason,
            GameMcpSpellLoadoutProjection.Project(in submission));
        terminal = terminal.WithDetails(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(World(removed: true)), command, terminal));

        var success = GameMcpTestHarness.Json(terminal.Project(command));

        Assert.Equal(
            new[] { "status", "uuid", "name", "slot", "loadBudget", "oneWay" },
            success.Properties().Select(property => property.Name));
        Assert.Equal("committed", (string?)success["status"]);
        Assert.Equal(1, (int)success["slot"]!["before"]!);
        Assert.Equal("empty", (string?)success["slot"]!["after"]);

        var budget = success["loadBudget"]!;
        Assert.Equal(
            new[] { "used", "maximum", "fitsAnotherSpell", "usageBudget" },
            ((JObject)budget).Properties().Select(property => property.Name));
        Assert.Equal(2, (int)budget["used"]!["before"]!);
        Assert.Equal(1, (int)budget["used"]!["after"]!);
        Assert.Equal(3, (int)budget["maximum"]!);
        Assert.True((bool)budget["fitsAnotherSpell"]!);

        var usage = Assert.Single(budget["usageBudget"]!.Values<JObject>())!;
        Assert.Equal(
            new[] { "resource", "headroom", "used", "maximum" },
            usage.Properties().Select(property => property.Name));
        Assert.Equal("3", (string?)usage["headroom"]!["before"]);
        Assert.Equal("6", (string?)usage["headroom"]!["after"]);
        Assert.Equal("2", (string?)usage["used"]);
        Assert.Equal("8", (string?)usage["maximum"]);

        Assert.Equal(
            "The game destroyed this spell; the way back is another add.",
            (string?)success["oneWay"]);
    }

    [Fact]
    public void FailureNamesTheMissingOutcomeWithoutPersistentState()
    {
        var submission = new SpellLoadoutSubmission(
            SpellLoadoutPreflight.VerificationFailed,
            SpellLoadoutNativeStage.Verification,
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(2, 1, 0),
            "the requested swap was not observable");

        var failure = GameMcpTestHarness.Json(
            GameMcpSpellLoadoutProjection.Project(in submission));

        Assert.Equal("requested spell slot state", (string?)failure["missingOutcome"]);
        Assert.Single(failure.Properties());
    }

    [Fact]
    public void ReadAdmissionAndOperationOwnershipUseOneLoadoutCapability()
    {
        var world = World();
        Assert.True(GameMcpEntityCapabilityMap.Contains(
            world,
            FirstInstanceId,
            GameMcpCommandKind.SpellLoadout,
            out var admissionReason), admissionReason);
        Assert.True(GameMcpEntityCapabilityMap.Supports(
            "spell-slots", GameMcpCommandKind.SpellLoadout));

        var registry = new ActionFamilyOwnershipRegistry();
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile()).Current;
        using var ownership = new AutomataActionFamilyOwnership(registry);
        ownership.Refresh(configuration, lifecycleReady: true);
        Assert.False(ownership.TryCaptureSpellLoadoutMutationPermit());
        Assert.True(ownership.TryBeginGameMcpOperation(
            GameMcpCommandKind.SpellLoadout,
            "move",
            out var scope,
            out var reason), reason);
        using (scope)
            Assert.True(ownership.TryCaptureSpellLoadoutMutationPermit());
        Assert.False(ownership.TryCaptureSpellLoadoutMutationPermit());
    }

    private static GameMcpCommand Command(
        string mode,
        int destinationSlot = 0,
        GameMcpFrameContext? frameContext = null) => new(
        1,
        GameMcpCommandKind.SpellLoadout,
        9,
        3,
        mode,
        FirstInstanceId,
        Guid.Empty,
        "Spell",
        destinationSlot + 1,
        string.Empty,
        string.Empty,
        false,
        frameContext: frameContext);

    /// <summary>
    /// A spell instance's id is a Unity object the game never shows and the asset catalog never
    /// publishes, so the loadout bar is addressed the way the screen numbers it. A refusal says
    /// both what was asked for and what is actually there, so the next call needs no second read.
    /// </summary>
    [Fact]
    public void A_slot_names_the_spell_and_a_refusal_names_both_sides()
    {
        var world = World();

        Assert.True(GameMcpWorldQuery.TryEquippedSpellSlot(world, 1, out var first, out _));
        Assert.Equal(FirstInstanceId, first);
        Assert.True(GameMcpWorldQuery.TryEquippedSpellSlot(world, 2, out var second, out _));
        Assert.Equal(SecondInstanceId, second);

        Assert.False(GameMcpWorldQuery.TryEquippedSpellSlot(world, 3, out _, out var empty));
        Assert.Equal("Slot 3 is empty; the spells you have equipped are in slots 1, 2.", empty);

        Assert.False(GameMcpWorldQuery.TryEquippedSpellSlot(world, 9, out _, out var absent));
        Assert.Equal("There is no slot 9; the loadout bar has slots 1 to 3.", absent);

        Assert.False(GameMcpWorldQuery.TryEquippedSpellSlot(world, 0, out _, out var zero));
        Assert.Equal("There is no slot 0; the loadout bar has slots 1 to 3.", zero);
    }

    /// <summary>
    /// A price row addresses the slot it prices the way every spell verb does. It carried the raw
    /// array index, so the caller who read a price and then cast that number cast the slot before.
    /// </summary>
    [Fact]
    public void A_spell_price_row_names_the_slot_the_cast_verb_takes()
    {
        var resourceId = Guid.Parse("6a3a5b41-4a2e-4f52-9f47-9f6d1a4e2c11");
        var world = new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(9, new[]
            {
                new EntityIdentityName(resourceId, "ResourceSO", "Knowledge", "knowledge"),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "spell slots", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
            SpellCosts = PublicationTable<WorldSpellCost>.Create(new[]
            {
                new WorldSpellCost(
                    0, WorldSpellCostKind.Immediate, resourceId, new BigDouble(50)),
            }),
        };

        var row = Assert.Single(GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(world), "spell-costs", 0, 10))
            ["rows"]!.Values<JObject>());

        Assert.Equal(1, (int)row["slot"]!);
        Assert.Null(row["slotIndex"]);
        Assert.Equal("immediate", (string?)row["kind"]);
    }

    private static GameWorldState World(
        bool moved = false,
        bool removed = false,
        bool uniqueFirst = false)
    {
        var first = Slot(
            moved ? 1 : 0,
            FirstInstanceId,
            FirstRecipeId,
            canRemove: true,
            casting: false,
            loadoutUnique: uniqueFirst);
        var second = Slot(
            moved ? 0 : 1,
            SecondInstanceId,
            SecondRecipeId,
            canRemove: false,
            casting: true);
        var empty = new WorldSpellSlot(
            2, Guid.Empty, Guid.Empty, false, false, false, false, false,
            false, false, false, false, false, 0, 0, BigDouble.Zero);
        return new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "spell slots", WorldCategoryOutcome.Collected, 3, 0, string.Empty),
                new WorldCollectionCategoryStatus(
                    "spell workbench", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
            Resources = PublicationTable<WorldResource>.Create(new[]
            {
                SpellWeightResource(removed ? 2 : 5),
            }),
            SpellWorkbench = new WorldSpellWorkbench(
                removed ? 1 : 2,
                3,
                true,
                4,
                12,
                usageBudgetResourceIds: PublicationTable<Guid>.Create(new[] { SpellWeightResourceId })),
            SpellSlots = removed
                ? PublicationTable<WorldSpellSlot>.Create(new[]
                {
                    new WorldSpellSlot(
                        0, Guid.Empty, Guid.Empty, false, false, false, false, false,
                        false, false, false, false, false, 0, 0, BigDouble.Zero),
                    second,
                    empty,
                })
                : PublicationTable<WorldSpellSlot>.Create(new[]
                {
                    moved ? second : first,
                    moved ? first : second,
                    empty,
                }),
        };
    }

    /// <summary>
    /// One spell-weight resource, at the quantity the loadout leaves it: a bandwidth pool whose
    /// headroom is the room left under its ceiling, which is what the add gate weighs a candidate
    /// against.
    /// </summary>
    private static WorldResource SpellWeightResource(int used)
    {
        var rateInputs = default(RawResourceRateInputs);
        var traits = new RawResourceTraits(
            0d, 0d, 0d, false, false, false,
            bandwidthResource: true,
            invertedResource: false,
            excludeFromGlobals: false,
            startVisible: true,
            BigDouble.Zero, 0, 0, 0d, false, 0d,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            SpellWeightResourceId, new BigDouble(used), new BigDouble(8),
            true, BigDouble.Zero, BigDouble.Zero, new BigDouble(8),
            new BigDouble(8), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false, false,
            false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
        return new WorldResource(
            in reading, true, new BigDouble(8 - used), 1d, false, new BigDouble(used),
            BigDouble.Zero);
    }

    private static WorldSpellSlot Slot(
        int slot,
        Guid instance,
        Guid recipe,
        bool canRemove,
        bool casting,
        bool loadoutUnique = false) => new(
            slot,
            instance,
            recipe,
            true,
            casting,
            false,
            false,
            false,
            false,
            false,
            true,
            true,
            canRemove,
            true,
            1,
            1,
            BigDouble.Zero,
            4,
            4,
            0,
            4,
            false,
            true,
            PublicationTable<WorldSpellSlotGlyph>.Empty,
            isLoadoutUnique: loadoutUnique);
}
