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
    private static readonly Guid First = Guid.Parse("f5000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("f5000000-0000-0000-0000-000000000002");
    private static readonly Guid Third = Guid.Parse("f5000000-0000-0000-0000-000000000003");

    [Fact]
    public void Tool_has_one_mode_conditioned_uuid_shape_and_no_generation_or_receipt_inputs()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_challenge");
        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(new[] { "mode" }, schema["required"]!.Values<string>());
        Assert.Equal(new[] { "select", "activate", "abandon", "reroll_time_challenges", "reroll_prestige_challenges" },
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
                ["name"] = "game_challenge",
                ["arguments"] = new JObject { ["mode"] = "select" },
            }));
        var forbidden = router.Handle(GameMcpAcceptanceFixture.Request(2, "tools/call",
            new JObject
            {
                ["name"] = "game_challenge",
                ["arguments"] = new JObject
                {
                    ["mode"] = "reroll_time_challenges",
                    ["uuid"] = First.ToString("D"),
                },
            }));

        Assert.Contains(missing.Body!["error"]!["data"]!["validationErrors"]!.Values<JObject>(),
            error => (string?)error?["code"] == "missing_required" && (string?)error?["field"] == "uuid");
        Assert.Contains(forbidden.Body!["error"]!["data"]!["validationErrors"]!.Values<JObject>(),
            error => (string?)error?["code"] == "unexpected_for_mode" && (string?)error?["field"] == "uuid");
    }

    [Fact]
    public void Challenge_world_list_is_lean_and_world_get_is_decision_complete()
    {
        var world = World();
        var response = Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(world, generation: 2501), "challenges", 0, 50).Freeze(), world);

        var state = response["challengeState"]!;
        Assert.Equal(2, (int)state["rerollsLeft"]!);
        Assert.Equal(3, (int)state["selectionMaximum"]!);
        Assert.Equal("Prismatic Trial", (string?)state["selected"]![0]!["name"]);
        Assert.Equal("Expanding Trial", (string?)state["timeOffers"]![1]!["name"]);
        var first = Assert.Single(response["rows"]!.Values<JObject>(),
            row => (string?)row?["uuid"] == First.ToString("D"))!;
        Assert.Equal("Prismatic Trial", (string?)first["name"]);
        Assert.Equal("queued", (string?)first["state"]);
        Assert.Equal("1", (string?)first["level"]);
        Assert.Null(first["select"]);
        var exact = Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, generation: 2501),
            "challenges", First.ToString("D")).Freeze(), world)["row"]!;
        Assert.True((bool)exact["select"]!["available"]!);
        Assert.True((bool)exact["activate"]!["available"]!);
        Assert.Equal("12", (string?)exact["nextDifficulty"]);
        Assert.Equal("30", (string?)exact["nextReward"]);
        Assert.Null(first["receipt"]);
        Assert.Null(first["payment"]);
    }

    [Fact]
    public void Committed_poststate_eliminates_name_joins_and_readbacks_for_target_and_reroll_modes()
    {
        var world = World();
        var context = GameMcpTestHarness.Context(world, generation: 2502);
        var before = World(selected: false);
        var selectCommand = new GameMcpCommand(
            1, GameMcpCommandKind.Challenge, 9, 3, "select", First, Guid.Empty,
            "ChallengeSO", 1, string.Empty, string.Empty, false, false,
            frameContext: GameMcpTestHarness.Context(before));
        var rerollCommand = new GameMcpCommand(
            2, GameMcpCommandKind.Challenge, 9, 3, "reroll_time_challenges", Guid.Empty, Guid.Empty,
            "ChallengeSO", 1, string.Empty, string.Empty, false, false,
            frameContext: GameMcpTestHarness.Context(before));
        var target = Json(GameMcpWorldQuery.ProjectChallengePostState(context, selectCommand), world);
        var fetch = Json(GameMcpWorldQuery.ProjectChallengePostState(context, rerollCommand), world);

        Assert.Equal("Prismatic Trial", (string?)target["name"]);
        Assert.False((bool)target["selected"]!["before"]!);
        Assert.True((bool)target["selected"]!["after"]!);
        Assert.Null(target["challengeState"]);
        Assert.Null(target["receipt"]);
        Assert.NotNull(fetch["challengeState"]);
        Assert.Equal("Expanding Trial",
            (string?)fetch["challengeState"]!["timeOffers"]![1]!["name"]);
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
    /// The read block names the verb that acts on it and says which of the two presses this would
    /// be, so nothing has to be attempted to learn the price.
    /// </summary>
    [Fact]
    public void The_read_block_names_the_reroll_verb_and_whether_the_next_press_costs_one()
    {
        var free = Json(GameMcpWorldQuery.ProjectChallengeState(
            World(challengesFetched: false, rerollsLeft: 3)), World());
        var paid = Json(GameMcpWorldQuery.ProjectChallengeState(World()), World());

        Assert.False((bool)free["rerollTimeChallenges"]!["costsReroll"]!);
        Assert.True((bool)paid["rerollTimeChallenges"]!["costsReroll"]!);
        Assert.True((bool)paid["rerollPrestigeChallenges"]!["costsReroll"]!);
        Assert.Null(paid["fetchTimeChallenges"]);
    }

    private static JObject Reroll(GameWorldState before, GameWorldState after)
    {
        var command = new GameMcpCommand(
            3, GameMcpCommandKind.Challenge, 9, 3, "reroll_time_challenges", Guid.Empty, Guid.Empty,
            "ChallengeSO", 1, string.Empty, string.Empty, false, false,
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

    private static GameWorldState World(
        bool selected = true,
        int rerollsLeft = 2,
        bool challengesFetched = true)
    {
        var rows = new[]
        {
            new WorldChallenge(First, 1, 1, true, false, 5, 10, 12, 30,
                true, true, false, new BigDouble(12), new BigDouble(30)),
            new WorldChallenge(Second, 0, 0, true, false, 5, 10, 15, 40,
                true, false, false, new BigDouble(15), new BigDouble(40)),
            new WorldChallenge(Third, 2, 2, true, false, 5, 10, 20, 50,
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
                true, string.Empty, true, challengesFetched, rerollsLeft, 3, 3,
                selected
                    ? PublicationTable<WorldChallengeReference>.Create(new[]
                    {
                        new WorldChallengeReference(0, First),
                    })
                    : PublicationTable<WorldChallengeReference>.Empty,
                PublicationTable<WorldChallengeReference>.Create(new[]
                {
                    new WorldChallengeReference(0, First),
                    new WorldChallengeReference(1, Second),
                }),
                PublicationTable<WorldChallengeReference>.Create(new[]
                {
                    new WorldChallengeReference(0, Third),
                })),
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
