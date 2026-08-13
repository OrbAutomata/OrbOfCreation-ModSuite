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

public sealed class GameMcpSpellCompositionTests
{
    private static readonly Guid RecipeId =
        Guid.Parse("36375616-7476-4748-8c20-ba628933bea5");
    private static readonly Guid FirstGlyphId =
        Guid.Parse("81894d9f-4e91-43da-9f47-2a97d77a2294");
    private static readonly Guid SecondGlyphId =
        Guid.Parse("0f38b02c-b81a-4fcd-9e07-73e09bd38dee");
    private static readonly Guid FirstCoreGlyphId =
        Guid.Parse("1c002d3e-a0f0-4980-b6a8-e0f396a68934");
    private static readonly Guid SecondCoreGlyphId =
        Guid.Parse("cd38cfe0-14d9-44be-9621-de4b6874449b");
    private static readonly Guid ResourceId =
        Guid.Parse("eda26ca0-afcc-4fc3-9d8a-eb279123353d");
    private static readonly Guid SpellInstanceId =
        Guid.Parse("13b37dd5-44f7-4eb5-af6b-168454578466");
    private static readonly Guid SpellTypeId =
        Guid.Parse("4f2b9f27-9c22-4a24-9c9c-3b52b2b1e0a1");

    [Fact]
    public void ToolIsOneGlobalCastingDialWithoutPerSpellAugmentMutation()
    {
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_casting_dial");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(new[] { "dial", "value" }, schema["required"]!.Values<string>().ToArray());
        Assert.Equal(
            new[] { "output", "reserve" },
            schema["properties"]!["dial"]!["enum"]!.Values<string>().ToArray());
        Assert.NotNull(schema["properties"]!["value"]);
        Assert.Null(schema["properties"]!["spellInstanceUuid"]);
        Assert.Null(schema["properties"]!["augmentGlyphs"]);
        Assert.Null(schema["properties"]!["expectedNativeType"]);
        Assert.Null(schema["properties"]!["worldGeneration"]);
        Assert.Null(schema["properties"]!["detail"]);
        Assert.Null(schema["properties"]!["verbosity"]);
    }

    [Fact]
    public void MissingDialValueIsNamedAtSchemaValidation()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var response = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = "game_casting_dial",
                ["arguments"] = new JObject { ["dial"] = "output" },
            }));

        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: required " +
            "field 'value' is missing", GameMcpTestHarness.Page(response));
    }

    [Fact]
    public void GlobalDialAndBakedGlyphLayoutArePublishedInTheirOwningSurfaces()
    {
        var context = GameMcpTestHarness.Context(World());

        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            context,
            "spell-recipes",
            RecipeId.ToString("D")));
        var row = (JObject)response["row"]!;
        var equipped = Assert.Single(row["equipped"]!.Values<JObject>())!;
        var overview = GameMcpTestHarness.Json(GameMcpWorldQuery.Overview(context));

        Assert.Equal(4, (int)overview["casting"]!["output"]!["current"]!);
        Assert.Equal(12, (int)overview["casting"]!["output"]!["maximum"]!);
        Assert.Equal(3, (int)overview["casting"]!["reserve"]!["current"]!);
        Assert.Equal(9, (int)overview["casting"]!["reserve"]!["maximum"]!);
        Assert.Null(row["outputLevel"]);
        // The equipped spell is a runtime instance the catalog never publishes, so its handle
        // resolved for no tool. The recipe carries the same name and does resolve, so the row names
        // the spell once, by the identity a caller can look up.
        Assert.Equal("Gather Knowledge", (string?)equipped["name"]);
        Assert.Null(equipped["spellRecipe"]);
        Assert.Null(equipped["spellInstance"]);
        Assert.Null(equipped["outputLevel"]);
        Assert.Equal(6, (int)equipped["effectiveLevel"]!);
        Assert.Equal(3, (int)equipped["requiredMasteryLevel"]!);
        Assert.Equal(5, (int)equipped["recipeMasteryLevel"]!);
        Assert.True((bool)equipped["duration"]!);
        Assert.False((bool)equipped["usageRequirementsMet"]!);

        var applied = Assert.Single(equipped["glyphs"]!.Values<JObject>())!;
        Assert.Equal("Brew", (string?)applied["glyph"]!["name"]);
        Assert.Equal(2, (int)applied["count"]!);
        Assert.Null(response["augmentOptions"]);
        var options = row["loadoutAdd"]!["augmentOptions"]!.Values<JObject>().ToArray();
        Assert.Equal(new[] { "Insight", "Brew" },
            options.Select(option => (string?)option!["glyph"]!["name"]));
        Assert.Equal(new[] { 2, 3 }, options.Select(option => (int)option!["usableCount"]!));
        Assert.All(options, option => Assert.Null(option!["currentUses"]));

        var cast = Assert.Single(equipped["castCosts"]!.Values<JObject>())!;
        Assert.Equal("Knowledge", (string?)cast["resource"]!["name"]);
        Assert.Equal("4.4e3", (string?)cast["cost"]);
        Assert.Equal("9e6", (string?)cast["spendableAmount"]);
        Assert.True((bool)cast["affordable"]!);
        var drain = Assert.Single(equipped["drainCostsPerSecond"]!.Values<JObject>())!;
        Assert.Equal("250", (string?)drain["cost"]);
        Assert.Equal("9e6", (string?)drain["spendableAmount"]);
    }

    [Theory]
    [InlineData("output")]
    [InlineData("reserve")]
    public void ThePreparedCommandCarriesTheDialTheCallerNamed(string dial)
    {
        var operation = GameMcpProtocolRouter.BuildOperation(
            "game_casting_dial",
            new JObject { ["dial"] = dial, ["value"] = 5 });

        // Two dials share one screen, so the response can only name the one it moved if the
        // prepared command carries it. It reached the projection empty and printed an empty dial.
        Assert.True(Plugin.TryPrepareGameMcpCommand(
            new GameMcpFrameOperation(1, operation),
            GameMcpTestHarness.Context(World()),
            out var command,
            out var failure), failure?.Reason);
        Assert.Equal(dial, command.PayloadKey);
    }

    [Fact]
    public void CommittedMutationReturnsOnlyTheSettledDialDelta()
    {
        var submission = new SpellCompositionSubmission(
            SpellCompositionPreflight.Proceeded,
            SpellCompositionNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1),
            "the global output level is observable");
        var mapped = SpellCompositionActionResultMapper.Map(in submission);
        var command = Command(
            "set_output_level",
            GameMcpTestHarness.Context(World(outputLevel: 4)));
        var terminal = GameMcpCommandResult.FromAction(
            in mapped,
            command.Kind,
            9,
            3,
            submission.Reason,
            GameMcpSpellCompositionProjection.Project(in submission, "output"));
        terminal = terminal.WithDetails(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(World(outputLevel: 5)), command, terminal));

        var success = GameMcpTestHarness.Json(terminal.Project(command));

        Assert.Equal(
            new[] { "status", "dial", "before", "after", "maximum" },
            success.Properties().Select(property => property.Name));
        Assert.Equal("committed", (string?)success["status"]);
        Assert.Equal("output", (string?)success["dial"]);
        Assert.Equal(4, (int)success["before"]!);
        Assert.Equal(5, (int)success["after"]!);
        // The floor is the constant 1 on every call, so the tool documentation holds it and the
        // commit spends its bytes on the ceiling, which is the end that moves.
        Assert.Null(success["minimum"]);
        Assert.Equal(12, (int)success["maximum"]!);
        Assert.Null(success["code"]);
        Assert.Null(success["preflight"]);
        Assert.Null(success["receipt"]);
        Assert.DoesNotContain("payment", success.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attempt", success.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailureNamesTheSingleMissingOutcomeWithoutPersistentQuarantine()
    {
        var submission = new SpellCompositionSubmission(
            SpellCompositionPreflight.VerificationFailed,
            SpellCompositionNativeStage.Verification,
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(1, 1, 0),
            "the requested composition was not observable");

        var failure = GameMcpTestHarness.Json(
            GameMcpSpellCompositionProjection.Project(in submission, "output"));

        // Two dials share one tool. A refusal that named neither left the caller to remember
        // which one it had asked for.
        Assert.Equal("output", (string?)failure["dial"]);
        Assert.Equal("requested dial value", (string?)failure["missingOutcome"]);
        Assert.Equal(2, failure.Properties().Count());
        Assert.DoesNotContain("payment", failure.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <remarks>
    /// The commit publishes the live range beside the value it moved. The out-of-range refusal
    /// named that same range in prose only, so the one caller who most needed the numbers — the one
    /// who just guessed wrong — was the one who had to parse them back out of a sentence.
    /// </remarks>
    [Fact]
    public void An_out_of_range_dial_carries_the_range_its_own_sentence_names()
    {
        var refused = GameMcpTestHarness.Json(GameMcpSpellCompositionProjection.Project(
            SpellCompositionSubmission.OutOfRange(
                "Requested Output Level 20 is outside the live native range 1..12.", 1, 12),
            "output"));

        Assert.Equal("output", (string?)refused["dial"]);
        Assert.Equal(1, (int)refused["minimum"]!);
        Assert.Equal(12, (int)refused["maximum"]!);
    }

    [Fact]
    public void ReadAdmissionAndOperationScopedOwnershipUseOneCompositionCapability()
    {
        var world = World();
        Assert.True(GameMcpEntityCapabilityMap.Contains(
            world,
            Guid.Empty,
            GameMcpCommandKind.SpellComposition,
            out var outputReason), outputReason);
        Assert.True(GameMcpEntityCapabilityMap.Supports(
            "spell-slots", GameMcpCommandKind.SpellComposition));

        var registry = new ActionFamilyOwnershipRegistry();
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile()).Current;
        using var ownership = new AutomataActionFamilyOwnership(registry);
        ownership.Refresh(configuration, lifecycleReady: true);
        Assert.False(ownership.TryCaptureSpellCompositionMutationPermit());
        Assert.True(ownership.TryBeginGameMcpOperation(
            GameMcpCommandKind.SpellComposition,
            "set_output_level",
            out var scope,
            out var reason), reason);
        using (scope)
            Assert.True(ownership.TryCaptureSpellCompositionMutationPermit());
        Assert.False(ownership.TryCaptureSpellCompositionMutationPermit());
    }

    private static GameMcpCommand Command(
        string mode,
        GameMcpFrameContext? frameContext = null) => new(
        1,
        GameMcpCommandKind.SpellComposition,
        9,
        3,
        mode,
        Guid.Empty,
        Guid.Empty,
        "IntVariable",
        5,
        mode == "set_output_level" ? "output" : "reserve",
        string.Empty,
        false,
        frameContext: frameContext);

    /// <summary>
    /// The authored half of a spell — how it casts, its unmodified price, and what it belongs to —
    /// is readable from the row that names the spell.
    /// </summary>
    /// <remarks>
    /// The world has captured all three of these tables since the spell graph reader landed and
    /// nothing anywhere read any of them. The publication was right and the missing reader was the
    /// defect, so they ride on the detail row rather than being deleted.
    /// </remarks>
    [Fact]
    public void The_authored_half_of_a_spell_is_readable_from_its_own_row()
    {
        var context = GameMcpTestHarness.Context(World());

        var row = (JObject)GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            context, "spell-recipes", RecipeId.ToString("D")))["row"]!;

        // Words, not the game's ordinals: `castType: 2` and `rechargeProcessorType: 0` were the two
        // cells in this block a reader could not use, sitting beside the spell's type in plain
        // player words. `rechargeMultiplier: 1` and `repeatEffectRate: 1` said nothing about what
        // one of them meant, and the second was worse than silent — the game hands that "rate"
        // straight to a tick timer as the seconds BETWEEN repeats, so the obvious reading of it is
        // the reciprocal of the truth.
        Assert.Equal("aura", (string?)row["casting"]!["castType"]);
        Assert.Equal("spell-casts", (string?)row["casting"]!["rechargeCountsIn"]);
        Assert.Null(row["casting"]!["rechargeProcessorType"]);
        Assert.Equal(12d, (double)row["casting"]!["rechargeSeconds"]!);
        Assert.Equal(1.5d, (double)row["casting"]!["rechargeUnitMultiplier"]!);
        Assert.Null(row["casting"]!["rechargeMultiplier"]);
        Assert.Equal(30d, (double)row["casting"]!["maximumChannelSeconds"]!);
        Assert.Null(row["casting"]!["repeatEffectRate"]);
        Assert.Null(row["casting"]!["repeatEffectSeconds"]);

        var cast = Assert.Single(row["authoredCosts"]!["cast"]!.Values<JObject>())!;
        Assert.Equal("Knowledge", (string?)cast["name"]);
        Assert.Equal("4.4e3", (string?)cast["cost"]);
        var hold = Assert.Single(row["authoredCosts"]!["hold"]!.Values<JObject>())!;
        Assert.Equal("250", (string?)hold["cost"]);
        Assert.Null(row["authoredCosts"]!["upkeep"]);

        Assert.Equal(
            new[] { GameMcpTestHarness.Handle(SpellTypeId) },
            row["belongsTo"]!["spellTypes"]!.Values<JObject>()
                .Select(entry => (string?)entry!["uuid"]));
        Assert.Equal(
            new[]
            {
                GameMcpTestHarness.Handle(FirstCoreGlyphId),
                GameMcpTestHarness.Handle(SecondCoreGlyphId),
            },
            row["belongsTo"]!["coreGlyphs"]!.Values<JObject>()
                .Select(entry => (string?)entry!["uuid"]));
        Assert.Null(row["belongsTo"]!["recipeBooks"]);
    }

    /// <summary>
    /// Each native enum's words are pinned to what the game's own code does with the ordinal, not
    /// to the declaration order a decompiler prints — <c>SpellRecipeSO.CastType</c> lists Aura
    /// first and Aura is 2. An ordinal outside the pinned build's vocabulary throws: a sixth kind
    /// is a game change to model, never a number to pass through to a cell.
    /// </summary>
    [Fact]
    public void Every_native_enum_the_surface_prints_has_a_closed_vocabulary()
    {
        Assert.Equal("instant", GameMcpNativeVocabulary.CastType(0));
        Assert.Equal("channel", GameMcpNativeVocabulary.CastType(1));
        Assert.Equal("aura", GameMcpNativeVocabulary.CastType(2));
        Assert.Throws<InvalidOperationException>(() => GameMcpNativeVocabulary.CastType(3));

        Assert.Equal("time", GameMcpNativeVocabulary.RechargeProcessorType(0));
        Assert.Equal("spell-casts", GameMcpNativeVocabulary.RechargeProcessorType(1));
        Assert.Equal("attributes-developed", GameMcpNativeVocabulary.RechargeProcessorType(2));
        Assert.Throws<InvalidOperationException>(
            () => GameMcpNativeVocabulary.RechargeProcessorType(3));

        Assert.Equal("raw", GameMcpNativeVocabulary.ModifierEffect(0));
        Assert.Equal("exponent", GameMcpNativeVocabulary.ModifierEffect(4));
        Assert.Throws<InvalidOperationException>(() => GameMcpNativeVocabulary.ModifierEffect(5));

        // One ordinal, three meanings, chosen by the condition class it rode in on.
        Assert.Equal(
            "at-least-level",
            GameMcpNativeVocabulary.RequirementCheck(WorldRequirementConditionKind.Upgrade, 2));
        Assert.Equal(
            "at-least-mastery-level",
            GameMcpNativeVocabulary.RequirementCheck(WorldRequirementConditionKind.Spell, 2));
        Assert.Equal(
            "any-available",
            GameMcpNativeVocabulary.RequirementCheck(WorldRequirementConditionKind.List, 2));

        // The two kinds whose reqType is a suite sentinel rather than a native check say nothing.
        Assert.Null(
            GameMcpNativeVocabulary.RequirementCheck(WorldRequirementConditionKind.Unknown, -1));
        Assert.Null(
            GameMcpNativeVocabulary.RequirementCheck(WorldRequirementConditionKind.Literal, 1));

        Assert.Throws<InvalidOperationException>(
            () => GameMcpNativeVocabulary.RequirementCheck(
                WorldRequirementConditionKind.Ritual, 2));
    }

    private static GameWorldState World(int outputLevel = 4)
    {
        var recipeGlyphs = PublicationTable<WorldSpellRecipeGlyph>.Create(new[]
        {
            new WorldSpellRecipeGlyph(0, FirstCoreGlyphId),
            new WorldSpellRecipeGlyph(1, SecondCoreGlyphId),
        });
        var applied = PublicationTable<WorldSpellSlotGlyph>.Create(new[]
        {
            new WorldSpellSlotGlyph(FirstGlyphId, 2),
        });
        return new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "spell-recipes", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
                new WorldCollectionCategoryStatus(
                    "spell slots", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
                new WorldCollectionCategoryStatus(
                    "spell workbench", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(new[]
            {
                new WorldSpellRecipe(
                    RecipeId,
                    true,
                    5,
                    BigDouble.Zero,
                    0,
                    false,
                    false,
                    false,
                    0,
                    1d,
                    1,
                    false,
                    BigDouble.One,
                    BigDouble.One,
                    BigDouble.One,
                    BigDouble.One,
                    BigDouble.One,
                    BigDouble.One,
                    false,
                    recipeGlyphs,
                    PublicationTable<WorldDiscoverableCost>.Empty,
                    true),
            }),
            Glyphs = PublicationTable<WorldGlyph>.Create(new[]
            {
                Glyph(SecondGlyphId, 3, 2, 2),
                CoreGlyph(FirstCoreGlyphId),
                Glyph(FirstGlyphId, 7, 1, 3),
                CoreGlyph(SecondCoreGlyphId),
            }.OrderBy(glyph => glyph.EntityId).ToArray()),
            SpellWorkbench = new WorldSpellWorkbench(
                1,
                3,
                true,
                outputLevel,
                12,
                3,
                9),
            SpellSlots = PublicationTable<WorldSpellSlot>.Create(new[]
            {
                new WorldSpellSlot(
                    0,
                    SpellInstanceId,
                    RecipeId,
                    true,
                    false,
                    false,
                    false,
                    false,
                    false,
                    true,
                    true,
                    true,
                    true,
                    2,
                    3,
                    BigDouble.Zero,
                    4,
                    6,
                    3,
                    5,
                    true,
                    false,
                    applied),
            }),
            SpellCosts = PublicationTable<WorldSpellCost>.Create(new[]
            {
                new WorldSpellCost(0, WorldSpellCostKind.Immediate, ResourceId, new BigDouble(4.4d, 3)),
                new WorldSpellCost(0, WorldSpellCostKind.Drain, ResourceId, new BigDouble(2.5d, 2)),
            }),
            Resources = PublicationTable<WorldResource>.Create(new[] { Resource() }),
            SpellRecipeAuthoring = PublicationTable<WorldSpellRecipeAuthoring>.Create(new[]
            {
                new WorldSpellRecipeAuthoring(RecipeId, 2, 12d, 1.5d, 1, 30d, 0d),
            }),
            SpellAuthoredCosts = PublicationTable<WorldSpellAuthoredCost>.Create(new[]
            {
                new WorldSpellAuthoredCost(
                    RecipeId, WorldSpellAuthoredCostKind.Immediate, 0, ResourceId,
                    new BigDouble(4.4d, 3)),
                new WorldSpellAuthoredCost(
                    RecipeId, WorldSpellAuthoredCostKind.HoldDrain, 0, ResourceId,
                    new BigDouble(2.5d, 2)),
            }),
            SpellRelations = PublicationTable<WorldSpellRelation>.Create(new[]
            {
                new WorldSpellRelation(
                    RecipeId, WorldSpellRelationKind.SpellType, 0, SpellTypeId),
                new WorldSpellRelation(
                    RecipeId, WorldSpellRelationKind.CoreGlyph, 0, FirstCoreGlyphId),
                new WorldSpellRelation(
                    RecipeId, WorldSpellRelationKind.CoreGlyph, 1, SecondCoreGlyphId),
            }),
        };
    }

    private static WorldGlyph Glyph(Guid id, int level, int mastery, int maximum) => new(
        id,
        level,
        0,
        0,
        true,
        true,
        false,
        true,
        false,
        false,
        mastery,
        BigDouble.Zero,
        BigDouble.Zero,
        new BigDouble(maximum, 0),
        maximum);

    /// <summary>
    /// A core glyph as the game authors one: an unlocker, so <c>discoverable</c> is false and it is
    /// held off an authored requirement edge rather than by discovery.
    /// </summary>
    private static WorldGlyph CoreGlyph(Guid id) => new(
        id,
        1,
        0,
        0,
        true,
        false,
        false,
        false,
        false,
        false,
        0,
        BigDouble.Zero,
        BigDouble.Zero,
        BigDouble.One,
        1);

    private static WorldResource Resource()
    {
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            ResourceId,
            new BigDouble(9d, 6),
            new BigDouble(1d, 9),
            true,
            BigDouble.Zero,
            BigDouble.Zero,
            new BigDouble(100d),
            new BigDouble(100d),
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            false,
            true,
            false,
            0,
            Guid.Empty,
            in rateInputs,
            in traits,
            in modifiers);
        return new WorldResource(
            in reading,
            true,
            new BigDouble(9.91d, 8),
            0.009d,
            false,
            new BigDouble(9d, 6),
            BigDouble.Zero);
    }
}
