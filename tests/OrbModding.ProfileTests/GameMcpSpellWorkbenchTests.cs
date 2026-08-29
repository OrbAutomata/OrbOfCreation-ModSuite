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

    /// <summary>The books the two core ids are the internal half of, as the build authors them.</summary>
    private static readonly Guid FirstBookId =
        Guid.Parse("c4104148-b464-4123-acd0-db63d34d9a2c");
    private static readonly Guid SecondBookId =
        Guid.Parse("a9a4dd71-53cf-405c-b9c3-6d252308fe9b");

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
            new[] { "mode", "uuid" },
            schema["required"]!.Values<string>().ToArray());
        Assert.Equal(
            new[] { "preview", "confirm", "offer_initiate", "offer_select", "offer_confirm", "offer_reroll" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>().ToArray());
        Assert.Null(schema["properties"]!["expectedNativeType"]);
        Assert.Null(schema["properties"]!["worldGeneration"]);
        Assert.Null(schema["properties"]!["detail"]);
        Assert.Null(schema["properties"]!["verbosity"]);
    }

    /// <summary>
    /// The press names what it wants discovered and nothing else — the screen follows from it.
    /// </summary>
    [Fact]
    public void ConfirmAsksForTheThingItWouldDiscoverAndNothingElse()
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
            "field 'uuid' is missing", GameMcpTestHarness.Page(response));
    }

    [Fact]
    public void ListIsLeanWhileGetExposesTheDiscoveryDecision()
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
        // The button carries no composition, so neither does the row that describes it.
        Assert.Null(exact["discover"]!["surface"]);
        Assert.Null(exact["discover"]!["components"]);
        // The authored core is a list of retired unlocker ids, and each is the internal half of a
        // Recipe Book. The row names the books, which are the tiles a player owns, in slot order.
        Assert.Null(exact["coreGlyphs"]);
        var books = exact["composedOf"]!.Values<JObject>().ToArray();
        Assert.Equal(new[] { "Brew", "Insight" },
            books.Select(book => (string?)book!["book"]!["name"]));
        Assert.Equal(new[] { true, true },
            books.Select(book => (bool)book!["owned"]!));
        var cost = Assert.Single(exact["discover"]!["costs"]!.Values<JObject>())!;
        Assert.Equal("Knowledge", (string?)cost["resource"]!["name"]);
        Assert.Equal("4.4e3", (string?)cost["cost"]);
        Assert.Equal("9e6", (string?)cost["spendableAmount"]);
    }

    /// <summary>
    /// Which spell recipes are discovered is a question the listings answer, on both the page that
    /// carries the column and the search that reaches it — and a category that spells discovery as
    /// its lifecycle state is told so rather than answered with a second word for one fact.
    /// </summary>
    /// <remarks>
    /// A round needed one discovered-but-unequipped recipe out of sixty-five, of which twenty-four
    /// were discovered. Neither listing could narrow it, so the round guessed three names off how
    /// early they sounded and paid a 4,100-byte detail read to learn that two of the three were
    /// wrong for the purpose.
    /// </remarks>
    [Fact]
    public void The_listings_narrow_spell_recipes_to_one_side_of_the_discovery_verdict()
    {
        var context = GameMcpTestHarness.Context(World(
            discovered: true, discoveryAffordable: true, hasEmptySlot: true));

        var listed = GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            context, "spell-recipes", 0, 50, discovered: true));
        var listedOther = GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            context, "spell-recipes", 0, 50, discovered: false));
        var found = GameMcpTestHarness.Json(GameMcpWorldQuery.Search(
            context, string.Empty, 0, 50, "spell-recipes", discoveredFilter: true));
        var foundOther = GameMcpTestHarness.Json(GameMcpWorldQuery.Search(
            context, string.Empty, 0, 50, "spell-recipes", discoveredFilter: false));

        Assert.True((bool)Assert.Single(listed["rows"]!.Values<JObject>())!["discovered"]!);
        Assert.Equal(1, (int)listed["total"]!);
        Assert.Empty(listedOther["rows"]!.Values<JObject>());
        Assert.Equal(0, (int)listedOther["total"]!);
        Assert.Single(found["rows"]!.Values<JObject>());
        Assert.Empty(foundOther["rows"]!.Values<JObject>());

        // A category whose rows carry no such column is told which ones do, on both verbs, rather
        // than being handed a silently unfiltered page.
        var listRefusal = GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            context, "resources", 0, 50, discovered: true));
        var searchRefusal = GameMcpTestHarness.Json(GameMcpWorldQuery.Search(
            context, string.Empty, 0, 50, "rituals", discoveredFilter: true));

        Assert.Equal("ERR_INPUT", (string?)listRefusal["reasonCode"]);
        Assert.Equal(
            "the discovered filter narrows a page by the discovered column its rows carry, and " +
            "rows in resources carry none; the categories whose rows carry one are " +
            "spell-recipes, time-runes",
            (string?)listRefusal["reason"]);
        Assert.Equal("ERR_INPUT", (string?)searchRefusal["reasonCode"]);
        Assert.Equal(
            "the categories whose rows spell discovery in that word are spell-recipes, " +
            "time-runes, so it cannot narrow rituals; the rest spell it as their state, which " +
            "the state filter narrows",
            (string?)searchRefusal["reason"]);
    }

    /// <summary>
    /// The two gates a mastery purchase can meet answer in two sentences: no level is ready, or one
    /// is and the price is short by a named amount.
    /// </summary>
    /// <remarks>
    /// One sentence stood for both — <c>This spell has no ready mastery level whose cost you can
    /// afford</c> — and a round read it, could not tell which half it had met, and spent a mastery
    /// listing, two navigations, a refused tooltip, a screenshot and a screen read finding out.
    /// The research verb had answered the same shape of question two calls earlier in eighty-nine
    /// bytes, and the same round called that the friendliest unaffordable wording it saw.
    /// </remarks>
    [Fact]
    public void A_refused_mastery_level_says_which_of_the_two_gates_stopped_it()
    {
        var notReady = GameMcpWorldQuery.MasteryRefusalReason(
            Named(World(discovered: true, discoveryAffordable: true, hasEmptySlot: true)),
            RecipeId);
        var shortfall = GameMcpWorldQuery.MasteryRefusalReason(
            Named(World(
                discovered: true,
                discoveryAffordable: true,
                hasEmptySlot: true,
                masteryReady: true)),
            RecipeId);

        Assert.Equal(
            "Gather Knowledge has no mastery level ready to buy: its mastery bar fills by " +
            "casting it.",
            notReady);
        // The gap is the spendable pool, not the raw holding: this resource is bandwidth, so the
        // three it can still commit is what a purchase would actually draw on.
        Assert.Equal("Needs 12 Knowledge (have 3).", shortfall);
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
        Assert.True((bool)row["loadoutAdd"]!["acceptsAugments"]!);
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
    /// Locked, unaffordable and full are three different answers. A screen the game has not
    /// unlocked draws no row to press, so an empty loadout slot proves nothing about the call.
    /// </summary>
    [Fact]
    public void A_locked_loadout_screen_refuses_the_add_in_the_screens_own_words()
    {
        var context = GameMcpTestHarness.Context(World(
            discovered: true,
            discoveryAffordable: true,
            hasEmptySlot: true,
            loadoutScreenUnlocked: false));

        var add = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            context, "spell-recipes", RecipeId.ToString("D")))["row"]!["loadoutAdd"]!;

        Assert.False((bool)add["available"]!);
        Assert.True((bool)add["acceptsAugments"]!);
        Assert.Equal("ERR_LOCKED", (string?)add["reasonCode"]);
        Assert.Equal(
            "The screen this action lives on is not unlocked yet.",
            (string?)add["reason"]);
        Assert.Null(add["verbDecides"]);
        // The vocabulary the call needs still rides on the refusal: the page that refuses you is
        // still the page that has to teach the call.
        Assert.Single(add["augmentOptions"]!.Values<JObject>());
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

    [Fact]
    public void StructurallyUnavailableLoadoutAddWithholdsPriceClaimsButNotTheVocabulary()
    {
        var context = GameMcpTestHarness.Context(World(
            discovered: true,
            discoveryAffordable: true,
            hasEmptySlot: false));

        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            context, "spell-recipes", RecipeId.ToString("D")));
        var decision = response["row"]!["loadoutAdd"]!;

        Assert.False((bool)decision["available"]!);
        Assert.Equal("ERR_LIMIT", (string?)decision["reasonCode"]);
        Assert.Equal("Every slot in this loadout is in use.", (string?)decision["reason"]);
        Assert.Null(decision["affordable"]);
        Assert.Null(decision["costs"]);
        Assert.Null(decision["verbDecides"]);

        // The call this page is refusing is still the call it has to teach.
        Assert.Single(decision["augmentOptions"]!.Values<JObject>());
    }

    /// <summary>
    /// The Beam Burst moment: a core glyph at level zero is not a rule the game's add path has, so
    /// the page does not refuse on it — and where it does say yes, it names the gates only the verb
    /// can settle rather than promising they pass.
    /// </summary>
    /// <remarks>
    /// A round levelled two core glyphs on this page's advice, watched the predicate flip to
    /// <c>available: yes</c>, and got the identical refusal from the verb both times. Neither the
    /// verb nor the game's own create button reads a core glyph's level anywhere; the sentence the
    /// player was acting on was this suite's own, attributed to the game.
    ///
    /// The list is two. <c>unique-spell rule</c> named a gate the caller had no way to settle, and
    /// the world publishes the fact it reads: every equipped instance of this recipe is on this
    /// same row under <c>equipped</c>, each carrying the game's own <c>isLoadoutUnique</c>. Naming
    /// a decided fact as pending is the same defect as predicting an undecidable one, one register
    /// over. <c>glyph layout resolution</c> and <c>creation price</c> went with the machinery that
    /// invented them: the row passes the recipe and the game charges nothing for the press.
    /// </remarks>
    [Fact]
    public void LoadoutAddNeitherInventsACoreGlyphLevelRuleNorPromisesTheVerbsLiveGates()
    {
        var context = GameMcpTestHarness.Context(World(
            discovered: true,
            discoveryAffordable: true,
            hasEmptySlot: true));

        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            context, "spell-recipes", RecipeId.ToString("D")));
        var decision = response["row"]!["loadoutAdd"]!;

        Assert.True((bool)decision["available"]!);
        Assert.Null(decision["reasonCode"]);
        Assert.Equal(
            new[]
            {
                "usage budget",
                "augment requirements",
            },
            decision["verbDecides"]!.Values<string>());
    }

    /// <summary>
    /// The preview answers the one budget a load is weighed against, and nothing about paying.
    /// </summary>
    /// <remarks>
    /// A round read a creation price off this preview, paid it on the add, and the game charged
    /// nothing — there is no such price. What a load does draw is the usage allocation the loaded
    /// spell holds, which is what the answer carries now.
    /// </remarks>
    [Fact]
    public void LoadPreviewNamesTheUsageAllocationTheLoadedSpellWouldHold()
    {
        var preview = SpellWorkbenchLoadPreview.Admitted(
            RecipeId,
            new[] { new SpellWorkbenchUsageAllocation(ResourceId, new BigDouble(4400)) });

        var response = GameMcpTestHarness.Json(
            GameMcpSpellWorkbenchProjection.ProjectLoadPreview(in preview));

        Assert.Equal(
            new[] { "status", "usageAllocation" },
            response.Properties().Select(property => property.Name));
        Assert.Equal("available", (string?)response["status"]);
        var row = Assert.Single(response["usageAllocation"]!.Values<JObject>());
        Assert.Equal(
            new[] { "amount", "resource" },
            row.Properties().Select(property => property.Name));
        Assert.Equal("Knowledge", (string?)row["resource"]!["name"]);
        Assert.Equal("4.4e3", (string?)row["amount"]);
    }

    /// <summary>An admitted load with no allocation says so by carrying no allocation block.</summary>
    [Fact]
    public void LoadPreviewWithoutAnAllocationCarriesNothingBesideItsStatus()
    {
        var preview = SpellWorkbenchLoadPreview.Admitted(
            RecipeId,
            Array.Empty<SpellWorkbenchUsageAllocation>());

        var response = GameMcpTestHarness.Json(
            GameMcpSpellWorkbenchProjection.ProjectLoadPreview(in preview));

        Assert.Equal(
            new[] { "status" },
            response.Properties().Select(property => property.Name));
        Assert.Equal("available", (string?)response["status"]);
    }

    [Fact]
    public void LoadPreviewRefusalCarriesOnlyTheActionableReason()
    {
        var preview = SpellWorkbenchLoadPreview.Refused(
            SpellWorkbenchPreflight.AugmentSlotsExceeded,
            "This layout puts 3 different augments on Test Recipe and the game allows 1 at once " +
            "(Max Spell Augment Slots). Ask for 1 different augments or fewer, or raise that " +
            "limit first.");

        var response = GameMcpTestHarness.Json(
            GameMcpSpellWorkbenchProjection.ProjectLoadPreview(in preview));

        Assert.Equal(
            new[] { "status", "reasonCode", "reason" },
            response.Properties().Select(property => property.Name));
        Assert.Equal("refused", (string?)response["status"]);
        Assert.Equal("ERR_LIMIT", (string?)response["reasonCode"]);
        Assert.Contains("Max Spell Augment Slots", (string?)response["reason"]);
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
            "the requested spell is loaded");
        var mapped = SpellWorkbenchActionResultMapper.Map(in submission);
        var before = World(
            discovered: true,
            discoveryAffordable: true,
            hasEmptySlot: true);
        var after = World(
            discovered: true,
            discoveryAffordable: true,
            hasEmptySlot: true,
            equipped: true);
        var command = Command("create", before: before);
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
                "status", "uuid", "name", "slot", "loadBudget",
            },
            success.Properties().Select(property => property.Name));
        Assert.Equal("committed", (string?)success["status"]);
        Assert.Equal("Gather Knowledge", (string?)success["name"]);
        Assert.Null((int?)success["slot"]!["before"]);
        Assert.Equal(1, (int)success["slot"]!["after"]!);
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
    public void SettledProjectorReportsObservedLoadoutChanges()
    {
        var terminal = GameMcpCommandResult.Committed(
            "committed",
            9,
            3);

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
            "create",
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

    private static GameWorldState Named(GameWorldState world) =>
        world with { EntityIdentities = GameMcpTestHarness.EntityCatalog };

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
        bool usageBudget = false,
        bool loadoutScreenUnlocked = true,
        bool masteryReady = false)
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
            Views = PublicationTable<WorldView>.Create(new[]
            {
                new WorldView(
                    KnownEntities.MagicSpellbookLoadout.Uuid,
                    false,
                    false,
                    loadoutScreenUnlocked),
                new WorldView(KnownEntities.MagicSpellbookLearn.Uuid, false, false, true),
            }),
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(new[]
            {
                new WorldSpellRecipe(
                    RecipeId,
                    discovered,
                    0,
                    BigDouble.Zero,
                    0,
                    masteryReady,
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
            // Only the augment is a row. The recipe's two core entries are retired unlocker ids the
            // world publishes no glyph for, which is why a core glyph's level cannot reach this page.
            AugmentGlyphs = PublicationTable<WorldGlyph>.Create(new[]
            {
                Glyph(AugmentGlyphId, 1),
            }),
            RecipeBookGlyphs = PublicationTable<WorldRecipeBookGlyph>.Create(new[]
            {
                new WorldRecipeBookGlyph(SecondGlyphId, SecondBookId),
                new WorldRecipeBookGlyph(FirstGlyphId, FirstBookId),
            }.OrderBy(edge => edge.GlyphId).ToArray()),
            RecipeBooks = PublicationTable<WorldRecipeBook>.Create(new[]
            {
                new WorldRecipeBook(SecondBookId, true),
                new WorldRecipeBook(FirstBookId, true),
            }.OrderBy(book => book.EntityId).ToArray()),
            Resources = usageBudget || masteryReady
                ? PublicationTable<WorldResource>.Create(new[] { SpellWeightResource() })
                : PublicationTable<WorldResource>.Empty,
            MasteryCosts = masteryReady
                ? PublicationTable<WorldMasteryCost>.Create(new[]
                {
                    new WorldMasteryCost(RecipeId, 0, ResourceId, new BigDouble(12), false),
                })
                : PublicationTable<WorldMasteryCost>.Empty,
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

    /// <summary>One Augment Glyph, as the game authors them: discoverable and spent on spells.</summary>
    private static WorldGlyph Glyph(Guid id, int level) => new(
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
        0,
        BigDouble.Zero,
        BigDouble.Zero,
        BigDouble.Zero,
        maximumUsages: 1);
}
