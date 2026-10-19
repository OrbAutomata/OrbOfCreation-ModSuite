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
        // No screen discovers one spell: the row names the Spellcraft roll and carries no price.
        Assert.False((bool)exact["discover"]!["available"]!);
        Assert.StartsWith(
            "Only the roll on Magic > Spellbook > Unlock gives this",
            (string?)exact["discover"]!["reason"]);
        Assert.Null(exact["discover"]!["costs"]);
        // The authored core is the data's old composition shape, a list of retired unlocker ids;
        // no roll reads it, so neither does the row.
        Assert.Null(exact["coreGlyphs"]);
        Assert.Null(exact["composedOf"]);
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

    /// <summary>
    /// A learned spell's page carries what its card on Magic > Spellbook > Spells shows: the
    /// mastery bar as the screen prints it, and the Confirm Mastery button's price and readiness.
    /// </summary>
    [Fact]
    public void A_learned_spells_page_carries_its_cards_mastery_bar_and_confirm_mastery_button()
    {
        var ready = (JObject)GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(Named(World(
                discovered: true,
                discoveryAffordable: true,
                hasEmptySlot: true,
                masteryReady: true,
                masteryAffordable: true,
                masteryXp: 640,
                requiredMasteryXp: 600,
                held: 20,
                spendable: 20))),
            "spell-recipes",
            RecipeId.ToString("D")))["row"]!;
        var filling = (JObject)GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(Named(World(
                discovered: true,
                discoveryAffordable: true,
                hasEmptySlot: true,
                masteryXp: 122,
                requiredMasteryXp: 600))),
            "spell-recipes",
            RecipeId.ToString("D")))["row"]!;
        var unlearned = (JObject)GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(
                Named(World(discovered: false, discoveryAffordable: true, hasEmptySlot: true))),
            "spell-recipes",
            RecipeId.ToString("D")))["row"]!;

        Assert.Equal("640", (string?)ready["masteryXp"]);
        Assert.Equal("600", (string?)ready["masteryXpNeeded"]);
        var confirm = (JObject)ready["confirmMastery"]!;
        Assert.True((bool)confirm["available"]!);
        Assert.Equal(
            "Its leveling requirements, which the press settles.",
            (string?)confirm["notYetChecked"]);
        var cost = Assert.Single(confirm["costs"]!.Values<JObject>())!;
        Assert.Equal("12", (string?)cost["cost"]);
        Assert.Equal("20", (string?)cost["spendableAmount"]);
        Assert.True((bool)cost["affordable"]!);

        Assert.Equal("122", (string?)filling["masteryXp"]);
        Assert.Equal("600", (string?)filling["masteryXpNeeded"]);
        Assert.False((bool)filling["confirmMastery"]!["available"]!);
        Assert.Equal(
            "Gather Knowledge has no mastery level ready to buy: its mastery bar fills by " +
            "casting it.",
            (string?)filling["confirmMastery"]!["reason"]);

        Assert.Null(unlearned["masteryXp"]);
        Assert.Null(unlearned["confirmMastery"]);
    }

    /// <summary>
    /// The card prints the type XP a confirm grants beside each of the spell's type tracks: the
    /// per-level amount through that type's Type Xp and the player's All Spell Types Xp.
    /// </summary>
    [Fact]
    public void Confirm_mastery_names_the_type_xp_it_grants_each_of_the_spells_types()
    {
        var boosted = Guid.Parse("1b0c6f0e-7a57-4c55-8f0a-2d6c1f3e9a01");
        var plain = Guid.Parse("9e4d2a7c-3b61-4f08-b5d2-7c0a8e6f1b02");
        var excluded = Guid.Parse("c3a9f1d4-5e72-4b16-9d08-1f6b2e7a4c03");
        var world = World(
            discovered: true, discoveryAffordable: true, hasEmptySlot: true,
            masteryReady: true, masteryAffordable: true, held: 20, spendable: 20) with
        {
            SpellTypeXpPerMastery = 30d,
            DoubleVariables = PublicationTable<WorldNumberVariable>.Create(new[]
            {
                new WorldNumberVariable(KnownEntities.AllSpellTypeXpMod.Uuid, new BigDouble(120), true),
            }),
            SpellTypes = PublicationTable<WorldSpellType>.Create(new[]
            {
                CantripType(boosted, typeXpMod: 150d),
                CantripType(plain, typeXpMod: 100d),
                CantripType(excluded, typeXpMod: 100d),
            }.OrderBy(type => type.EntityId).ToArray()),
            SpellRelations = PublicationTable<WorldSpellRelation>.Create(new[]
            {
                new WorldSpellRelation(RecipeId, WorldSpellRelationKind.SpellType, 0, boosted),
                new WorldSpellRelation(RecipeId, WorldSpellRelationKind.SpellType, 1, plain),
                new WorldSpellRelation(RecipeId, WorldSpellRelationKind.NotSpellType, 0, excluded),
            }),
        };

        var grants = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
                GameMcpTestHarness.Context(world), "spell-recipes", RecipeId.ToString("D")))
            ["row"]!["confirmMastery"]!["grants"]!.Values<JObject>().ToArray();
        var uncollected = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
                GameMcpTestHarness.Context(world with { SpellTypeXpPerMastery = 0d }),
                "spell-recipes", RecipeId.ToString("D")))["row"]!["confirmMastery"]!;

        Assert.Equal(new[] { "54", "36" }, grants.Select(grant => (string?)grant!["typeXp"]));
        Assert.Null(uncollected["grants"]);
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
        Assert.Null(add["reasonCode"]);
        Assert.Equal(
            "Magic > Spellbook > Loadout is not unlocked yet, so the game draws no row for this.",
            (string?)add["reason"]);
        Assert.Null(add["verbDecides"]);
        Assert.Null(add["notYetChecked"]);
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

    /// <summary>
    /// The Sanctify moment: a spell weighs what the game charges, not what it is authored at.
    /// </summary>
    /// <remarks>
    /// <c>Spell.GetUsageCost()</c> adds <c>penaltyUsageCost</c> while the recipe's "use
    /// effectively" requirements are unmet. A round read authored upkeep 6, headroom 7 and
    /// <c>loadoutAdd: available: yes</c>, got a refusal with no number in it, and had to ask for a
    /// screenshot to learn the tile printed a red 12.
    /// </remarks>
    [Fact]
    public void A_recipe_reads_the_weight_the_game_charges_and_the_room_it_needs()
    {
        var unmet = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(World(
                discovered: true, discoveryAffordable: true, hasEmptySlot: true,
                usageBudget: true, held: 24, spendable: 7, authoredWeight: 6, penaltyWeight: 6,
                usageRequirementsMet: false)),
            "spell-recipes", RecipeId.ToString("D")))["row"]!;

        Assert.Equal(
            "12 Knowledge, including 6 more until its \"use effectively\" requirements are met",
            (string?)unmet["weight"]);
        Assert.False((bool)unmet["loadoutAdd"]!["available"]!);
        Assert.Equal(
            "It weighs 12 Knowledge and 7 is free. Remove loaded spells weighing 5 more, or " +
            "raise that allocation. → Loaded spells and their weights: world_list " +
            "category=spell-slots",
            (string?)unmet["loadoutAdd"]!["reason"]);

        var met = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(World(
                discovered: true, discoveryAffordable: true, hasEmptySlot: true,
                usageBudget: true, held: 24, spendable: 7, authoredWeight: 6, penaltyWeight: 6)),
            "spell-recipes", RecipeId.ToString("D")))["row"]!;

        Assert.Equal("6 Knowledge", (string?)met["weight"]);
        Assert.True((bool)met["loadoutAdd"]!["available"]!);
    }

    /// <summary>
    /// A Cantrip weighs what its tile prints: the type's live weight reduction comes off it.
    /// </summary>
    /// <remarks>
    /// With no glyph staged, <c>Spell.GetAllSpellTypes()</c> holds the recipe's authored types,
    /// because <c>SetupLimitedElementalType()</c> copies them into <c>augmentedSpellTypes</c>. A
    /// round read Channel Blaze at 3 and Channel Spark at 1 while the tile, the preview and the add
    /// all charged 2 and 0.
    /// </remarks>
    [Theory]
    [InlineData(3, "2 Knowledge")]
    [InlineData(1, "0 Knowledge")]
    public void A_cantrip_reads_the_weight_its_type_takes_off(int authored, string charged)
    {
        var cantripId = Guid.Parse("5d2c0d36-0d1c-4c84-9df2-55b0fd4a3a10");
        var world = World(
            discovered: true, discoveryAffordable: true, hasEmptySlot: true,
            usageBudget: true, held: 24, spendable: 7, authoredWeight: authored) with
        {
            SpellTypes = PublicationTable<WorldSpellType>.Create(new[] { CantripType(cantripId) }),
            SpellRelations = PublicationTable<WorldSpellRelation>.Create(new[]
            {
                new WorldSpellRelation(RecipeId, WorldSpellRelationKind.SpellType, 0, cantripId),
            }),
        };

        var row = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world), "spell-recipes", RecipeId.ToString("D")))["row"]!;

        Assert.Equal(charged, (string?)row["weight"]);
    }

    private static WorldSpellType CantripType(Guid id, double typeXpMod = 1d) => new(
        id,
        typeLevel: 0,
        typeXp: BigDouble.Zero,
        typeXpRequiredBase: 1d,
        augmentPowerMod: 1d,
        hasNoLevels: false,
        isElemental: false,
        isLoadoutUnique: false,
        hasNotTypeSignificance: false,
        isVisible: true,
        debugMode: false,
        typeXpMod: new BigDouble(typeXpMod),
        power: BigDouble.One,
        cooldownSpeed: BigDouble.One,
        cooldownTime: BigDouble.One,
        costMod: BigDouble.One,
        drainCostMod: BigDouble.One,
        durationMod: BigDouble.One,
        elementalResonance: BigDouble.One,
        augmentResonance: BigDouble.One,
        maxStacksMod: BigDouble.One,
        scalingMod: BigDouble.One,
        usageCostReduction: BigDouble.One,
        bonusCritRate: BigDouble.Zero,
        critEffectMod: BigDouble.One,
        critDurationMod: BigDouble.One,
        bonusDoubleCastRate: BigDouble.Zero,
        doubleCastEffectMod: BigDouble.One,
        chargeTimeMod: BigDouble.One,
        chargeEffectMod: BigDouble.One,
        chargeSpecialMod: BigDouble.One,
        bonusFlashRate: BigDouble.Zero,
        flashEffectMod: BigDouble.One);

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
        Assert.Null(decision["reasonCode"]);
        Assert.Equal("the bar is full (3 of 3)", (string?)decision["reason"]);
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
        // A sentence, not two mechanism words in a list under a key named after the verb.
        Assert.Null(decision["verbDecides"]);
        Assert.Equal(
            "Augments add their own weight and requirements, which the press settles.",
            (string?)decision["notYetChecked"]);
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
            new[] { "status", "usageAllocation", "loaded" },
            response.Properties().Select(property => property.Name));
        Assert.Equal("available", (string?)response["status"]);
        Assert.Equal(
            "→ Loaded spells and their weights: world_list category=spell-slots",
            (string?)response["loaded"]);
        var row = Assert.Single(response["usageAllocation"]!.Values<JObject>());
        Assert.Equal(
            new[] { "amount", "resource" },
            row.Properties().Select(property => property.Name));
        Assert.Equal("Knowledge", (string?)row["resource"]!["name"]);
        Assert.Equal("4.40e3", (string?)row["amount"]);
    }

    /// <summary>A preview that prices a replacement names the copy and the removal it holds after.</summary>
    [Fact]
    public void LoadPreviewThatReplacesALoadedCopySaysWhichAndWhatToDoFirst()
    {
        var preview = SpellWorkbenchLoadPreview.Admitted(
            RecipeId,
            new[] { new SpellWorkbenchUsageAllocation(ResourceId, new BigDouble(3)) },
            "Channel Spark in slot 2 — remove it, then add");

        var response = GameMcpTestHarness.Json(
            GameMcpSpellWorkbenchProjection.ProjectLoadPreview(in preview));

        Assert.Equal(
            new[] { "status", "replaces", "usageAllocation", "loaded" },
            response.Properties().Select(property => property.Name));
        Assert.Equal(
            "Channel Spark in slot 2 — remove it, then add",
            (string?)response["replaces"]);
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
            SpellWorkbenchActionResultCodes.AugmentSlotsExceeded,
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

    /// <summary>
    /// A glyph the caller named and the suite resolved is never "not found". A shut availability
    /// gate is a lock, and asking one glyph for more uses than it has is a ceiling.
    /// </summary>
    /// <remarks>
    /// Both refusals used to ship <c>ERR_NOT_FOUND</c>, which sent a caller holding a live uuid off
    /// to look for an entity that was in its hand.
    /// </remarks>
    [Fact]
    public void AResolvedGlyphIsRefusedAsAGateOrACeilingRatherThanAsAMiss()
    {
        var unavailable = SpellWorkbenchLoadPreview.Refused(
            SpellWorkbenchActionResultCodes.GlyphUnavailable, "Quick, in the screen's words.");
        var overCapacity = SpellWorkbenchLoadPreview.Refused(
            SpellWorkbenchActionResultCodes.GlyphUsagesExceeded, "Quick, in the screen's numbers.");

        var locked = GameMcpTestHarness.Json(
            GameMcpSpellWorkbenchProjection.ProjectLoadPreview(in unavailable));
        var limited = GameMcpTestHarness.Json(
            GameMcpSpellWorkbenchProjection.ProjectLoadPreview(in overCapacity));

        Assert.Equal("refused", (string?)locked["status"]);
        Assert.Equal("ERR_LOCKED", (string?)locked["reasonCode"]);
        Assert.Equal("Quick, in the screen's words.", (string?)locked["reason"]);
        Assert.Equal("refused", (string?)limited["status"]);
        Assert.Equal("ERR_LIMIT", (string?)limited["reasonCode"]);
        Assert.Equal("Quick, in the screen's numbers.", (string?)limited["reason"]);
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
        Assert.Null(row["discover"]!["reasonCode"]);
        Assert.Null(row["selected"]);
        Assert.Null(row["select"]);
    }

    [Fact]
    public void SuccessIsOnlyNamedPostStateWhileFailuresNameTheMissingOutcome()
    {
        var submission = NativeActionOutcome.Commit(
            new NativeMutationCallOutcome(1, 1, 1),
            "the requested spell is loaded");
        var mapped = submission.ToResult();
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
            9,
            3,
            GameMcpSpellWorkbenchProjection.Project(in submission));
        terminal = terminal.WithSettledPostState(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after), command, terminal));

        var success = GameMcpTestHarness.Json(terminal.Project(command));
        Assert.Equal(new[]
            {
                "status", "uuid", "name", "slot", "loadBudget",
            },
            success.Properties().Select(property => property.Name));
        Assert.Equal("committed", (string?)success["status"]);
        Assert.Equal("Gather Knowledge", (string?)success["name"]);
        Assert.Equal("empty", (string?)success["slot"]!["before"]);
        Assert.Equal(1, (int)success["slot"]!["after"]!);
        Assert.Null(success["preflight"]);
        Assert.Null(success["before"]);
        Assert.Null(success["after"]);
        Assert.Null(success["receipt"]);
        Assert.DoesNotContain("payment", success.ToString(), StringComparison.OrdinalIgnoreCase);

        var refusedSubmission = NativeActionOutcome.Reject(
            SpellWorkbenchActionResultCodes.WrongSelection,
            "the selected recipe changed");
        var refusal = GameMcpTestHarness.Json(
            GameMcpSpellWorkbenchProjection.Project(in refusedSubmission));
        Assert.Empty(refusal.Properties());

        var failedSubmission = NativeActionOutcome.Fault(
            SpellWorkbenchActionResultCodes.VerificationFailed,
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

        // An add moves the spell from no slot at all onto the bar, which is the pair a removal
        // already prints the other way round. Dropping the empty half left `slot: after=1`, a
        // line a caller cannot tell from a slot that did not move.
        Assert.Equal("empty", (string?)added["slot"]!["before"]);
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
        Assert.False(ownership.Family(GameMcpCommandKind.SpellWorkbench).TryCapture());

        Assert.True(ownership.TryBeginGameMcpOperation(
            GameMcpCommandKind.SpellWorkbench,
            "create",
            out var scope,
            out var reason), reason);
        using (scope)
            Assert.True(ownership.Family(GameMcpCommandKind.SpellWorkbench).TryCapture());
        Assert.False(ownership.Family(GameMcpCommandKind.SpellWorkbench).TryCapture());
        Assert.Equal(
            "action_family_unavailable",
            SpellWorkbenchActionResultCodes.MutationPermitUnavailable.Name);
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

    /// <summary>
    /// A bought mastery level says what its own press was charged, whatever else moved the balance.
    /// </summary>
    /// <remarks>
    /// A level bought during a Spark channel answered <c>resources: before=101, after=125</c>:
    /// the channel paid in more than the 9 Spark price took out, so the answer read as a gain and
    /// the price could not be read off it at all. The charge is the price the press paid, in the
    /// figure the screen draws it in — the nominal 4.5 divided by the resource's 50% quality.
    /// </remarks>
    [Fact]
    public void A_mastery_press_says_what_it_was_charged_not_how_the_balance_moved()
    {
        var before = World(
            discovered: true, discoveryAffordable: true, hasEmptySlot: true,
            masteryReady: true) with
        {
            Resources = PublicationTable<WorldResource>.Create(new[] { SparkResource(101) }),
        };
        var after = before with
        {
            Resources = PublicationTable<WorldResource>.Create(new[] { SparkResource(125) }),
        };
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.SpellLevel, 9, 3, "single", RecipeId, Guid.Empty,
            "SpellRecipeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 71));
        var level = 1;
        var submission = SpellLevelSubmission.Attempted(
            NativeMutationVerifier.Execute(
                "Spell level single", RecipeId.ToString("D"), "mastery level increased",
                () => level, () => level++, (previous, current) => current > previous),
            "Spell Leveling paid for a level", "and the level did not rise")
            .WithCharges(new[] { new ResourceCharge(SparkId, new BigDouble(4.5)) });
        var committed = GameMcpCommandResult.Committed(
            "committed", 9, 3, GameMcpSpellLevelProjection.Project(in submission, before));

        var wire = GameMcpTestHarness.Json(committed.WithSettledPostState(
                GameMcpWorldQuery.ProjectGameplayPostState(
                    GameMcpTestHarness.Context(after, generation: 72), command, committed))
            .Project(command));

        Assert.NotNull(wire["mastery"]);
        Assert.Null(wire["resources"]);
        var charge = Assert.Single(wire["charged"]!.Values<JObject>())!;
        Assert.Equal("9", (string?)charge["cost"]);
    }

    private static readonly Guid SparkId =
        Guid.Parse("5a9c0e1d-2b3f-4c6d-8e7f-90a1b2c3d4e5");

    private static WorldResource SparkResource(int held)
    {
        var rateInputs = default(RawResourceRateInputs);
        var traits = new RawResourceTraits(
            0d, 0d, 0d, false, false, false,
            bandwidthResource: false,
            invertedResource: false,
            excludeFromGlobals: false,
            startVisible: true,
            BigDouble.Zero, 0, 0, 0d, false, 0d,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            SparkId, new BigDouble(held), new BigDouble(500),
            true, BigDouble.Zero, BigDouble.Zero, new BigDouble(50),
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false, false,
            false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
        return new WorldResource(
            in reading, true, new BigDouble(held), 0d, false,
            new BigDouble(held), BigDouble.Zero);
    }

    private static GameWorldState World(
        bool discovered,
        bool discoveryAffordable,
        bool hasEmptySlot,
        bool equipped = false,
        bool discoveryVisible = true,
        bool canDiscover = true,
        bool usageBudget = false,
        bool loadoutScreenUnlocked = true,
        bool masteryReady = false,
        bool masteryAffordable = false,
        double masteryXp = 0d,
        double requiredMasteryXp = 0d,
        int held = 5,
        int spendable = 3,
        int authoredWeight = 0,
        int penaltyWeight = 0,
        bool usageRequirementsMet = true)
    {
        var glyphs = PublicationTable<WorldSpellRecipeGlyph>.Create(new[]
        {
            new WorldSpellRecipeGlyph(0, FirstGlyphId),
            new WorldSpellRecipeGlyph(1, SecondGlyphId),
        });
        var discoveryCosts = PublicationTable<WorldResourceCost>.Create(new[]
        {
            new WorldResourceCost(
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
                    new BigDouble(masteryXp),
                    0,
                    masteryReady,
                    masteryAffordable,
                    false,
                    false,
                    penaltyWeight,
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
                    new BigDouble(requiredMasteryXp),
                    glyphs,
                    discoveryCosts,
                    discoveryAffordable,
                    new WorldDiscoverableDecision(
                        discoveryVisible,
                        canDiscover,
                        discovered,
                        required: false,
                        affordable: discoveryAffordable,
                        costs: discoveryCosts),
                    usageRequirementsMet: usageRequirementsMet),
            }),
            SpellAuthoredCosts = authoredWeight > 0
                ? PublicationTable<WorldSpellAuthoredCost>.Create(new[]
                {
                    new WorldSpellAuthoredCost(
                        RecipeId, WorldSpellAuthoredCostKind.Usage, 0, ResourceId,
                        new BigDouble(authoredWeight)),
                })
                : PublicationTable<WorldSpellAuthoredCost>.Empty,
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
                ? PublicationTable<WorldResource>.Create(
                    new[] { SpellWeightResource(held, spendable) })
                : PublicationTable<WorldResource>.Empty,
            MasteryCosts = masteryReady
                ? PublicationTable<WorldMasteryCost>.Create(new[]
                {
                    new WorldMasteryCost(
                        RecipeId, 0, ResourceId, new BigDouble(12), masteryAffordable),
                })
                : PublicationTable<WorldMasteryCost>.Empty,
            SpellWorkbench = new WorldSpellWorkbench(
                // The game holds no bar that is out of room and has slots to spare, so neither
                // does the fixture: a full bar is full, and the refusal that quotes both sides of
                // it would otherwise quote a pair the game never publishes.
                hasEmptySlot ? (equipped ? 1 : 0) : 3,
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
    private static WorldResource SpellWeightResource(int held = 5, int spendable = 3)
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
            ResourceId, new BigDouble(held), new BigDouble(8),
            true, BigDouble.Zero, BigDouble.Zero, new BigDouble(8),
            new BigDouble(8), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false, false,
            false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
        return new WorldResource(
            in reading, true, new BigDouble(spendable), 0.625, false,
            new BigDouble(held), BigDouble.Zero);
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
