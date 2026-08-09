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
            candidate => (string?)candidate["name"] == "game_prestige");
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
                ["name"] = "game_prestige",
                ["arguments"] = new JObject { ["confirm"] = false },
            }));

        Assert.Equal(-32602, (int?)response.Body!["error"]!["code"]);
        Assert.Contains("confirm must be true", (string?)response.Body["error"]!["message"]);
        Assert.Empty(inbox.ClaimPending());
    }

    [Fact]
    public void Challenge_read_carries_complete_named_prestige_decision_and_current_holding()
    {
        var world = World();
        var context = Context(world, 2601);
        var response = Json(GameMcpWorldQuery.ListRows(
            context, "challenges", 0, 50).Freeze(), world);

        Assert.True(response["challengeState"] is not null, response.ToString());
        var prestige = response["challengeState"]!["prestige"]!;
        Assert.Equal(7, (int)prestige["currentTimeAdvancements"]!);
        Assert.Equal(11, (int)prestige["startingTimeAdvancements"]!);
        Assert.Equal(5, (int)prestige["previousStartingTimeAdvancements"]!);
        Assert.Equal(6, (int)prestige["changeFromPrevious"]!);
        Assert.Equal(4, (int)prestige["resetCount"]!);
        Assert.Equal("Persistent Light", (string?)prestige["persistentResource"]!["resource"]!["name"]);
        Assert.Equal("80", (string?)prestige["persistentResource"]!["amount"]);
        Assert.Equal("Prismatic Trial", (string?)prestige["survivingChallengeSelections"]![0]!["name"]);
        Assert.Equal("Reward Trial", (string?)prestige["survivingChallengeRewards"]![0]!["name"]);
        Assert.True((bool)prestige["reset"]!["available"]!);
    }

    [Fact]
    public void Committed_poststate_returns_the_fresh_scene_prestige_and_challenge_decisions()
    {
        var world = World(worldComplete: false, fetched: false);
        var response = Json(GameMcpWorldQuery.ProjectPrestigePostState(Context(world, 2602)), world);

        Assert.Equal("Main", (string?)response["scene"]);
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
        var response = Json(GameMcpWorldQuery.ListRows(
            Context(world, 2603), "challenges", 0, 50).Freeze(), world);

        Assert.Equal("Prismatic Trial", (string?)response["challengeState"]!["prestigeOffers"]![0]!["name"]);
        Assert.False((bool)response["challengeState"]!["prestige"]!["available"]!);
        Assert.Equal("ERR_REFUSED", (string?)response["challengeState"]!["prestige"]!["reasonCode"]);
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
