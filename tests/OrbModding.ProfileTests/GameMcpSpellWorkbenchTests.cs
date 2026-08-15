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

public sealed class GameMcpSpellWorkbenchTests
{
    private static readonly Guid RecipeId =
        Guid.Parse("36375616-7476-4748-8c20-ba628933bea5");
    private static readonly Guid FirstGlyphId =
        Guid.Parse("81894d9f-4e91-43da-9f47-2a97d77a2294");
    private static readonly Guid SecondGlyphId =
        Guid.Parse("0f38b02c-b81a-4fcd-9e07-73e09bd38dee");
    private static readonly Guid ResourceId =
        Guid.Parse("eda26ca0-afcc-4fc3-9d8a-eb279123353d");
    private static readonly Guid AugmentGlyphId =
        Guid.Parse("95b05e80-e0e8-45d2-b510-5d9d8d7d9b70");

    [Fact]
    public void OutputFirstWorkbenchToolIsAbsentFromThePlayerSurface()
    {
        Assert.DoesNotContain(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_spell_workbench");
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_discover");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(
            new[] { "mode" },
            schema["required"]!.Values<string>().ToArray());
        Assert.Equal(
            new[] { "preview", "confirm", "offer_initiate", "offer_select", "offer_confirm", "offer_reroll" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>().ToArray());
        Assert.Null(schema["properties"]!["expectedNativeType"]);
        Assert.Null(schema["properties"]!["worldGeneration"]);
        Assert.Null(schema["properties"]!["detail"]);
        Assert.Null(schema["properties"]!["verbosity"]);
    }

    [Fact]
    public void ConfirmRequiresSurfaceAndComponentsNotAnOutputRecipe()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var response = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = "game_discover",
                ["arguments"] = new JObject { ["mode"] = "confirm" },
            }));

        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: required " +
            "field 'surface' is missing for mode 'confirm'; required field " +
            "'components' is missing for mode 'confirm'", GameMcpTestHarness.Page(response));
    }

    [Fact]
    public void ListIsLeanWhileGetExposesTheComponentFirstDiscoveryDecision()
    {
        var context = GameMcpTestHarness.Context(World(
            discovered: false,
            discoveryAffordable: true,
            hasEmptySlot: true));

        var list = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(context, "spell-recipes", 0, 10));
        var listed = Assert.Single(list["rows"]!.Values<JObject>())!;
        var get = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            context, "spell-recipes", RecipeId.ToString("D")));
        var exact = (JObject)get["row"]!;

        Assert.Equal("Gather Knowledge", (string?)listed["name"]);
        Assert.Equal(0, (int)listed["masteryLevel"]!);
        Assert.False((bool)listed["discovered"]!);
        // A page is one category, so the header names it once instead of every row repeating it.
        Assert.Null(listed["category"]);
        Assert.Null(listed["discover"]);
        Assert.True((bool)exact["discover"]!["available"]!);
        Assert.True((bool)exact["discover"]!["affordable"]!);
        Assert.Equal("spellcraft", (string?)exact["discover"]!["surface"]);
        Assert.Equal(
            new[] { "Brew", "Insight" },
            exact["discover"]!["components"]!.Values<JObject>()
                .Select(component => (string?)component!["component"]!["name"]));
        Assert.All(
            exact["discover"]!["components"]!.Values<JObject>(),
            component => Assert.Equal(1, (int)component!["count"]!));
        var glyphs = exact["coreGlyphs"]!.Values<JObject>().ToArray();
        Assert.Equal(new[] { "Brew", "Insight" },
            glyphs.Select(glyph => (string?)glyph!["glyph"]!["name"]));
        Assert.Equal(new[] { "7", "3" },
            glyphs.Select(glyph => (string?)glyph!["ownedLevel"]));
        var cost = Assert.Single(exact["discover"]!["costs"]!.Values<JObject>())!;
        Assert.Equal("Knowledge", (string?)cost["resource"]!["name"]);
        Assert.Equal("4.4e3", (string?)cost["cost"]);
        Assert.Equal("9e6", (string?)cost["spendableAmount"]);
    }

    [Fact]
    public void DiscoveredRecipePublishesTheExactLoadoutAddDecisionAndCurrentHoldings()
    {
        var context = GameMcpTestHarness.Context(World(
            discovered: true,
            discoveryAffordable: true,
            hasEmptySlot: true,
            equipped: true));

        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            context, "spell-recipes", RecipeId.ToString("D")));
        var row = response["row"]!;

        Assert.Null(row["selected"]);
        Assert.Null(row["select"]);
        Assert.True((bool)row["loadoutAdd"]!["available"]!);
        Assert.True((bool)row["loadoutAdd"]!["requiresGlyphLayout"]!);
        Assert.Null(row["loadoutAdd"]!["affordable"]);
        Assert.Null(row["loadoutAdd"]!["reasonCode"]);
        Assert.Null(row["loadoutAdd"]!["costs"]);
        Assert.Single(row["loadoutAdd"]!["augmentOptions"]!.Values<JObject>());
        Assert.Equal(1, (int)row["loadBudget"]!["used"]!);
        Assert.Equal(3, (int)row["loadBudget"]!["maximum"]!);
        Assert.True((bool)row["loadBudget"]!["fitsAnotherSpell"]!);
        var equipped = Assert.Single(row["equipped"]!.Values<JObject>())!;
        Assert.Equal(1, (int)equipped["slot"]!);
        Assert.Equal("Gather Knowledge", (string?)equipped["name"]);
    }

    /// <summary>
    /// The load budget names both budgets an added spell is weighed against, not only the spots.
    /// </summary>
    /// <remarks>
    /// A round removed a spell to free upkeep, saw <c>fitsAnotherSpell: yes</c> on both sides of
    /// the removal, and never learned that what refused its add was the spell-weight budget — a
    /// different pool with a different unit that the world published nowhere.
    /// </remarks>
    [Fact]
    public void LoadBudgetPublishesTheUsageHeadroomTheAddGateActuallyCompares()
    {
        var context = GameMcpTestHarness.Context(World(
            discovered: true,
            discoveryAffordable: true,
            hasEmptySlot: true,
            usageBudget: true));

        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            context, "spell-recipes", RecipeId.ToString("D")));
        var budget = response["row"]!["loadBudget"]!;

        Assert.True((bool)budget["fitsAnotherSpell"]!);
        var usage = Assert.Single(budget["usageBudget"]!.Values<JObject>())!;
        Assert.Equal(
            new[] { "resource", "headroom", "used", "maximum" },
            usage.Properties().Select(property => property.Name));
        Assert.Equal("Knowledge", (string?)usage["resource"]!["name"]);
        Assert.Equal("3", (string?)usage["headroom"]);
        Assert.Equal("5", (string?)usage["used"]);
        Assert.Equal("8", (string?)usage["maximum"]);
    }

    [Theory]
    [InlineData(false, 7, "ERR_LIMIT", "Every slot in this loadout is in use.")]
    [InlineData(true, 0, "ERR_LOCKED", "This recipe's core glyph has no level yet.")]
    public void StructurallyUnavailableLoadoutAddWithholdsLayoutOptionsAndPriceClaims(
        bool hasEmptySlot,
        int coreLevel,
        string reasonCode,
        string reason)
    {
        var context = GameMcpTestHarness.Context(World(
            discovered: true,
            discoveryAffordable: true,
            hasEmptySlot,
            coreLevel: coreLevel));

        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            context, "spell-recipes", RecipeId.ToString("D")));
        var decision = response["row"]!["loadoutAdd"]!;

        Assert.False((bool)decision["available"]!);
        Assert.Equal(reasonCode, (string?)decision["reasonCode"]);
        Assert.Equal(reason, (string?)decision["reason"]);
        Assert.Null(decision["augmentOptions"]);
        Assert.Null(decision["affordable"]);
        Assert.Null(decision["costs"]);
    }

    [Fact]
    public void ExplicitLayoutPreviewReturnsNamedNativePriceAndShortResource()
    {
        var preview = SpellWorkbenchPricePreview.Priced(
            RecipeId,
            RecipeId,
            new[] { new SpellWorkbenchPricePreviewCost(ResourceId, new BigDouble(4400)) },
            affordable: false,
            ResourceId);

        var response = GameMcpTestHarness.Json(
            GameMcpSpellWorkbenchProjection.ProjectPricePreview(in preview));

        Assert.Equal("available", (string?)response["status"]);

        // The recipe was the caller's own argument; the answer is the price and whether it can be
        // paid, not that same recipe read back under two names beneath its own handle.
        Assert.Null(response["recipe"]);
        Assert.Null(response["uuid"]);
        var cost = Assert.Single(response["costs"]!.Values<JObject>());
        Assert.Equal("Knowledge", (string?)cost["resource"]!["name"]);
        Assert.Equal("4.4e3", (string?)cost["cost"]);
        Assert.False((bool)response["affordable"]!);
        Assert.Equal("Knowledge", (string?)response["shortResource"]!["name"]);
    }

    /// <summary>
    /// A preview always says which spell the live glyphs resolve to, and a layout with no price
    /// does not answer a question about paying one.
    /// </summary>
    /// <remarks>
    /// A round asked for an empty augment layout, which the game prices as an empty cost list, and
    /// read the resulting bare <c>affordable: yes</c> as "this add will work". It could not have:
    /// the game quotes that same empty price for a layout it resolves to nothing at all, so the
    /// word was carrying a claim it never had the evidence for.
    /// </remarks>
    [Fact]
    public void ExplicitLayoutPreviewNamesTheResolvedSpellAndDropsAffordabilityWithoutAPrice()
    {
        var preview = SpellWorkbenchPricePreview.Priced(
            RecipeId,
            RecipeId,
            Array.Empty<SpellWorkbenchPricePreviewCost>(),
            affordable: true,
            Guid.Empty);

        var response = GameMcpTestHarness.Json(
            GameMcpSpellWorkbenchProjection.ProjectPricePreview(in preview));

        Assert.Equal(
            new[] { "status", "resolvesTo", "costs" },
            response.Properties().Select(property => property.Name));
        Assert.Equal(RecipeId.ToString("D")[..6], (string?)response["resolvesTo"]!["uuid"]);
        Assert.Empty(response["costs"]!.Values<JObject>());
    }

    [Fact]
    public void ExplicitLayoutPreviewRefusalCarriesOnlyTheActionableReason()
    {
        var preview = SpellWorkbenchPricePreview.Refused(
            SpellWorkbenchPreflight.LayoutResolvedElsewhere,
            "This glyph layout resolves to Beam Burst, not Test Recipe.");

        var response = GameMcpTestHarness.Json(
            GameMcpSpellWorkbenchProjection.ProjectPricePreview(in preview));

        Assert.Equal(
            new[] { "status", "reasonCode", "reason" },
            response.Properties().Select(property => property.Name));
        Assert.Equal("unavailable", (string?)response["status"]);
        Assert.Equal("ERR_NOT_FOUND", (string?)response["reasonCode"]);
        Assert.Contains("resolves to Beam Burst", (string?)response["reason"]);
    }

    [Fact]
    public void UnavailableDiscoveryNamesTheNativeVisibilityPredicateWithoutSelectionCeremony()
    {
        var context = GameMcpTestHarness.Context(World(
            discovered: false,
            discoveryAffordable: true,
            hasEmptySlot: true,
            discoveryVisible: false));

        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            context, "spell-recipes", RecipeId.ToString("D")));
        var row = response["row"]!;

        Assert.False((bool)row["discover"]!["available"]!);
        Assert.Equal("ERR_LOCKED", (string?)row["discover"]!["reasonCode"]);
        Assert.Null(row["selected"]);
        Assert.Null(row["select"]);
    }

    [Fact]
    public void SuccessIsOnlyNamedPostStateWhileFailuresNameTheMissingOutcome()
    {
        var submission = new SpellWorkbenchSubmission(
            SpellWorkbenchPreflight.Proceeded,
            SpellWorkbenchNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1),
            "the requested recipe is discovered");
        var mapped = SpellWorkbenchActionResultMapper.Map(in submission);
        var before = World(
            discovered: false,
            discoveryAffordable: true,
            hasEmptySlot: true);
        var after = World(
            discovered: true,
            discoveryAffordable: true,
            hasEmptySlot: true);
        var command = Command("discover", "spellcraft", before);
        var terminal = GameMcpCommandResult.FromAction(
            in mapped,
            command.Kind,
            9,
            3,
            submission.Reason,
            GameMcpSpellWorkbenchProjection.Project(in submission));
        terminal = terminal.WithDetails(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after), command, terminal));

        var success = GameMcpTestHarness.Json(terminal.Project(command));
        Assert.Equal(new[]
            {
                "status", "uuid", "name",
                "discovered", "surface",
            },
            success.Properties().Select(property => property.Name));
        Assert.Equal("committed", (string?)success["status"]);
        Assert.Equal("Gather Knowledge", (string?)success["name"]);
        Assert.False((bool)success["discovered"]!["before"]!);
        Assert.True((bool)success["discovered"]!["after"]!);
        Assert.Equal("spellcraft", (string?)success["surface"]);
        Assert.Null(success["preflight"]);
        Assert.Null(success["before"]);
        Assert.Null(success["after"]);
        Assert.Null(success["receipt"]);
        Assert.DoesNotContain("payment", success.ToString(), StringComparison.OrdinalIgnoreCase);

        var refusedSubmission = SpellWorkbenchSubmission.Reject(
            SpellWorkbenchPreflight.WrongSelection,
            "the selected recipe changed");
        var refusal = GameMcpTestHarness.Json(
            GameMcpSpellWorkbenchProjection.Project(in refusedSubmission));
        Assert.Empty(refusal.Properties());

        var failedSubmission = new SpellWorkbenchSubmission(
            SpellWorkbenchPreflight.VerificationFailed,
            SpellWorkbenchNativeStage.Verification,
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(1, 1, 0),
            "the target was not discovered");
        var failure = GameMcpTestHarness.Json(
            GameMcpSpellWorkbenchProjection.Project(in failedSubmission));
        Assert.Equal("requested spell workbench transition",
            (string?)failure["missingOutcome"]);
        Assert.Single(failure.Properties());
    }

    [Fact]
    public void SettledProjectorReportsObservedDiscoveryAndLoadoutChanges()
    {
        var undiscovered = World(
            discovered: false,
            discoveryAffordable: true,
            hasEmptySlot: true);
        var discoveryCommand = Command("discover", before: undiscovered);
        var terminal = GameMcpCommandResult.Committed(
            "committed",
            9,
            3);

        var unchanged = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(undiscovered), discoveryCommand, terminal));

        Assert.False((bool)unchanged["discovered"]!["before"]!);
        Assert.False((bool)unchanged["discovered"]!["after"]!);
        Assert.Null(unchanged["surface"]);

        var beforeAdd = World(
            discovered: true,
            discoveryAffordable: true,
            hasEmptySlot: true);
        var afterAdd = World(
            discovered: true,
            discoveryAffordable: true,
            hasEmptySlot: true,
            equipped: true);
        var addCommand = Command("create", before: beforeAdd);
        var added = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(afterAdd), addCommand, terminal));

        Assert.Equal(1, (int)added["slot"]!["after"]!);
        Assert.Equal(0, (int)added["loadBudget"]!["used"]!["before"]!);
        Assert.Equal(1, (int)added["loadBudget"]!["used"]!["after"]!);
        Assert.Equal(3, (int)added["loadBudget"]!["maximum"]!);
        Assert.Null(added["discovered"]);
    }

    [Fact]
    public void ReadAdmissionAndActionUseOneSpellRecipeCapability()
    {
        var world = World(
            discovered: false,
            discoveryAffordable: true,
            hasEmptySlot: true);

        Assert.True(GameMcpEntityCapabilityMap.Contains(
            world,
            RecipeId,
            GameMcpCommandKind.SpellWorkbench,
            out var reason), reason);
        Assert.True(GameMcpEntityCapabilityMap.Supports(
            "spell-recipes",
            GameMcpCommandKind.SpellWorkbench));
    }

    [Fact]
    public void LocalhostMcpOwnsSpellWorkbenchOnlyForTheCurrentOperation()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile()).Current;
        using var ownership = new AutomataActionFamilyOwnership(registry);

        ownership.Refresh(configuration, lifecycleReady: true);
        Assert.False(ownership.TryCaptureSpellWorkbenchMutationPermit());

        Assert.True(ownership.TryBeginGameMcpOperation(
            GameMcpCommandKind.SpellWorkbench,
            "discover",
            out var scope,
            out var reason), reason);
        using (scope)
            Assert.True(ownership.TryCaptureSpellWorkbenchMutationPermit());
        Assert.False(ownership.TryCaptureSpellWorkbenchMutationPermit());
        Assert.Equal(
            "action_family_unavailable",
            GameMcpActionResultCodeNames.Name(
                SpellWorkbenchActionResultCodes.MutationPermitUnavailable,
                GameMcpCommandKind.SpellWorkbench));
    }

    private static GameMcpCommand Command(
        string mode,
        string payloadKey = "",
        GameWorldState? before = null) => new(
        1,
        GameMcpCommandKind.SpellWorkbench,
        9,
        3,
        mode,
        RecipeId,
        Guid.Empty,
        "SpellRecipeSO",
        1,
        payloadKey,
        string.Empty,
        false,
        frameContext: before is null ? null : GameMcpTestHarness.Context(before));

    private static GameWorldState World(
        bool discovered,
        bool discoveryAffordable,
        bool hasEmptySlot,
        bool equipped = false,
        bool discoveryVisible = true,
        bool canDiscover = true,
        int coreLevel = 7,
        bool usageBudget = false)
    {
        var glyphs = PublicationTable<WorldSpellRecipeGlyph>.Create(new[]
        {
            new WorldSpellRecipeGlyph(0, FirstGlyphId),
            new WorldSpellRecipeGlyph(1, SecondGlyphId),
        });
        var discoveryCosts = PublicationTable<WorldDiscoverableCost>.Create(new[]
        {
            new WorldDiscoverableCost(
                ResourceId,
                new BigDouble(4.4d, 3),
                new BigDouble(9d, 6)),
        });
        return new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "spell-recipes", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(new[]
            {
                new WorldSpellRecipe(
                    RecipeId,
                    discovered,
                    0,
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
                    glyphs,
                    discoveryCosts,
                    discoveryAffordable,
                    new WorldDiscoverableDecision(
                        discoveryVisible,
                        canDiscover,
                        discovered,
                        required: false,
                        affordable: discoveryAffordable,
                        costs: discoveryCosts)),
            }),
            Glyphs = PublicationTable<WorldGlyph>.Create(new[]
            {
                Glyph(SecondGlyphId, 3),
                Glyph(FirstGlyphId, coreLevel),
                Glyph(AugmentGlyphId, 1, augment: true),
            }),
            Resources = usageBudget
                ? PublicationTable<WorldResource>.Create(new[] { SpellWeightResource() })
                : PublicationTable<WorldResource>.Empty,
            SpellWorkbench = new WorldSpellWorkbench(
                equipped ? 1 : 0,
                3,
                hasEmptySlot,
                usageBudgetResourceIds: usageBudget
                    ? PublicationTable<Guid>.Create(new[] { ResourceId })
                    : null),
            SpellSlots = equipped
                ? PublicationTable<WorldSpellSlot>.Create(new[]
                {
                    new WorldSpellSlot(
                        0,
                        Guid.NewGuid(),
                        RecipeId,
                        true,
                        false,
                        false,
                        false,
                        false,
                        false,
                        false,
                        true,
                        true,
                        true,
                        1,
                        1,
                        BigDouble.Zero),
                })
                : PublicationTable<WorldSpellSlot>.Empty,
        };
    }

    /// <summary>
    /// One spell-weight resource: a bandwidth counter whose spendable pool is the room left under
    /// its ceiling, which is exactly what the usage gate compares a candidate spell against.
    /// </summary>
    private static WorldResource SpellWeightResource()
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
            ResourceId, new BigDouble(5), new BigDouble(8),
            true, BigDouble.Zero, BigDouble.Zero, new BigDouble(8),
            new BigDouble(8), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false, false,
            false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
        return new WorldResource(
            in reading, true, new BigDouble(3), 0.625, false, new BigDouble(5), BigDouble.Zero);
    }

    /// <summary>
    /// One glyph of either population, as the game authors them: an augment is discoverable and
    /// spent on spells, a core glyph is neither and is held off an authored requirement edge.
    /// </summary>
    private static WorldGlyph Glyph(Guid id, int level, bool augment = false) => new(
        id,
        level,
        0,
        0,
        true,
        augment,
        false,
        augment,
        false,
        false,
        0,
        BigDouble.Zero,
        BigDouble.Zero,
        BigDouble.Zero,
        maximumUsages: 1);
}
