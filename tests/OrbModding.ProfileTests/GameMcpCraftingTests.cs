using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;
using static OrbModding.ProfileTests.GameMcpTestHarness;

namespace OrbModding.ProfileTests;

public sealed class GameMcpCraftingTests
{
    private static readonly Guid RecipeId =
        Guid.Parse("f2000000-0000-0000-0000-000000000001");
    private static readonly Guid QueueId =
        Guid.Parse("f2000000-0000-0000-0000-000000000002");
    private static readonly Guid AutomationQueueId =
        Guid.Parse("f2000000-0000-0000-0000-000000000004");
    private static readonly Guid ResourceId =
        Guid.Parse("f2000000-0000-0000-0000-000000000003");

    [Fact]
    public void ToolAdvertisesOneRecipeUuidMutation()
    {
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_craft");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(new[] { "uuid" }, schema["required"]!.Values<string>());
        Assert.NotNull(schema["properties"]!["uuid"]);
        Assert.NotNull(schema["properties"]!["mode"]);
        Assert.Equal(
            new[] { "craft", "automate", "cancel_manual", "cancel_automation" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>());
        var amount = schema["properties"]!["amount"]!["anyOf"]!;
        Assert.Equal(1, (int)amount[0]!["minimum"]!);
        Assert.Null(amount[0]!["maximum"]);
        Assert.Equal(new[] { "max" }, amount[1]!["enum"]!.Values<string>());
        var operation = GameMcpProtocolRouter.BuildOperation(
            "game_craft",
            new JObject
            {
                ["mode"] = "automate",
                ["uuid"] = RecipeId.ToString("D"),
            });
        Assert.Equal(GameMcpOperationClass.Gameplay, operation.Classification);
        Assert.Equal("automate", operation.Mode);
    }

    [Fact]
    public void ValidationNamesMissingUuidAndRejectsRemovedGenerationMetadata()
    {
        var inbox = new GameMcpFrameInbox();
        var router = new GameMcpProtocolRouter(inbox);
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = "game_craft",
                ["arguments"] = new JObject(),
            }));
        var accepted = router.Handle(GameMcpAcceptanceFixture.Request(
            2,
            "tools/call",
            new JObject
            {
                ["name"] = "game_craft",
                ["arguments"] = new JObject
                {
                    ["uuid"] = RecipeId.ToString("D"),
                    ["worldGeneration"] = 17,
                },
            }));

        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: required " +
            "field 'uuid' is missing", GameMcpTestHarness.Page(missing));
        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: field " +
            "'worldGeneration' is not accepted by game_craft", GameMcpTestHarness.Page(accepted));
        Assert.Empty(inbox.ClaimPending());
    }

    [Fact]
    public void AQueueBeyondTheIntegerRangeReadsAsTheScreenShowsIt()
    {
        var row = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            Context(queuedAmount: 3.96e9),
            "crafting-recipes",
            RecipeId.ToString("D")), Identities())["row"]!;

        Assert.Equal("3.96e9", (string?)row["queuedAmount"]);
    }

    [Fact]
    public void ACancelledQueueBeyondTheIntegerRangeReadsAsTheScreenShowsIt()
    {
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Crafting, 15, 8, "cancel_manual", RecipeId, Guid.Empty,
            "CraftingRecipeSO", 1, string.Empty, string.Empty, false,
            frameContext: Context(queuedAmount: 3.96e9));
        var committed = GameMcpCommandResult.Committed("committed", 15, 8);

        var postState = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            Context(queuedAmount: 0), command, committed), Identities());

        Assert.Equal("3.96e9", (string?)postState["queued"]!["before"]);
        Assert.Equal("0", (string?)postState["queued"]!["after"]);
    }

    [Fact]
    public void PreDecisionRowCarriesNamedExactCostsHoldingsAffordabilityAndQueueState()
    {
        var result = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            Context(),
            "crafting-recipes",
            RecipeId.ToString("D")), Identities());

        Assert.Equal("available", (string?)result["status"]);
        Assert.Null(result["worldGeneration"]);
        var row = result["row"]!;
        Assert.Equal("Craft Sigils", (string?)result["name"]);
        Assert.Equal(GameMcpTestHarness.Handle(RecipeId), (string?)result["uuid"]);
        Assert.Equal("queue_stack", (string?)row["execution"]);
        Assert.Equal(2, (int)row["purchaseAmount"]!);
        Assert.Equal("4", (string?)row["queuedAmount"]);
        Assert.True((bool)row["canStart"]!);
        Assert.Equal("Sigil Queue", (string?)row["queue"]!["queue"]!["name"]);
        Assert.Equal(1, (int)row["queue"]!["used"]!);
        Assert.Equal(3, (int)row["queue"]!["maximum"]!);
        var cost = Assert.Single(row["nextCosts"]!).Value<JObject>()!;
        Assert.Equal("Arcane Dust", (string?)cost["resource"]!["name"]);
        Assert.Equal("75", (string?)cost["cost"]);
        Assert.Equal("900", (string?)cost["spendableAmount"]);
        Assert.True((bool)cost["affordable"]!);
        Assert.True((bool)row["cancelManual"]!["available"]!);
        // Repetitions 3 means a badge of 4: the two coordinates are only equal at 1 and 2.
        Assert.Equal("4", (string?)row["automation"]!["amount"]);
        Assert.Equal(3, (int)row["automation"]!["repetitions"]!);
        Assert.True((bool)row["automation"]!["available"]!);
        Assert.True((bool)row["automation"]!["canCancel"]!);
        Assert.Null(row["automation"]!["reason"]);
        Assert.Null(row["automation"]!["reasonCode"]);
    }

    [Fact]
    public void The_scan_row_names_the_starting_amount_the_detail_row_names()
    {
        // The scan row used to fall through to the reflected projector and publish the raw native
        // path reading.startingQuantity, so the same fact had two names and two shapes.
        var listed = Assert.Single(GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            Context(), "crafting-recipes", 0, 10).Freeze(), Identities())["rows"]!.Values<JObject>())!;
        var detail = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            Context(), "crafting-recipes", RecipeId.ToString("D")), Identities())["row"]!;

        Assert.Equal((string?)detail["startingAmount"], (string?)listed["startingAmount"]);
        Assert.Null(listed["reading"]);
    }

    [Fact]
    public void BlockedAutomationPutsTheCodeInReasonCodeAndProseInReason()
    {
        var row = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            Context(canAutomate: false),
            "crafting-recipes",
            RecipeId.ToString("D")), Identities())["row"]!["automation"]!;

        Assert.False((bool)row["available"]!);
        Assert.Null(row["reasonCode"]);
        Assert.Equal("Every automation slot on this queue is in use.", (string?)row["reason"]);
        Assert.Equal(1, (int)row["used"]!);
        Assert.Equal(3, (int)row["maximum"]!);
    }

    [Fact]
    public void An_undiscovered_recipe_is_never_told_its_automation_queue_is_full()
    {
        var row = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            Context(canAutomate: false, automationReasonCode: "hidden_or_undiscovered"),
            "crafting-recipes",
            RecipeId.ToString("D")), Identities())["row"]!["automation"]!;

        Assert.False((bool)row["available"]!);
        Assert.Null(row["reasonCode"]);
        Assert.Equal(
            GameMcpDecisionReason.For("hidden_or_undiscovered"),
            (string?)row["reason"]);
    }

    [Fact]
    public void Both_queues_name_themselves_and_list_what_occupies_their_slots()
    {
        var row = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            Context(),
            "crafting-recipes",
            RecipeId.ToString("D")), Identities())["row"]!;

        var manual = row["queue"]!;
        Assert.Equal("Sigil Queue", (string?)manual["queue"]!["name"]);
        var manualSlot = Assert.Single(manual["slots"]!).Value<JObject>()!;
        Assert.Equal(1, (int?)manualSlot["slot"]);
        Assert.Equal("Craft Sigils", (string?)manualSlot["recipe"]!["name"]);
        Assert.Equal("4", (string?)manualSlot["amount"]);
        Assert.Equal("manual", (string?)manualSlot["repetitions"]);

        var automation = row["automation"]!;
        Assert.Equal("Auto Sigil Queue", (string?)automation["queue"]!["name"]);
        var automationSlot = Assert.Single(automation["slots"]!).Value<JObject>()!;
        Assert.Equal(2, (int?)automationSlot["slot"]);
        Assert.Equal("Craft Sigils", (string?)automationSlot["recipe"]!["name"]);
        Assert.Equal("4", (string?)automationSlot["amount"]);
        Assert.Equal(3, (int?)automationSlot["repetitions"]);
    }

    [Fact]
    public void A_queue_that_was_read_and_holds_nothing_lists_no_slots_rather_than_going_absent()
    {
        var row = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            Context(withQueueEntries: false),
            "crafting-recipes",
            RecipeId.ToString("D")), Identities())["row"]!;

        Assert.Empty(row["queue"]!["slots"]!);
        Assert.Empty(row["automation"]!["slots"]!);
    }

    [Fact]
    public void A_repeating_recipe_whose_queue_entry_is_missing_says_so_instead_of_reading_zero()
    {
        // amount is joined from the entry that draws the badge. A skipped or partially collected
        // queue-entry category would otherwise ship amount 0 beside repetitions 3 — a pair the
        // strip never shows, and one no caller can tell from a genuinely idle recipe.
        var automation = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            Context(withQueueEntries: false),
            "crafting-recipes",
            RecipeId.ToString("D")), Identities())["row"]!["automation"]!;

        Assert.Null(automation["amount"]);
        Assert.Equal(3, (int)automation["repetitions"]!);
        Assert.Null(automation["amountUnavailable"]!["reasonCode"]);
    }

    [Fact]
    public void An_automation_commit_whose_entry_is_missing_publishes_no_settled_pair()
    {
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Crafting, 15, 8, "automate", RecipeId, Guid.Empty,
            "CraftingRecipeSO", 1, string.Empty, string.Empty, false, frameContext: Context(automationRepetitions: 3));
        var committed = GameMcpCommandResult.Committed("committed", 15, 8);

        var postState = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            Context(automationRepetitions: 5, withQueueEntries: false), command, committed), Identities());

        Assert.Null(postState["amount"]);
        Assert.Equal("ERR_NOT_FOUND", (string?)postState["amountUnavailable"]!["reasonCode"]);
    }

    [Fact]
    public void QueueContentsAreOrderedNamedAndSayWhichEntriesRepeat()
    {
        var result = GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            Context(),
            "crafting-queue-entries",
            0,
            10), Identities());

        Assert.Null(result["status"]);
        var rows = result["rows"]!.Values<JObject>().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal("Sigil Queue", (string?)rows[0]?["queue"]?["name"]);
        Assert.Equal("Craft Sigils", (string?)rows[0]?["recipe"]?["name"]);
        Assert.Equal(1, (int?)rows[0]?["slot"]);
        Assert.Equal("4", (string?)rows[0]?["amount"]);

        // One column carries both answers, so the page never spends a second one saying that the
        // first does not apply here.
        Assert.Equal("manual", (string?)rows[0]?["repetitions"]);
        Assert.Null(rows[0]?["automatic"]);
        Assert.Equal("Auto Sigil Queue", (string?)rows[1]?["queue"]?["name"]);
        Assert.Equal(2, (int?)rows[1]?["slot"]);
        Assert.Equal("4", (string?)rows[1]?["amount"]);
        Assert.Equal(3, (int?)rows[1]?["repetitions"]);
        Assert.Null(rows[1]?["automatic"]);
    }

    [Fact]
    public void Amount_defaults_to_one_craft_and_only_craft_reads_it()
    {
        var plain = GameMcpProtocolRouter.BuildOperation(
            "game_craft", new JObject { ["uuid"] = RecipeId.ToString("D") });
        var batch = GameMcpProtocolRouter.BuildOperation(
            "game_craft",
            new JObject { ["uuid"] = RecipeId.ToString("D"), ["amount"] = 1_000_000 });
        var automate = GameMcpProtocolRouter.BuildOperation(
            "game_craft",
            new JObject
            {
                ["mode"] = "automate",
                ["uuid"] = RecipeId.ToString("D"),
                ["amount"] = 2,
            });

        Assert.Equal(1, plain.Amount);
        Assert.Equal(1_000_000, batch.Amount);
        Assert.Equal(1, automate.Amount);
    }

    [Fact]
    public void Max_is_the_only_word_amount_takes()
    {
        var max = GameMcpProtocolRouter.BuildOperation(
            "game_craft", new JObject { ["uuid"] = RecipeId.ToString("D"), ["amount"] = "max" });

        Assert.Equal("max", max.SerializedValue);
        Assert.Equal(1, max.Amount);
        var refused = Assert.Throws<GameMcpInvalidParamsException>(() =>
            GameMcpProtocolRouter.BuildOperation(
                "game_craft",
                new JObject { ["uuid"] = RecipeId.ToString("D"), ["amount"] = "all" }));
        Assert.Equal("amount must be a whole number of crafts or \"max\"", refused.Message);
    }

    /// <summary>
    /// A count recipe queues the whole ask in one submission of the one craft GameAction, and the
    /// answer is the count its own sentinel verified.
    /// </summary>
    [Fact]
    public void A_count_craft_queues_the_whole_ask_in_one_submission()
    {
        var asks = new System.Collections.Generic.List<int>();
        var result = GameMcpCraftingProjection.Press(
            CraftCommand(3), 15, 8, false, NoShortfall,
            remaining =>
            {
                asks.Add(remaining);
                return Landed(CraftingPlayerPostcondition.QueueAdmitted, remaining);
            });
        var postState = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            Context(queuedAmount: 0), CraftCommand(3), result), Identities());

        Assert.Equal(new[] { 3 }, asks);
        Assert.Equal("committed", result.Status);
        Assert.Equal("Craft Sigils", (string?)postState["name"]);
        Assert.Equal("3", (string?)postState["queued"]);
        Assert.Null(postState["nextCosts"]);
        Assert.Null(postState["receipt"]);
    }

    [Fact]
    public void A_scroll_craft_is_one_submission_per_scroll()
    {
        var asks = new System.Collections.Generic.List<int>();
        var result = GameMcpCraftingProjection.Press(
            CraftCommand(3), 15, 8, false, NoShortfall,
            remaining =>
            {
                asks.Add(remaining);
                return Landed(CraftingPlayerPostcondition.QueueAdmitted, 1);
            });

        Assert.Equal(new[] { 3, 2, 1 }, asks);
        Assert.Equal("3", (string?)GameMcpTestHarness.Json(result.Details!, Identities())["queued"]);
    }

    [Fact]
    public void Max_submits_once_and_answers_the_crafts_on_the_screen()
    {
        var submissions = 0;
        var result = GameMcpCraftingProjection.Press(
            CraftCommand(1), 15, 8, true, NoShortfall,
            _ =>
            {
                submissions++;
                return Landed(
                    CraftingPlayerPostcondition.InstanceQuantityIncreased, new BigDouble(3.96, 9));
            });

        Assert.Equal(1, submissions);
        Assert.Equal("3.96e9", (string?)GameMcpTestHarness.Json(result.Details!, Identities())["queued"]);
    }

    [Fact]
    public void A_shortfall_keeps_what_landed_and_says_why_the_rest_stopped()
    {
        var submissions = 0;
        var result = GameMcpCraftingProjection.Press(
            CraftCommand(45), 15, 8, false, NoShortfall,
            _ => ++submissions == 1
                ? Landed(CraftingPlayerPostcondition.InstanceQuantityIncreased, 3)
                : Refused(
                    CraftingPlayerActionResultCodes.Unaffordable,
                    "The cost of another of this craft is not met."));
        var postState = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            Context(), CraftCommand(45), result), Identities());

        Assert.Equal(2, submissions);
        Assert.Equal("committed", result.Status);
        Assert.Equal(
            "3 of 45 asked; the rest stopped: The cost of another of this craft is not met.",
            (string?)postState["queued"]);
    }

    [Fact]
    public void An_unaffordable_craft_names_the_unmet_price()
    {
        var world = World(heldDust: 40);
        var result = GameMcpCraftingProjection.Press(
            CraftCommand(5), 15, 8, true,
            () => GameMcpWorldQuery.CraftShortfallReason(world, RecipeId),
            _ => Refused(
                CraftingPlayerActionResultCodes.Unaffordable,
                "The cost of another of this craft is not met."));

        Assert.Equal("refused", result.Status);
        Assert.Equal("unaffordable", result.Code);
        Assert.Equal("Needs 75 Arcane Dust (have 40).", result.Reason);
    }

    [Fact]
    public void An_unaffordable_craft_the_world_still_prices_as_met_keeps_the_games_sentence()
    {
        var world = World();
        var result = GameMcpCraftingProjection.Press(
            CraftCommand(5), 15, 8, false,
            () => GameMcpWorldQuery.CraftShortfallReason(world, RecipeId),
            _ => Refused(
                CraftingPlayerActionResultCodes.Unaffordable,
                "The cost of another of this craft is not met."));

        Assert.Equal("unaffordable", result.Code);
        Assert.Equal("The cost of another of this craft is not met.", result.Reason);
    }

    [Fact]
    public void A_refused_first_press_answers_as_that_refusal()
    {
        var result = GameMcpCraftingProjection.Press(
            CraftCommand(5), 15, 8, false, NoShortfall,
            _ => Refused(
                CraftingPlayerActionResultCodes.QueueFull,
                "The Manual crafting queue has no empty slot."));

        Assert.Equal("refused", result.Status);
        Assert.Equal("queue_full", result.Code);
        Assert.Equal("The Manual crafting queue has no empty slot.", result.Reason);
    }

    [Fact]
    public void A_fault_after_landed_crafts_answers_as_the_fault_and_names_what_landed()
    {
        var submissions = 0;
        var result = GameMcpCraftingProjection.Press(
            CraftCommand(5), 15, 8, false, NoShortfall,
            _ => ++submissions <= 2
                ? Landed(CraftingPlayerPostcondition.QueueAdmitted, 1)
                : new CraftingPlayerSubmission(
                    NativeActionOutcome.Fault(
                        CraftingPlayerActionResultCodes.VerificationFailed,
                        NativeMutationOutcome.PostconditionFailed,
                        new NativeMutationCallOutcome(5, 1, 0),
                        "The exact crafting instance was not admitted to the authored queue."),
                    RecipeId));

        Assert.Equal("faulted", result.Status);
        Assert.StartsWith("2 of 5 asked went through before this craft. ", result.Reason);
        Assert.Equal(
            "requested craft completion",
            (string?)GameMcpTestHarness.Json(result.Details!, Identities())["missingOutcome"]);
    }

    [Fact]
    public void Instant_crafts_report_how_many_completed()
    {
        var result = GameMcpCraftingProjection.Press(
            CraftCommand(2), 15, 8, false, NoShortfall,
            remaining => Landed(CraftingPlayerPostcondition.InstantCompleted, remaining));
        var postState = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            Context(queuedAmount: 0), CraftCommand(2), result), Identities());

        Assert.Equal("2", (string?)postState["completed"]);
        Assert.Null(postState["queued"]);
    }

    // The screen's badge, not the repetition count: repetitions 4 to 3 is a badge of 8 to 4.
    [Theory]
    [InlineData("automate", 3, 5, "4", "16")]
    [InlineData("cancel_automation", 4, 3, "8", "4")]
    public void Automated_modes_return_the_settled_badge_amount(
        string mode,
        int before,
        int after,
        string badgeBefore,
        string badgeAfter)
    {
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Crafting, 15, 8, mode, RecipeId, Guid.Empty,
            "CraftingRecipeSO", 1, string.Empty, string.Empty, false, frameContext: Context(automationRepetitions: before));
        var committed = GameMcpCommandResult.Committed("committed", 15, 8);

        var postState = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            Context(automationRepetitions: after), command, committed), Identities());

        Assert.Equal("Craft Sigils", (string?)postState["name"]);
        Assert.Equal(badgeBefore, (string?)postState["amount"]!["before"]);
        Assert.Equal(badgeAfter, (string?)postState["amount"]!["after"]);
        Assert.Null(postState["automation"]);
        Assert.Null(postState["receipt"]);
    }

    [Fact]
    public void SuccessDetailsYieldToPostStateWhileFaultNamesTheMissingOutcome()
    {
        var fault = new CraftingPlayerSubmission(
            NativeActionOutcome.Fault(
                CraftingPlayerActionResultCodes.VerificationFailed,
                NativeMutationOutcome.PostconditionFailed,
                new NativeMutationCallOutcome(2, 1, 0),
                "queue did not change"),
            RecipeId);
        var committed = new CraftingPlayerSubmission(
            NativeActionOutcome.Commit(new NativeMutationCallOutcome(2, 1, 1), "queue changed"),
            RecipeId,
            CraftingPlayerPostcondition.QueueAdmitted);

        var failure = GameMcpTestHarness.Json(GameMcpCraftingProjection.Project(in fault), Identities());
        var success = GameMcpTestHarness.Json(GameMcpCraftingProjection.Project(in committed), Identities());

        Assert.Equal("requested craft completion", (string?)failure["missingOutcome"]);
        Assert.Single(failure.Properties());
        Assert.Empty(success.Properties());
    }

    [Fact]
    public void A_faulted_instance_transition_names_the_state_it_already_moved()
    {
        var silent = new CraftingInstanceLifecycleSubmission(NativeActionOutcome.Fault(
            CraftingInstanceLifecycleActionResultCodes.VerificationFailed,
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(1, 1, 0),
            "nothing moved"));
        var damaging = new CraftingInstanceLifecycleSubmission(
            NativeActionOutcome.Fault(
                CraftingInstanceLifecycleActionResultCodes.VerificationFailed,
                NativeMutationOutcome.PostconditionFailed,
                new NativeMutationCallOutcome(1, 1, 0),
                "the automation entry grew"),
            CraftingInstanceLifecycleSideEffect.Automation(1, 2));

        var quiet = GameMcpTestHarness.Json(GameMcpCraftingProjection.Project(in silent), Identities());
        var reported = GameMcpTestHarness.Json(GameMcpCraftingProjection.Project(in damaging), Identities());

        Assert.Null(quiet["observed"]);
        Assert.Equal(
            "requested crafting-instance transition", (string?)reported["missingOutcome"]);
        Assert.Equal(1, (int)reported["observed"]!["repetitions"]!["before"]!);
        Assert.Equal(2, (int)reported["observed"]!["repetitions"]!["after"]!);
    }

    [Fact]
    public void CraftingRecipeProjectionKeepsAuthoredEdgesAndNativeBlockerVerdicts()
    {
        var recipeId = Guid.Parse("b1b7d331-587a-4b4c-87cf-4a8f57c8256b");
        var typeId = Guid.Parse("5d343cb8-d676-4561-9c46-4bc74de5fcfd");
        var resourceId = Guid.Parse("4d4a9dd0-6b71-4ac2-89a1-7a4dde91ed54");
        var consumableId = Guid.Parse("a969e17f-1e72-4e69-9149-603b5bac33e0");
        var reading = new RawCraftingRecipeSample(
            recipeId,
            visible: false,
            canBuyAtStartingQuantity: false,
            startingQuantity: new BigDouble(2d),
            useQuantityAsLevel: true,
            timeToComplete: 9.5d,
            outputWithinCapacity: false,
            completionEffectCount: 1);
        var recipe = new WorldCraftingRecipe(
            in reading,
            PublicationTable<WorldCraftingRecipeTypeLink>.Create(new[]
            {
                new WorldCraftingRecipeTypeLink(recipeId, typeId),
            }),
            PublicationTable<WorldCraftingRecipeResource>.Create(new[]
            {
                new WorldCraftingRecipeResource(
                    recipeId,
                    WorldCraftingRecipeResourceKind.AuthoredInput,
                    resourceId,
                    new BigDouble(3d),
                    resourceStateAvailable: true,
                    visible: true,
                    bandwidthResource: true,
                    trueQuantity: new BigDouble(80d),
                    isCapped: true,
                    capacity: new BigDouble(100d),
                    usage: new BigDouble(4d),
                    drain: new BigDouble(1.5d)),
            }),
            PublicationTable<WorldCraftingRecipeConsumableOutput>.Create(new[]
            {
                new WorldCraftingRecipeConsumableOutput(recipeId, 0, 1, consumableId),
            }),
            PublicationTable<WorldCraftingRecipeDrainBlock>.Create(new[]
            {
                new WorldCraftingRecipeDrainBlock(recipeId, 0, new BigDouble(0.75d)),
            }));
        var world = new GameWorldState
        {
            CraftingRecipes = PublicationTable<WorldCraftingRecipe>.Create(new[] { recipe }),
            Resources = PublicationTable<WorldResource>.Create(new[]
            {
                GameMcpTestHarness.BandwidthResource(
                    resourceId, new BigDouble(80), new BigDouble(100)),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    Clean("crafting-recipes"),
                    Clean("crafting-recipe-state"),
                    Clean("crafting-decisions"),
                    Clean("resources"),
                }),
            CollectedAtEpoch = 25,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var state = GameMcpTestHarness.Context(world);

        var result = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            state,
            "crafting-recipes",
            recipeId.ToString("D")));

        Assert.Equal("available", (string?)result["status"]);
        Assert.Null(result["worldGeneration"]);
        var row = (JObject)result["row"]!;
        Assert.Equal(GameMcpTestHarness.Handle(recipeId), (string?)result["uuid"]);
        Assert.False((bool)row["visible"]!);
        Assert.False((bool)row["canStart"]!);
        Assert.Equal(
            new[]
            {
                "hidden_or_undiscovered",
                "native_purchase_refused",
                "output_capacity_blocked",
            },
            row["blockers"]!.Values<string>());
        Assert.Equal(GameMcpTestHarness.Handle(typeId),
            (string?)Assert.Single(row["types"]!)!["uuid"]);
        var input = Assert.Single(row["inputs"]!.Values<JObject>())!;
        Assert.Equal(GameMcpTestHarness.Handle(resourceId), (string?)input["resource"]!["uuid"]);
        Assert.True((bool)input["bandwidth"]!);
        Assert.Equal("3", (string?)input["cost"]);
        Assert.Equal("20", (string?)input["spendableAmount"]);
        Assert.Equal("100", (string?)input["capacity"]);
        Assert.True((bool)input["affordable"]!);
        var output = Assert.Single(row["consumableOutputs"]!.Values<JObject>())!;
        Assert.Equal(GameMcpTestHarness.Handle(consumableId), (string?)output["uuid"]);
        var drain = Assert.Single(row["drainBlockers"]!.Values<JObject>())!;
        Assert.Null(drain["reasonCode"]);
        Assert.Equal("0.750", (string?)drain["availableRatio"]);

        var incompleteWorld = new GameWorldState
        {
            CraftingRecipes = world.CraftingRecipes,
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[] { Clean("crafting-recipes") }),
            CollectedAtEpoch = 26,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var unavailable = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(incompleteWorld),
            "crafting-recipes",
            recipeId.ToString("D")));
        Assert.Equal("unavailable", (string?)unavailable["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)unavailable["reasonCode"]);
        Assert.Contains(
            "crafting-recipe-state",
            (string?)unavailable["reason"],
            StringComparison.Ordinal);
    }

    private static GameMcpCommand CraftCommand(int amount) => new(
        1, GameMcpCommandKind.Crafting, 15, 8, "craft", RecipeId, Guid.Empty,
        "CraftingRecipeSO", amount, string.Empty, string.Empty, false,
        frameContext: Context());

    private static CraftingPlayerSubmission Landed(
        CraftingPlayerPostcondition postcondition,
        BigDouble crafted) =>
        new(
            NativeActionOutcome.Commit(new NativeMutationCallOutcome(5, 1, 1), "verified"),
            RecipeId,
            postcondition,
            crafted);

    private static CraftingPlayerSubmission Refused(
        ServiceActionResultCode code,
        string reason)
    {
        var action = new CraftingPlayerAction(RecipeId, 15, 1, false);
        return CraftingPlayerSubmission.Reject(in action, code, reason);
    }

    private static GameMcpFrameContext Context(
        double queuedAmount = 4,
        int automationRepetitions = 3,
        bool canAutomate = true,
        string automationReasonCode = "automation_full",
        bool withQueueEntries = true) =>
        GameMcpTestHarness.Context(
            World(
                queuedAmount, automationRepetitions, canAutomate, automationReasonCode,
                withQueueEntries),
            2201,
            configurationGeneration: 8,
            lifecycleGeneration: 15);

    private static GameWorldState World(
        double queuedAmount = 4,
        int automationRepetitions = 3,
        bool canAutomate = true,
        string automationReasonCode = "automation_full",
        bool withQueueEntries = true,
        double heldDust = 900)
    {
        var reading = new RawCraftingRecipeSample(
            RecipeId,
            visible: true,
            canBuyAtStartingQuantity: true,
            startingQuantity: BigDouble.One,
            useQuantityAsLevel: false,
            timeToComplete: 5,
            outputWithinCapacity: true,
            completionEffectCount: 0);
        var recipe = new WorldCraftingRecipe(
            in reading,
            PublicationTable<WorldCraftingRecipeTypeLink>.Empty,
            PublicationTable<WorldCraftingRecipeResource>.Empty,
            PublicationTable<WorldCraftingRecipeConsumableOutput>.Empty,
            PublicationTable<WorldCraftingRecipeDrainBlock>.Empty);
        return new GameWorldState
        {
            CollectedAtEpoch = 15,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = Identities(),
            CraftingRecipes = PublicationTable<WorldCraftingRecipe>.Create(new[] { recipe }),
            CraftingDecisions = PublicationTable<WorldCraftingDecision>.Create(new[]
            {
                new WorldCraftingDecision(
                    RecipeId,
                    WorldCraftingPipeline.QueueStack,
                    new BigDouble(2),
                    new BigDouble(queuedAmount),
                    QueueId,
                    queueUsed: 1,
                    queueMaximum: 3,
                    canStart: true,
                    reasonCode: "ready",
                    automationRepetitions: automationRepetitions,
                    automationUsed: 1,
                    automationMaximum: 3,
                    canAutomate: canAutomate,
                    automationReasonCode: automationReasonCode,
                    automationQueueId: AutomationQueueId),
            }),
            CraftingQueueEntries = withQueueEntries
                ? PublicationTable<WorldCraftingQueueEntry>.Create(new[]
                {
                    new WorldCraftingQueueEntry(
                        QueueId, 0, RecipeId, new BigDouble(queuedAmount), false, 0),
                    // The badge the automation strip draws is the instance's own quantity,
                    // which the game stores as 2^(repetitions - 1).
                    new WorldCraftingQueueEntry(
                        AutomationQueueId, 1, RecipeId,
                        new BigDouble(AutomationBadgeAmount(automationRepetitions)), true,
                        automationRepetitions),
                })
                : PublicationTable<WorldCraftingQueueEntry>.Empty,
            CraftingDecisionCosts = PublicationTable<WorldCraftingDecisionCost>.Create(new[]
            {
                new WorldCraftingDecisionCost(
                    RecipeId,
                    new WorldResourceCost(ResourceId, new BigDouble(75), new BigDouble(heldDust))),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                Clean("crafting-recipes"),
                Clean("crafting-recipe-state"),
                Clean("crafting-decisions"),
                Clean("resources"),
            }),
        };
    }

    private static string NoShortfall() => string.Empty;

    private static int AutomationBadgeAmount(int repetitions) =>
        repetitions <= 0 ? 0 : 1 << (repetitions - 1);

    private static EntityIdentityCatalogSnapshot Identities() =>
        GameMcpTestHarness.CatalogWith(
            new EntityIdentityName(
                RecipeId, "CraftingRecipeSO", "Craft Sigils", "craftSigils"),
            new EntityIdentityName(
                QueueId, "CraftingInstanceListVariable", "Sigil Queue", "sigilQueue"),
            new EntityIdentityName(
                AutomationQueueId, "CraftingInstanceListVariable",
                "Auto Sigil Queue", "autoSigilQueue"),
            new EntityIdentityName(
                ResourceId, "ResourceSO", "Arcane Dust", "arcaneDust"));
}
