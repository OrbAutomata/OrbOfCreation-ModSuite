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

public sealed class GameMcpPrestigeTests
{
    private static readonly Guid ResourceId = Guid.Parse("f6000000-0000-0000-0000-000000000001");
    private static readonly Guid Queued = Guid.Parse("f6000000-0000-0000-0000-000000000002");
    private static readonly Guid Reward = Guid.Parse("f6000000-0000-0000-0000-000000000003");

    [Fact]
    public void Tool_requires_one_explicit_irreversible_confirmation_and_no_target_or_generation()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "time_prestige");
        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(new[] { "confirm" }, schema["required"]!.Values<string>());
        Assert.Equal("boolean", (string?)schema["properties"]!["confirm"]!["type"]);
        Assert.Null(schema["properties"]!["uuid"]);
        Assert.Null(schema["properties"]!["expectedNativeType"]);
        Assert.Null(schema["properties"]!["worldGeneration"]);
    }

    [Fact]
    public void False_confirmation_is_named_before_any_frame_operation_is_enqueued()
    {
        var inbox = new GameMcpFrameInbox();
        var router = new GameMcpProtocolRouter(inbox);
        var response = router.Handle(GameMcpAcceptanceFixture.Request(1, "tools/call",
            new JObject
            {
                ["name"] = "time_prestige",
                ["arguments"] = new JObject { ["confirm"] = false },
            }));

        Assert.Equal(-32602, (int?)response.Body!["error"]!["code"]);
        Assert.Contains("confirm must be true", (string?)response.Body["error"]!["message"]);
        Assert.Empty(inbox.ClaimPending());
    }

    [Fact]
    public void The_challenge_screen_carries_complete_named_prestige_decision_and_current_holding()
    {
        var world = World();
        var prestige = Json(GameMcpWorldQuery.ProjectPrestigeState(world), world);
        var advancements = prestige["timeAdvancements"]!;
        Assert.Equal(7, (int)advancements["atStart"]!);
        Assert.Equal(5, (int)advancements["previousStart"]!);
        Assert.Equal(2, (int)advancements["change"]!);
        Assert.Equal(4, (int)prestige["resetCount"]!);
        Assert.Equal("Persistent Light", (string?)prestige["persistentResource"]!["resource"]!["name"]);
        Assert.Equal("80", (string?)prestige["persistentResource"]!["amount"]);
        Assert.Equal("Prismatic Trial", (string?)prestige["queuedForReset"]![0]!["name"]);
        Assert.Equal("Reward Trial", (string?)prestige["survivingRewards"]![0]!["name"]);
        Assert.True((bool)prestige["reset"]!["available"]!);
    }

    /// <summary>
    /// The fixture holds three figures on purpose: the game's projected 11, the 7 a reset would
    /// start with, and the 5 the last one started with. Only the screen's own subtraction (7 - 5)
    /// may reach a caller, because the other one (11 - 5) is the number that told a live round each
    /// reset was worse than the last.
    /// </summary>
    [Fact]
    public void Reset_gain_is_the_screen_subtraction_and_reads_as_one_line()
    {
        var world = World();
        var prestige = Json(GameMcpWorldQuery.ProjectPrestigeState(world), world);

        Assert.Null(prestige["currentTimeAdvancements"]);
        Assert.Null(prestige["startingTimeAdvancements"]);
        Assert.Null(prestige["previousStartingTimeAdvancements"]);
        Assert.Null(prestige["changeFromPrevious"]);
        Assert.Contains(
            "timeAdvancements: atStart=7, previousStart=5, change=2",
            GameMcpTextPage.Render(prestige).Split('\n'));
    }

    /// <summary>
    /// A reset tears the world down behind a native scene fade and rebuilds it. The frame-scale
    /// budget every other verb settles on answered the heaviest verb in the game with a timeout on
    /// a reset that had plainly worked.
    /// </summary>
    [Fact]
    public void The_reset_settles_on_a_lifecycle_budget_rather_than_the_frame_scale_one()
    {
        var reset = new GameMcpCommand(
            1, GameMcpCommandKind.Prestige, 9, 3, "confirm", Guid.Empty, Guid.Empty,
            "PersistentResetManager", 1, string.Empty, string.Empty, false);

        Assert.Equal(15f, GameMcpPostStateSettlement.WaitSeconds(reset));
        Assert.Equal(
            GameMcpPostStateSettlement.MaximumWaitSeconds,
            GameMcpPostStateSettlement.WaitSeconds(GameMcpAcceptanceFixture.NativeCommand()));
    }

    /// <summary>
    /// The reset's own identity is the lifecycle it replaced, so an unsettled world still says
    /// whether the reset happened. It used to answer with a bare sentence and nothing to correlate
    /// against, which cost a live round four calls rebuilding the picture by hand.
    /// </summary>
    [Fact]
    public void An_unsettled_reset_still_says_which_lifecycle_it_replaced_and_where_to_read_it()
    {
        var replaced = Json(GameMcpWorldQuery.PrestigeSettlementPending(9, 10, "Main"), World());
        var nothing = Json(GameMcpWorldQuery.PrestigeSettlementPending(9, 9, null), World());

        Assert.Equal(9, (int)replaced["lifecycleGeneration"]!["before"]!);
        Assert.Equal(10, (int)replaced["lifecycleGeneration"]!["after"]!);
        Assert.Equal("Main", (string?)replaced["scene"]);
        Assert.Contains("world_overview",
            (string?)replaced["postStateUnavailable"]!["reason"]!, StringComparison.Ordinal);
        Assert.Contains("neither a new lifecycle",
            (string?)nothing["postStateUnavailable"]!["reason"]!, StringComparison.Ordinal);
        Assert.Null(nothing["scene"]);
    }

    [Fact]
    public void Committed_poststate_returns_the_fresh_scene_prestige_and_challenge_decisions()
    {
        var world = World(worldComplete: false, fetched: false);
        var response = Json(GameMcpWorldQuery.ProjectPrestigePostState(Context(world, 2602)), world);

        Assert.Equal("Main", (string?)response["scene"]);
        Assert.NotNull(response["lifecycleGeneration"]);
        Assert.Equal("ERR_STATE", (string?)response["prestigeState"]!["reset"]!["reasonCode"]);
        Assert.NotNull(response["challengeState"]);
        Assert.Null(response["receipt"]);
        Assert.Null(response["payment"]);
    }

    [Fact]
    public void Missing_prestige_evidence_is_local_and_does_not_poison_challenge_decisions()
    {
        var source = World();
        var context = source.ChallengeContext;
        var world = source with
        {
            ChallengeContext = new WorldChallengeContext(true, string.Empty,
                context.WorldCycleComplete, context.ChallengesFetched,
                context.RerollsLeft, context.RerollsMaximum, context.SelectionMaximum,
                context.Selected, context.TimeOffers, context.PrestigeOffers),
        };
        var response = Json(GameMcpWorldQuery.ProjectChallengeState(world), world);
        var prestige = Json(GameMcpWorldQuery.ProjectPrestigeState(world), world);

        Assert.Equal("Prismatic Trial", (string?)response["resetOffers"]![0]!["name"]);
        Assert.Null(response["prestige"]);
        Assert.False((bool)prestige["available"]!);
        Assert.Equal("ERR_REFUSED", (string?)prestige["reasonCode"]);
    }

    [Fact]
    public void Failure_names_the_missing_outcome_while_success_yields_to_fresh_poststate()
    {
        var failed = new PrestigeSubmission(PrestigePreflight.PostCommitFault,
            PrestigeNativeStage.NativeTransaction, NativeMutationOutcome.ExecutionThrew,
            new NativeMutationCallOutcome(1, 1, 0), "boom");
        var success = new PrestigeSubmission(PrestigePreflight.Proceeded,
            PrestigeNativeStage.Verification, NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1), "done");

        var failure = Json(GameMcpPrestigeProjection.Project(in failed), World());
        var committed = Json(GameMcpPrestigeProjection.Project(in success), World());

        Assert.Equal("next lifecycle", (string?)failure["missingOutcome"]);
        Assert.Single(failure.Properties());
        Assert.Empty(committed.Properties());
    }

    private static GameWorldState World(bool worldComplete = true, bool fetched = true)
    {
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(ResourceId, new BigDouble(80), new BigDouble(100),
            true, BigDouble.Zero, BigDouble.Zero, new BigDouble(100),
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false, false,
            false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
        var resource = new WorldResource(in reading, true, new BigDouble(20), 0.8, false,
            new BigDouble(80), BigDouble.Zero);
        var challenges = new[]
        {
            new WorldChallenge(Queued, 1, 1, true, false, 5, 10, 12, 30,
                true, true, false, new BigDouble(12), new BigDouble(30)),
            new WorldChallenge(Reward, 3, 2, true, true, 5, 10, 15, 40,
                true, true, false, new BigDouble(15), new BigDouble(40)),
        };
        var identities = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(ResourceId, "ResourceSO", "Persistent Light", "persistentLight"),
            new EntityIdentityName(Queued, "ChallengeSO", "Prismatic Trial", "prismaticTrial"),
            new EntityIdentityName(Reward, "ChallengeSO", "Reward Trial", "rewardTrial"),
        }).OrderBy(row => row.EntityId).ToArray();
        return new GameWorldState
        {
            CollectedAtEpoch = 31,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(31, identities),
            Resources = PublicationTable<WorldResource>.Create(new[] { resource }),
            Challenges = PublicationTable<WorldChallenge>.Create(challenges),
            ChallengeContext = new WorldChallengeContext(true, string.Empty,
                worldComplete, fetched, 2, 3, 3, ResourceId, 7, 11, 5, 4,
                PublicationTable<WorldChallengeReference>.Empty,
                PublicationTable<WorldChallengeReference>.Empty,
                PublicationTable<WorldChallengeReference>.Create(new[]
                {
                    new WorldChallengeReference(0, Queued),
                })),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus("challenges", WorldCategoryOutcome.Collected, 2, 0, string.Empty),
                new WorldCollectionCategoryStatus("challenge decisions", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
        };
    }

    private static GameMcpFrameContext Context(GameWorldState world, ulong generation)
        => GameMcpTestHarness.Context(world, generation);

    private static JObject Json(GameMcpValue value, GameWorldState world) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, world.EntityIdentities));
}
