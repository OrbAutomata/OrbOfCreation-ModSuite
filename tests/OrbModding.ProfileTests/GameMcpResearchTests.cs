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

namespace OrbModding.ProfileTests;

public sealed class GameMcpResearchTests
{
    private static readonly Guid ResearchId = Guid.Parse("f8000000-0000-0000-0000-000000000001");
    private static readonly Guid ResourceId = Guid.Parse("f8000000-0000-0000-0000-000000000002");
    private static readonly Guid TypeId = Guid.Parse("f8000000-0000-0000-0000-000000000003");

    [Fact]
    public void Tool_requires_an_explicit_amount_for_develop_only()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_research");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(new[] { "mode", "uuid" }, schema["required"]!.Values<string>());
        Assert.Equal(new[] { "develop", "pause", "resume", "cancel", "bonus" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.Null(schema["properties"]!["worldGeneration"]);
        Assert.NotNull(schema["properties"]!["amount"]);
    }

    [Fact]
    public void Validation_names_missing_uuid_and_rejects_removed_generation_metadata()
    {
        var inbox = new GameMcpFrameInbox();
        var router = new GameMcpProtocolRouter(inbox);
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(1, "tools/call",
            new JObject
            {
                ["name"] = "game_research",
                ["arguments"] = new JObject { ["mode"] = "develop" },
            }));
        var rejected = router.Handle(GameMcpAcceptanceFixture.Request(2, "tools/call",
            new JObject
            {
                ["name"] = "game_research",
                ["arguments"] = new JObject
                {
                    ["mode"] = "develop",
                    ["uuid"] = ResearchId.ToString("D"),
                    ["amount"] = 1,
                    ["worldGeneration"] = 9,
                },
            }));

        // One transport: an argument refusal is the page every other refusal is, and its sentence
        // names the offending field rather than repeating it in a parallel machine array.
        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: " +
            "required field 'uuid' is missing",
            GameMcpTestHarness.Page(missing));
        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: " +
            "field 'worldGeneration' is not accepted by game_research",
            GameMcpTestHarness.Page(rejected));
        Assert.Empty(inbox.ClaimPending());
    }

    [Fact]
    public void Develop_without_an_amount_asks_for_one_level_instead_of_failing_validation()
    {
        var operation = GameMcpProtocolRouter.BuildOperation(
            "game_research",
            new JObject
            {
                ["mode"] = "develop",
                ["uuid"] = ResearchId.ToString("D"),
            });

        Assert.Equal(1, operation.Amount);
    }

    [Fact]
    public void Research_row_is_a_named_complete_queue_decision_with_cost_holdings_and_progress()
    {
        var world = World();
        var response = Json(GameMcpWorldQuery.GetRow(GameMcpTestHarness.Context(world, 2801),
            "research", ResearchId.ToString("D")).Freeze(), world);
        var row = response["row"]!;

        Assert.Equal("Improved Casting", (string?)row["name"]);
        Assert.Equal("active", (string?)row["state"]);
        Assert.Equal(3, (int)row["queuedLevels"]!);
        Assert.Equal("queue", (string?)row["develop"]!["route"]);
        Assert.Equal(3, (int)row["develop"]!["maximumBatch"]!);
        Assert.Equal(2, (int)row["develop"]!["levels"]!);
        Assert.True((bool)row["develop"]!["affordable"]!);
        var cost = Assert.Single(row["develop"]!["costs"]!).Value<JObject>()!;
        Assert.Equal("Arcana", (string?)cost["resource"]!["name"]);
        Assert.Equal("20", (string?)cost["cost"]);
        Assert.Equal("80", (string?)cost["spendableAmount"]);
        Assert.Equal("40", (string?)row["investment"]![0]!["invested"]);
        Assert.Equal("Insight", (string?)row["researchTypes"]![0]!["researchType"]!["name"]);
        Assert.Equal(2, (int)row["researchTypes"]![0]!["remainingBonusLevels"]!);
    }

    [Fact]
    public void Research_cost_uses_spendable_amount_while_native_affordability_remains_authoritative()
    {
        var world = World(
            developmentCostAffordable: false,
            spendableAmount: 1,
            investmentRemaining: 100);
        var response = Json(GameMcpWorldQuery.GetRow(GameMcpTestHarness.Context(world, 2803),
            "research", ResearchId.ToString("D")).Freeze(), world);
        var develop = response["row"]!["develop"]!;
        var cost = Assert.Single(develop["costs"]!).Value<JObject>()!;

        Assert.Equal("1", (string?)cost["spendableAmount"]);
        Assert.False((bool)develop["affordable"]!);
        Assert.Equal("ERR_UNAFFORDABLE", (string?)develop["reasonCode"]);
        Assert.Null(cost["lifetimeAmount"]);
    }

    [Fact]
    public void Investment_names_the_remaining_price_beside_what_the_player_actually_holds()
    {
        var world = World(investmentRemaining: 60, heldAmount: 90);
        var response = Json(GameMcpWorldQuery.GetRow(GameMcpTestHarness.Context(world, 2804),
            "research", ResearchId.ToString("D")).Freeze(), world);
        var investment = Assert.Single(response["row"]!["investment"]!).Value<JObject>()!;

        Assert.Equal("40", (string?)investment["invested"]);
        Assert.Equal("100", (string?)investment["required"]);
        Assert.Equal("60", (string?)investment["remainingCost"]);
        Assert.Equal("90", (string?)investment["spendableAmount"]);
        Assert.Null(investment["availableToInvest"]);
        Assert.Null(investment["cost"]);
    }

    [Fact]
    public void An_unpublished_investment_resource_omits_holdings_instead_of_reporting_none()
    {
        var world = World(investmentRemaining: 60);
        var response = Json(GameMcpWorldQuery.GetRow(GameMcpTestHarness.Context(world, 2805),
            "research", ResearchId.ToString("D")).Freeze(), world);
        var investment = Assert.Single(response["row"]!["investment"]!).Value<JObject>()!;

        Assert.Equal("60", (string?)investment["remainingCost"]);
        Assert.Null(investment["spendableAmount"]);
    }

    /// <remarks>
    /// The game shuts its own develop gate once the price is out of reach, and that gate was what
    /// published <c>costs</c>. A row observed in play said "Needs 4 Orb Advancement (have 0)" while
    /// publishing no cost row at all, and <c>investment</c> — the native fill bar — named only
    /// resources that were not short. The sentence was the only place the blocking resource
    /// appeared, so the numbers behind it could not be read.
    /// </remarks>
    [Fact]
    public void A_row_refused_for_its_price_publishes_the_price_that_refused_it()
    {
        var world = World(
            developmentCostAffordable: false,
            withinDevelopRange: false,
            spendableAmount: 1,
            heldAmount: 1);
        var response = Json(GameMcpWorldQuery.GetRow(Pinned(world, 2807),
            "research", ResearchId.ToString("D")).Freeze(), world);
        var develop = response["row"]!["develop"]!;

        Assert.Equal("ERR_UNAFFORDABLE", (string?)develop["reasonCode"]);
        Assert.Equal("Needs 20 Arcana (have 1).", (string?)develop["reason"]);
        var cost = Assert.Single(develop["costs"]!).Value<JObject>()!;
        Assert.Equal("Arcana", (string?)cost["resource"]!["name"]);
        Assert.Equal("20", (string?)cost["cost"]);
        Assert.Equal("1", (string?)cost["spendableAmount"]);
        Assert.False((bool)cost["affordable"]!);
    }

    [Fact]
    public void A_refusal_that_is_not_about_price_publishes_no_price_verdict()
    {
        var world = World(complete: true, developmentCostAffordable: false,
            investmentRemaining: 100, heldAmount: 1);
        var response = Json(GameMcpWorldQuery.GetRow(GameMcpTestHarness.Context(world, 2806),
            "research", ResearchId.ToString("D")).Freeze(), world);
        var develop = response["row"]!["develop"]!;

        Assert.Equal("ERR_STATE", (string?)develop["reasonCode"]);
        Assert.Null(develop["affordable"]);
    }

    [Fact]
    public void Failure_names_the_missing_outcome_while_success_yields_to_fresh_world_poststate()
    {
        var failed = new ResearchSubmission(ResearchPreflight.VerificationFailed,
            ResearchNativeStage.Verification, NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(1, 1, 0), "unchanged");
        var success = new ResearchSubmission(ResearchPreflight.Proceeded,
            ResearchNativeStage.Verification, NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1), "changed");

        var failure = Json(GameMcpResearchProjection.Project(in failed), World());
        var committed = Json(GameMcpResearchProjection.Project(in success), World());

        Assert.Equal("requested research transition", (string?)failure["missingOutcome"]);
        Assert.Single(failure.Properties());
        Assert.Empty(committed.Properties());
    }

    [Fact]
    public void Committed_poststate_is_the_same_named_research_row_with_next_decisions()
    {
        var world = World();
        var response = Json(GameMcpWorldQuery.ProjectPostState(
            GameMcpTestHarness.Context(world, 2802), "research", ResearchId), world);

        Assert.Equal("Improved Casting", (string?)response["name"]);
        Assert.NotNull(response["develop"]);
        Assert.Null(response["pause"]);
        Assert.NotNull(response["cancel"]);
        Assert.Null(response["receipt"]);
        Assert.Null(response["payment"]);
    }

    /// <summary>
    /// A level takes research time, so a develop moves the queue and leaves the level count alone.
    /// The response has to name the count that moved instead of a level pair reading as a no-op.
    /// </summary>
    [Fact]
    public void A_develop_commit_publishes_the_queue_it_moved_and_omits_the_level_it_did_not()
    {
        var before = World(queuedLevels: 3);
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Research, 41, 8, "develop", ResearchId, Guid.Empty,
            "ResearchSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 51));
        var after = World(queuedLevels: 4);

        var delta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 52),
            command,
            GameMcpCommandResult.Committed("committed", 41, 8)), after);

        Assert.Equal(3, (int)delta["queuedLevels"]!["before"]!);
        Assert.Equal(4, (int)delta["queuedLevels"]!["after"]!);
        Assert.Equal("active", (string?)delta["state"]!["after"]);
        Assert.Null(delta["totalLevel"]);
    }

    [Fact]
    public void A_level_that_finished_inside_the_commit_is_the_one_case_that_carries_totalLevel()
    {
        var before = World(totalLevel: 1, queuedLevels: 4);
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Research, 41, 8, "develop", ResearchId, Guid.Empty,
            "ResearchSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 51));
        var after = World(totalLevel: 2, queuedLevels: 4);

        var delta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 52),
            command,
            GameMcpCommandResult.Committed("committed", 41, 8)), after);

        Assert.Equal(1, (int)delta["totalLevel"]!["before"]!);
        Assert.Equal(2, (int)delta["totalLevel"]!["after"]!);
    }

    [Fact]
    public void OrdinaryDevelopSettlementUsesTheActionSentinelInsteadOfWaitingForCompletion()
    {
        var completedAt = DateTime.UtcNow.Ticks;
        var before = World(isDeveloping: false, queueMode: false);
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Research, 41, 8, "develop", ResearchId, Guid.Empty,
            "ResearchSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 51));
        var started = World(
            isDeveloping: true, collectedAtUtcTicks: completedAt + 1, queueMode: false);
        var idle = World(
            isDeveloping: false, collectedAtUtcTicks: completedAt + 1, queueMode: false);

        Assert.True(GameMcpPostStateSettlement.IsReady(
            GameMcpTestHarness.Context(started, generation: 52), 51, completedAt, command));
        Assert.False(GameMcpPostStateSettlement.IsReady(
            GameMcpTestHarness.Context(idle, generation: 52), 51, completedAt, command));

        var timeout = Json(GameMcpPostStateSettlement.TimedOut(
            command, GameMcpTestHarness.Context(idle, generation: 52)), idle);
        Assert.Equal("ERR_STATE", (string?)timeout["postStateUnavailable"]!["reasonCode"]);
        Assert.Contains("before was idle and the settled target is idle",
            (string?)timeout["postStateUnavailable"]!["reason"]);
    }

    [Fact]
    public void QueueDevelopTimeoutNamesTheExpectedAndObservedPendingLevels()
    {
        var before = World(isDeveloping: true, queueMode: true);
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Research, 41, 8, "develop", ResearchId, Guid.Empty,
            "ResearchSO", 2, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 51));
        var unchanged = World(isDeveloping: true, queueMode: true);

        var timeout = Json(GameMcpPostStateSettlement.TimedOut(
            command, GameMcpTestHarness.Context(unchanged, generation: 52)), unchanged);

        Assert.Equal("ERR_STATE", (string?)timeout["postStateUnavailable"]!["reasonCode"]);
        Assert.Contains("research queue has 4 pending levels, not the requested 6",
            (string?)timeout["postStateUnavailable"]!["reason"]);
    }

    [Theory]
    [InlineData(false, "before was not published and the settled target is developing")]
    [InlineData(true, "research queue before state was not published")]
    public void DevelopTimeoutDoesNotInventAnAbsentBeforeState(
        bool queueMode,
        string expectedReason)
    {
        var before = new GameWorldState();
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Research, 41, 8, "develop", ResearchId, Guid.Empty,
            "ResearchSO", 2, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 51));
        var after = World(isDeveloping: true, queueMode: queueMode);

        var timeout = Json(GameMcpPostStateSettlement.TimedOut(
            command, GameMcpTestHarness.Context(after, generation: 52)), after);

        Assert.Equal("ERR_STATE", (string?)timeout["postStateUnavailable"]!["reasonCode"]);
        Assert.Contains(expectedReason,
            (string?)timeout["postStateUnavailable"]!["reason"]);
    }

    private static GameWorldState World(
        bool developmentCostAffordable = true,
        double spendableAmount = 80,
        double investmentRemaining = 60,
        bool isDeveloping = true,
        long? collectedAtUtcTicks = null,
        bool queueMode = true,
        bool complete = false,
        double? heldAmount = null,
        int queuedLevels = 3,
        int totalLevel = 1,
        bool withinDevelopRange = true)
    {
        var decision = new WorldResearchDecision(
            queueMode,
            3,
            queuedLevels,
            developmentCostAffordable ? 2 : 0,
            1,
            new BigDouble(30), new BigDouble(30), new BigDouble(0.5), true, 2,
            developmentCostAffordable,
            PublicationTable<WorldResearchCost>.Create(new[]
            {
                new WorldResearchCost(
                    ResourceId,
                    new BigDouble(20),
                    new BigDouble(spendableAmount)),
            }),
            PublicationTable<WorldResearchInvestment>.Create(new[]
            {
                new WorldResearchInvestment(ResourceId, new BigDouble(40),
                    new BigDouble(100), new BigDouble(investmentRemaining)),
            }),
            PublicationTable<WorldResearchTypeDecision>.Create(new[]
            {
                new WorldResearchTypeDecision(TypeId, 2, 1, 5),
            }));
        var modifiers = new RawResearchModifiers(BigDouble.Zero, BigDouble.Zero,
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero);
        var research = new WorldResearch(ResearchId, 1, 2, 0, 0, 10, 60,
            isDeveloping, true, false, true, true, complete, true, withinDevelopRange,
            true, true, true, true,
            1, 1, 0, totalLevel, 10, false, 2, 1, new BigDouble(60), 1, 1,
            PublicationTable<WorldResearchRequirementAdjustment>.Empty, in modifiers, in decision);
        var identities = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(ResearchId, "ResearchSO", "Improved Casting", "improvedCasting"),
            new EntityIdentityName(ResourceId, "ResourceSO", "Arcana", "arcana"),
            new EntityIdentityName(TypeId, "ResearchTypeSO", "Insight", "insight"),
        }).OrderBy(row => row.EntityId).ToArray();
        return new GameWorldState
        {
            CollectedAtEpoch = 41,
            CollectedAtUtcTicks = collectedAtUtcTicks ?? DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(41, identities),
            Resources = heldAmount is null
                ? PublicationTable<WorldResource>.Empty
                : PublicationTable<WorldResource>.Create(new[] { Held(heldAmount.Value) }),
            Research = PublicationTable<WorldResearch>.Create(new[] { research }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus("research", WorldCategoryOutcome.Collected,
                    1, 0, string.Empty),
            }),
        };
    }

    /// <summary>
    /// Choosing among 148 researches used to need a detail page per candidate. `state` already says
    /// complete, so no second column repeats it, and the cost verdict rides every row rather than
    /// only the rows whose develop gate happened to be open.
    /// </summary>
    [Fact]
    public void A_research_list_row_carries_what_a_caller_picks_the_next_research_by()
    {
        var world = World(developmentCostAffordable: false);
        var response = Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(world, 2811), "research", 0, 50).Freeze(), world);
        var row = Assert.IsType<JArray>(response["rows"]).Values<JObject>().Single()!;

        Assert.Equal("Improved Casting", (string?)row["name"]);
        Assert.Equal("active", (string?)row["state"]);
        Assert.Equal(1, (int)row["totalLevel"]!);
        Assert.Equal(3, (int)row["queuedLevels"]!);
        Assert.False((bool)row["canDevelop"]!);
        Assert.False((bool)row["affordable"]!);
        Assert.Null(row["complete"]);
    }

    [Fact]
    public void Develop_over_ask_carries_the_ceiling_its_own_sentence_names()
    {
        var refused = Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            GameMcpResearchProjection.Project(
                ResearchSubmission.Reject(
                    ResearchPreflight.AmountUnavailable,
                    "Research Queue Mode is off, so one develop starts one level and this " +
                    "call takes at most 1 level.",
                    1)),
            EntityIdentityCatalogSnapshot.Unbound(1)));

        Assert.Equal(1, (int)refused["maximumAmount"]!);

        var noCeiling = Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            GameMcpResearchProjection.Project(
                ResearchSubmission.Reject(
                    ResearchPreflight.DevelopUnavailable,
                    "The next research level is unavailable or unaffordable.")),
            EntityIdentityCatalogSnapshot.Unbound(1)));

        Assert.Null(noCeiling["maximumAmount"]);
    }

    private static WorldResource Held(double quantity)
    {
        var rateInputs = default(RawResourceRateInputs);
        var modifiers = default(RawResourceModifiers);
        var traits = default(RawResourceTraits);
        var held = new BigDouble(quantity);
        var reading = new RawResourceSample(
            ResourceId, held, new BigDouble(-1), true,
            BigDouble.Zero, BigDouble.Zero, new BigDouble(100),
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            false, false, false, 0, Guid.Empty,
            in rateInputs, in traits, in modifiers);
        return new WorldResource(
            in reading, true, BigDouble.Zero, 0d, false, held, BigDouble.Zero);
    }

    /// <summary>
    /// A frame pinned to the world's own identity catalog, the way a live frame is. The shared
    /// harness swaps in its own, which leaves a sentence composed at read time naming a UUID the
    /// normalizer then resolves for every other field.
    /// </summary>
    private static GameMcpFrameContext Pinned(GameWorldState world, ulong generation)
    {
        var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(generation));
        return GameMcpTestHarness.Context(publisher.ReadLatest());
    }

    private static JObject Json(GameMcpValue value, GameWorldState world) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, world.EntityIdentities));
}
