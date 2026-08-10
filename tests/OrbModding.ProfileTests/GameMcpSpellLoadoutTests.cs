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
        Assert.True((bool)firstSummary["occupied"]!);
        Assert.Null(firstSummary["remove"]);
        var second = Assert.IsType<JObject>(rows[1]);
        var empty = Assert.IsType<JObject>(rows[2]);
        Assert.Equal(2, (int)second["slot"]!);
        Assert.Equal("Whirling Sorcery", (string?)second["spellRecipe"]!["name"]);
        Assert.Equal(3, (int)empty["slot"]!);
        Assert.False((bool)empty["occupied"]!);
        Assert.Null(response["moveDestinations"]);
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

    private static GameWorldState World(bool moved = false)
    {
        var first = Slot(
            moved ? 1 : 0,
            FirstInstanceId,
            FirstRecipeId,
            canRemove: true,
            casting: false);
        var second = Slot(
            moved ? 0 : 1,
            SecondInstanceId,
            SecondRecipeId,
            canRemove: false,
            casting: true);
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
            SpellWorkbench = new WorldSpellWorkbench(
                2,
                3,
                true,
                4,
                12),
            SpellSlots = PublicationTable<WorldSpellSlot>.Create(new[]
            {
                moved ? second : first,
                moved ? first : second,
                new WorldSpellSlot(
                    2, Guid.Empty, Guid.Empty, false, false, false, false, false,
                    false, false, false, false, false, 0, 0, BigDouble.Zero),
            }),
        };
    }

    private static WorldSpellSlot Slot(
        int slot,
        Guid instance,
        Guid recipe,
        bool canRemove,
        bool casting) => new(
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
            PublicationTable<WorldSpellSlotGlyph>.Empty);
}
