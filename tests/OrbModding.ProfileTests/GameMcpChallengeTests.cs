using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpChallengeTests
{
    private static readonly Guid First = Guid.Parse("f5100000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("f5200000-0000-0000-0000-000000000002");
    private static readonly Guid Third = Guid.Parse("f5300000-0000-0000-0000-000000000003");

    [Fact]
    public void Tool_has_one_mode_conditioned_uuid_shape_and_no_generation_or_receipt_inputs()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "time_challenge");
        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(new[] { "mode" }, schema["required"]!.Values<string>());
        Assert.Equal(new[] { "select", "queue", "abandon", "reroll", "state" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.NotNull(schema["properties"]!["uuid"]);
        Assert.Null(schema["properties"]!["worldGeneration"]);
    }

    [Fact]
    public void Validation_names_uuid_missing_or_forbidden_by_mode()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(1, "tools/call",
            new JObject
            {
                ["name"] = "time_challenge",
                ["arguments"] = new JObject { ["mode"] = "select" },
            }));
        var forbidden = router.Handle(GameMcpAcceptanceFixture.Request(2, "tools/call",
            new JObject
            {
                ["name"] = "time_challenge",
                ["arguments"] = new JObject
                {
                    ["mode"] = "reroll",
                    ["uuid"] = First.ToString("D"),
                },
            }));

        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: required " +
            "field 'uuid' is missing for mode 'select'", GameMcpTestHarness.Page(missing));
        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: field " +
            "'uuid' is not accepted for mode 'reroll'", GameMcpTestHarness.Page(forbidden));
    }

    /// <summary>
    /// The screen state used to ride every page of a ninety-eight-row category, so a request for one
    /// row came back nine tenths ambient state. It is one answer a caller asks for now.
    /// </summary>
    [Fact]
    public void Challenge_reads_carry_rows_only_and_the_screen_is_its_own_answer()
    {
        var world = World();
        var response = Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(world, generation: 2501), "challenges", 0, 50).Freeze(), world);

        Assert.Null(response["challengeState"]);
        var first = Assert.Single(response["rows"]!.Values<JObject>(),
            row => (string?)row?["uuid"] == GameMcpTestHarness.Handle(First))!;
        Assert.Equal("Prismatic Trial", (string?)first["name"]);

        // Two questions on one row: how far the player has come with this challenge, and what its
        // own run is doing. They used to share the name `state`, which is why the run word moved.
        Assert.Equal("available", (string?)first["state"]);
        Assert.Equal("queued", (string?)first["run"]);
        Assert.Equal("1", (string?)first["level"]);
        Assert.Null(first["select"]);
        var exact = Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, generation: 2501),
            "challenges", First.ToString("D")).Freeze(), world)["row"]!;
        Assert.Null(exact["challengeState"]);
        Assert.True((bool)exact["select"]!["available"]!);
        Assert.True((bool)exact["queue"]!["available"]!);
        Assert.Null(exact["activate"]);
        Assert.Equal("12", (string?)exact["nextDifficulty"]);
        Assert.Equal("30", (string?)exact["nextReward"]);
        Assert.Null(first["receipt"]);
        Assert.Null(first["payment"]);

        var state = Json(GameMcpWorldQuery.ProjectChallengeState(world), world);
        Assert.Equal(2, (int)state["rerollsLeft"]!);
        Assert.Equal(3, (int)state["selectionMaximum"]!);
        Assert.Equal("Prismatic Trial", (string?)state["selected"]![0]!["name"]);
        Assert.Equal("Expanding Trial", (string?)state["offers"]![1]!["name"]);
    }

    /// <summary>
    /// The Reset modal and the Time screen draw the same list from the same asset, so it is said
    /// once. A build where the two ever part company says the second one out loud.
    /// </summary>
    [Fact]
    public void The_reset_modals_offers_are_named_only_when_they_differ_from_the_screens()
    {
        var shared = World(prestigeOffers: new[] { First, Second });
        var parted = World();

        Assert.Null(Json(GameMcpWorldQuery.ProjectChallengeState(shared), shared)["resetOffers"]);
        Assert.Equal("Temporal Trial", (string?)Json(
            GameMcpWorldQuery.ProjectChallengeState(parted), parted)["resetOffers"]![0]!["name"]);
    }

    [Fact]
    public void Committed_poststate_eliminates_name_joins_and_readbacks_for_target_and_reroll_modes()
    {
        var world = World();
        var context = GameMcpTestHarness.Context(world, generation: 2502);
        var before = World(selected: false);
        var selectCommand = new GameMcpCommand(
            1, GameMcpCommandKind.Challenge, 9, 3, "select", First, Guid.Empty,
            "ChallengeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before));
        var rerollCommand = new GameMcpCommand(
            2, GameMcpCommandKind.Challenge, 9, 3, "reroll", Guid.Empty, Guid.Empty,
            "ChallengeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before));
        var target = Json(GameMcpWorldQuery.ProjectChallengePostState(context, selectCommand), world);
        var fetch = Json(GameMcpWorldQuery.ProjectChallengePostState(context, rerollCommand), world);

        Assert.Equal("Prismatic Trial", (string?)target["name"]);
        Assert.False((bool)target["selected"]!["before"]!);
        Assert.True((bool)target["selected"]!["after"]!);
        Assert.Null(target["challengeState"]);
        Assert.Null(target["receipt"]);
        Assert.Null(fetch["challengeState"]);
        Assert.Equal("Expanding Trial", (string?)fetch["offers"]![1]!["name"]);
        Assert.Equal(2, (int)fetch["rerollsLeft"]!["before"]!);
        Assert.Equal(2, (int)fetch["rerollsLeft"]!["after"]!);
        Assert.True((bool)fetch["challengesFetched"]!["before"]!);
        Assert.True((bool)fetch["challengesFetched"]!["after"]!);
    }

    /// <summary>
    /// The game relabels one button: the first press of a world cycle sets hasFetchedChallenges and
    /// is free, every later press decrements challengeRerollsLeft. Both presses publish both facts,
    /// so a caller never has to infer a spend from an absent key.
    /// </summary>
    [Fact]
    public void A_reroll_publishes_what_it_spent_and_a_free_first_press_publishes_that_it_spent_nothing()
    {
        var firstPress = Reroll(
            before: World(challengesFetched: false, rerollsLeft: 3),
            after: World(challengesFetched: true, rerollsLeft: 3));
        var laterPress = Reroll(
            before: World(challengesFetched: true, rerollsLeft: 3),
            after: World(challengesFetched: true, rerollsLeft: 2));

        Assert.False((bool)firstPress["challengesFetched"]!["before"]!);
        Assert.True((bool)firstPress["challengesFetched"]!["after"]!);
        Assert.Equal(3, (int)firstPress["rerollsLeft"]!["before"]!);
        Assert.Equal(3, (int)firstPress["rerollsLeft"]!["after"]!);

        Assert.True((bool)laterPress["challengesFetched"]!["before"]!);
        Assert.Equal(3, (int)laterPress["rerollsLeft"]!["before"]!);
        Assert.Equal(2, (int)laterPress["rerollsLeft"]!["after"]!);
    }

    /// <summary>
    /// One button, one decision: the Time screen and the Reset modal spend the same budget through
    /// the same asset, so the read says which of the two presses the next one would be exactly once.
    /// </summary>
    [Fact]
    public void The_read_block_names_one_reroll_decision_and_whether_the_next_press_costs_one()
    {
        var free = Json(GameMcpWorldQuery.ProjectChallengeState(
            World(challengesFetched: false, rerollsLeft: 3)), World());
        var paid = Json(GameMcpWorldQuery.ProjectChallengeState(World()), World());

        Assert.False((bool)free["reroll"]!["costsReroll"]!);
        Assert.True((bool)paid["reroll"]!["costsReroll"]!);
        Assert.Null(paid["rerollTimeChallenges"]);
        Assert.Null(paid["rerollPrestigeChallenges"]);
        Assert.Null(paid["fetchTimeChallenges"]);
    }

    /// <summary>
    /// The game promises the spend, not a different set of offers: a pool small enough to redraw
    /// itself is a legitimate outcome of a press that landed. The delta says which happened, so
    /// neither the caller nor the boundary treats an identical redraw as a failure.
    /// </summary>
    [Fact]
    public void An_identical_redraw_is_a_landed_press_that_says_the_offers_did_not_move()
    {
        var identical = Reroll(
            before: World(rerollsLeft: 3),
            after: World(rerollsLeft: 2));
        var redrawn = Reroll(
            before: World(rerollsLeft: 3),
            after: World(rerollsLeft: 2, timeOffers: new[] { Third, First }));

        Assert.False((bool)identical["changed"]!);
        Assert.Equal(3, (int)identical["rerollsLeft"]!["before"]!);
        Assert.Equal(2, (int)identical["rerollsLeft"]!["after"]!);
        Assert.True((bool)redrawn["changed"]!);
    }

    /// <summary>
    /// A press that was attempted spent before it asked for offers, so its failure carries the same
    /// settled pair a commit does rather than leaving the budget to be inferred from silence.
    /// </summary>
    [Fact]
    public void A_failed_press_refuses_with_the_budget_on_both_sides()
    {
        var faulted = new ChallengeSubmission(ChallengePreflight.PostCommitFault,
            ChallengeNativeStage.DecisionCommit, NativeMutationOutcome.ExecutionThrew,
            new NativeMutationCallOutcome(1, 1, 0), "the native pipeline threw", 3, 3);

        var refused = Json(GameMcpChallengeProjection.Project(in faulted), World());

        Assert.Equal(3, (int)refused["rerollsLeft"]!["before"]!);
        Assert.Equal(3, (int)refused["rerollsLeft"]!["after"]!);
    }

    private static JObject Reroll(GameWorldState before, GameWorldState after)
    {
        var command = new GameMcpCommand(
            3, GameMcpCommandKind.Challenge, 9, 3, "reroll", Guid.Empty, Guid.Empty,
            "ChallengeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before));
        return Json(
            GameMcpWorldQuery.ProjectChallengePostState(
                GameMcpTestHarness.Context(after, generation: 2503), command),
            after);
    }

    [Fact]
    public void Failure_names_the_missing_outcome_while_success_yields_to_newer_poststate()
    {
        var failure = new ChallengeSubmission(ChallengePreflight.VerificationFailed,
            ChallengeNativeStage.Verification, NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(1, 1, 0), "state unchanged");
        var success = new ChallengeSubmission(ChallengePreflight.Proceeded,
            ChallengeNativeStage.Verification, NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1), "state changed");

        var failed = Json(GameMcpChallengeProjection.Project(in failure), World());
        var committed = Json(GameMcpChallengeProjection.Project(in success), World());

        Assert.Equal("requested challenge transition", (string?)failed["missingOutcome"]);
        Assert.Single(failed.Properties());
        Assert.Empty(committed.Properties());
    }

    /// <remarks>
    /// The reroll budget is the one axis a spent-budget refusal turns on, so it ships as the number
    /// the refusal read rather than as a sentence a planner has to parse.
    /// </remarks>
    [Fact]
    public void A_spent_reroll_budget_is_refused_with_the_budget_it_read()
    {
        var refusal = ChallengeSubmission.RerollsExhausted("No challenge rerolls remain.", 0);

        var refused = Json(GameMcpChallengeProjection.Project(in refusal), World());

        Assert.Equal(0, (int?)refused["rerollsLeft"]);
        Assert.Single(refused.Properties());
    }

    /// <summary>
    /// One button, two presses' worth of weight. The cycle's first press is the offer fetch: it
    /// costs no reroll, arms every challenge it drew for the reset, and opens the reset that was
    /// refusing. A round pressed it, read <c>changed: yes</c> beside a fresh five, and had to go to
    /// a second verb to find out that five challenges were queued and the reset had unlocked.
    /// </summary>
    [Fact]
    public void The_offer_fetch_names_the_press_what_it_armed_and_the_reset_it_opened()
    {
        var fetch = Reroll(
            before: World(challengesFetched: false, rerollsLeft: 3),
            after: World(challengesFetched: true, rerollsLeft: 3, thirdState: 1));
        var later = Reroll(
            before: World(challengesFetched: true, rerollsLeft: 3, thirdState: 1),
            after: World(challengesFetched: true, rerollsLeft: 2, thirdState: 1));

        Assert.Equal("offer_fetch", (string?)fetch["press"]);
        Assert.Equal("Temporal Trial", (string?)fetch["queuedForReset"]![0]!["name"]);
        Assert.False((bool)fetch["reset"]!["before"]!);
        Assert.True((bool)fetch["reset"]!["after"]!);

        Assert.Equal("reroll", (string?)later["press"]);
        Assert.True((bool)later["reset"]!["before"]!);
        Assert.True((bool)later["reset"]!["after"]!);
    }

    /// <summary>
    /// An offer is an offer of this cycle's. The game never clears the list, so after a reset the
    /// same five names sat under <c>offers:</c> beside <c>challengesFetched: no</c> while all five
    /// were running, and before the first fetch they were the previous cycle's draw. Which
    /// challenges are running is the <c>run</c> column's answer, on the rows that own it.
    /// </summary>
    [Fact]
    public void A_cycle_whose_offers_are_not_fetched_publishes_no_offer_list()
    {
        var unfetched = World(challengesFetched: false);
        var fetched = World();

        var closed = Json(GameMcpWorldQuery.ProjectChallengeState(unfetched), unfetched);
        var open = Json(GameMcpWorldQuery.ProjectChallengeState(fetched), fetched);

        Assert.False((bool)closed["challengesFetched"]!);
        Assert.Null(closed["offers"]);
        Assert.Null(closed["resetOffers"]);
        Assert.Equal("Prismatic Trial", (string?)closed["selected"]![0]!["name"]);

        Assert.Equal("Prismatic Trial", (string?)open["offers"]![0]!["name"]);
    }

    /// <summary>
    /// The gate says whether the press is open and why not; the row beside it already says where
    /// the challenge stands. <c>selected=</c> and <c>queued=</c> agreed with the row's own
    /// <c>selected</c> and <c>run</c> columns on all hundred blocks of one round.
    /// </summary>
    [Fact]
    public void A_gate_states_its_verdict_without_restating_the_row_beside_it()
    {
        var world = World();
        var row = (JObject)Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, generation: 2504),
            "challenges", First.ToString("D")).Freeze(), world)["row"]!;

        Assert.True((bool)row["selected"]!);
        Assert.Equal("queued", (string?)row["run"]);
        Assert.Equal(
            new[] { "available" },
            ((JObject)row["select"]!).Properties().Select(property => property.Name).ToArray());
        Assert.Equal(
            new[] { "available" },
            ((JObject)row["queue"]!).Properties().Select(property => property.Name).ToArray());
    }

    /// <summary>
    /// The selection budget is the game's own gate and the published world carries no reading of
    /// it: a round read <c>select: no (ERR_LIMIT)</c> off a page and then watched the verb perform
    /// the swap. The page refuses on what the world states outright and leaves the rest to the verb.
    /// </summary>
    [Fact]
    public void The_page_never_predicts_a_selection_refusal_the_verb_performs()
    {
        var full = World(selectionMaximum: 1);
        var row = (JObject)Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(full, generation: 2505),
            "challenges", Second.ToString("D")).Freeze(), full)["row"]!;

        Assert.False((bool)row["selected"]!);
        Assert.Equal(1, (int)Json(
            GameMcpWorldQuery.ProjectChallengeState(full), full)["selectionMaximum"]!);
        Assert.True((bool)row["select"]!["available"]!);
    }

    /// <summary>
    /// A challenge whose run is over is refused by the standing rule, under the class that says the
    /// state was the blocker rather than the caller's argument.
    /// </summary>
    [Fact]
    public void A_challenge_that_has_already_run_is_refused_as_a_state()
    {
        var world = World(firstState: 3);
        var row = (JObject)Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, generation: 2506),
            "challenges", First.ToString("D")).Freeze(), world)["row"]!;

        Assert.False((bool)row["queue"]!["available"]!);
        Assert.Equal("ERR_STATE", (string?)row["queue"]!["reasonCode"]);
        Assert.Equal(
            "A challenge that has already run cannot be queued again until the next reset.",
            (string?)row["queue"]!["reason"]);
    }

    /// <summary>
    /// Selecting past a full list is two presses on the screen and the verb makes the first one
    /// itself. Answering only <c>selected: no -&gt; yes</c> confessed half of that, and a live round
    /// spent a follow-up <c>state</c> call learning which challenge had been given up. The swap
    /// names the displaced one in the shape a displaced spell slot already answers in.
    /// </summary>
    [Fact]
    public void A_select_that_swaps_names_the_challenge_it_displaced()
    {
        var before = World(selectionMaximum: 1);
        var after = World(selectionMaximum: 1, selectedIds: new[] { Second });
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Challenge, 9, 3, "select", Second,
            GameMcpWorldQuery.ChallengeSelectionToReplace(before, Second),
            "ChallengeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before));

        var swap = Json(
            GameMcpWorldQuery.ProjectChallengePostState(
                GameMcpTestHarness.Context(after, generation: 2507), command),
            after);

        Assert.Equal("Expanding Trial", (string?)swap["name"]);
        Assert.False((bool)swap["selected"]!["before"]!);
        Assert.True((bool)swap["selected"]!["after"]!);
        Assert.Equal("Prismatic Trial", (string?)swap["displaced"]!["name"]);
        Assert.True((bool)swap["displaced"]!["selected"]!["before"]!);
        Assert.False((bool)swap["displaced"]!["selected"]!["after"]!);
    }

    /// <summary>
    /// A selection the screen had room for displaced nothing, so nothing is named: the key is the
    /// press the caller did not ask for, not a slot the answer always fills.
    /// </summary>
    [Fact]
    public void A_select_with_room_to_spare_names_no_displaced_challenge()
    {
        var before = World(selected: false);
        var after = World();
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Challenge, 9, 3, "select", First,
            GameMcpWorldQuery.ChallengeSelectionToReplace(before, First),
            "ChallengeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before));

        var straight = Json(
            GameMcpWorldQuery.ProjectChallengePostState(
                GameMcpTestHarness.Context(after, generation: 2508), command),
            after);

        Assert.True((bool)straight["selected"]!["after"]!);
        Assert.Null(straight["displaced"]);
    }

    private static GameWorldState World(
        bool selected = true,
        int rerollsLeft = 2,
        bool challengesFetched = true,
        Guid[]? timeOffers = null,
        Guid[]? prestigeOffers = null,
        int selectionMaximum = 3,
        int firstState = 1,
        int thirdState = 2,
        Guid[]? selectedIds = null)
    {
        selectedIds ??= new[] { First };
        timeOffers ??= new[] { First, Second };
        prestigeOffers ??= new[] { Third };
        var rows = new[]
        {
            new WorldChallenge(First, 1, firstState, true, false, 5, 10, 12, 30,
                true, true, false, new BigDouble(12), new BigDouble(30)),
            new WorldChallenge(Second, 0, 0, true, false, 5, 10, 15, 40,
                true, false, false, new BigDouble(15), new BigDouble(40)),
            new WorldChallenge(Third, 2, thirdState, true, false, 5, 10, 20, 50,
                true, true, false, new BigDouble(20), new BigDouble(50)),
        };
        var identities = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(First, "ChallengeSO", "Prismatic Trial", "prismaticTrial"),
            new EntityIdentityName(Second, "ChallengeSO", "Expanding Trial", "expandingTrial"),
            new EntityIdentityName(Third, "ChallengeSO", "Temporal Trial", "temporalTrial"),
        }).OrderBy(row => row.EntityId).ToArray();
        return new GameWorldState
        {
            CollectedAtEpoch = 21,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(21, identities),
            Challenges = PublicationTable<WorldChallenge>.Create(rows),
            ChallengeContext = new WorldChallengeContext(
                true, string.Empty, true, challengesFetched, rerollsLeft, 3, selectionMaximum,
                selected
                    ? PublicationTable<WorldChallengeReference>.Create(
                        selectedIds
                            .Select((id, index) => new WorldChallengeReference(index, id))
                            .ToArray())
                    : PublicationTable<WorldChallengeReference>.Empty,
                PublicationTable<WorldChallengeReference>.Create(
                    timeOffers.Select((id, index) => new WorldChallengeReference(index, id)).ToArray()),
                PublicationTable<WorldChallengeReference>.Create(
                    prestigeOffers.Select((id, index) => new WorldChallengeReference(index, id)).ToArray())),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus("challenges", WorldCategoryOutcome.Collected, 3, 0, string.Empty),
                new WorldCollectionCategoryStatus("challenge decisions", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
        };
    }

    private static JObject Json(GameMcpValue value, GameWorldState world) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, world.EntityIdentities));
}
