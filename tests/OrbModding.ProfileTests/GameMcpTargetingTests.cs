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

public sealed class GameMcpTargetingTests
{
    private static readonly Guid First = Guid.Parse("b1cf414e-ae5a-425d-8a0e-4ce11b79017a");
    private static readonly Guid Second = Guid.Parse("5d5270f9-24af-4dba-9a64-eed93ae07d41");

    [Fact]
    public void ToolHasOneConditionalTargetAndNoGenerationOrReceiptKnobs()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_targeting");
        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(new[] { "mode" }, schema["required"]!.Values<string>());
        Assert.Equal(new[] { "submit", "randomize" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.NotNull(schema["properties"]!["uuid"]);
        Assert.Null(schema["properties"]!["worldGeneration"]);
        Assert.Null(schema["properties"]!["receipt"]);
    }

    [Fact]
    public void ConditionalTargetValidationNamesTheExactField()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var missing = Call(router, 1, new JObject { ["mode"] = "submit" });
        var unexpected = Call(router, 2, new JObject
        {
            ["mode"] = "randomize", ["uuid"] = First.ToString("D"),
        });
        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: required " +
            "field 'uuid' is missing for mode 'submit'", GameMcpTestHarness.Page(missing));
        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: field " +
            "'uuid' is not accepted for mode 'randomize'", GameMcpTestHarness.Page(unexpected));
    }

    [Fact]
    public void ReadSurfaceCarriesNamedOrderedCandidatesAndCurrentHoldings()
    {
        var json = GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(World()), "targeting", 0, 10));
        Assert.True(json["rows"] is JArray, json.ToString());
        var rows = (JArray)json["rows"]!;
        var row = Assert.IsType<JObject>(Assert.Single(rows));

        // The row is the request, so its existence is the whole of "a request is pending" — and the
        // owner is named in the words the player sees, not by the class the game's code gives it or
        // the class of the selection it opened.
        Assert.Equal(new[] { "owner", "candidates" },
            row.Properties().Select(property => property.Name));
        Assert.Equal("Targeted effect", (string?)row["owner"]);

        // The candidate list is what `limit` and `offset` page, so it is a page: its rows, how many
        // there are, and where to resume. Strongest effective level first, because that is the
        // column the caller picks by.
        var page = Assert.IsType<JObject>(row["candidates"]);
        Assert.Equal(new[] { "rows", "total" }, page.Properties().Select(property => property.Name));
        Assert.Equal(2, (int)page["total"]!);
        var candidates = page["rows"]!.OfType<JObject>().ToArray();
        Assert.Equal(
            new[] { GameMcpTestHarness.Handle(Second), GameMcpTestHarness.Handle(First) },
            candidates.Select(candidate => (string?)candidate["uuid"]));
        Assert.Equal(new[] { "Alchemic Command", "Alchemic Ability" },
            candidates.Select(candidate => (string?)candidate["name"]));
        Assert.Equal(7, (int)candidates[0]["level"]!);
        Assert.Equal(0, (int)candidates[0]["queuedLevels"]!);
        Assert.Equal(7, (int)candidates[0]["effectiveLevel"]!);

        // The sum of built and building levels has no badge and no name on the wire.
        Assert.Null(candidates[0]["committedLevel"]);
        Assert.True((bool)candidates[0]["available"]!);

        // Whether a roll would land is whether there is anything to roll over, which the candidates
        // are. A column restating them said nothing they did not.
        Assert.Null(row["randomize"]);
        Assert.Null(candidates[0]["reasonCode"]);
        Assert.Null(candidates[0]["reason"]);
        Assert.Null(row["cancel"]);
    }

    /// <summary>
    /// One request is one row, so paging the rows could only ever hand back the same row or
    /// nothing while the list a caller reads came back whole. <c>limit</c> and <c>offset</c> reach
    /// the candidates, the page says how many there are and where to resume, and asking past the
    /// last candidate answers with an empty page the way every other category does.
    /// </summary>
    [Fact]
    public void Limit_and_offset_page_the_candidates_and_every_candidate_stays_reachable()
    {
        var context = GameMcpTestHarness.Context(World());
        var first = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(context, "targeting", 0, 1));
        var second = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(context, "targeting", 1, 1));
        var past = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(context, "targeting", 2, 1));

        Assert.Equal(
            new[]
            {
                "rows 1/1:",
                "  owner: Targeted effect",
                "  candidates 1/2 next=1",
                "  [id | name | effectiveLevel | level | queuedLevels | available | position]",
                "  " + GameMcpTestHarness.Handle(Second) + " | Alchemic Command | 7 | 7 | 0 | " +
                "yes | 2",
            },
            GameMcpTextPage.Render(first).TrimEnd('\n').Split('\n'));
        Assert.Equal(
            GameMcpTestHarness.Handle(First),
            (string?)second["rows"]![0]!["candidates"]!["rows"]![0]!["uuid"]);
        Assert.Null(second["rows"]![0]!["candidates"]!["nextOffset"]);
        Assert.Empty(past["rows"]!.Values<JObject>());
    }

    [Fact]
    public void CommittedSubmitReturnsNamedTargetAndCompleteNextRequestOnly()
    {
        var submission = new TargetingSubmission(TargetingPreflight.Proceeded,
            TargetingNativeStage.Verification, NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1), First, "verified");
        var mapped = TargetingActionResultMapper.Map(in submission);
        var command = Command("submit", First);
        var terminal = GameMcpCommandResult.FromAction(in mapped, command.Kind, 9, 3,
            submission.Reason, GameMcpTargetingProjection.Project(in submission));
        terminal = terminal.WithDetails(GameMcpWorldQuery.ProjectTargetingPostState(
            GameMcpTestHarness.Context(World()),
            GameMcpTargetingProjection.SubmittedTarget(terminal.Details)));

        var success = GameMcpTestHarness.Json(terminal.Project(command));
        Assert.Equal(new[] { "status", "submittedTarget", "targeting" },
            success.Properties().Select(property => property.Name));
        Assert.Equal("committed", (string?)success["status"]);
        Assert.Null(success["code"]);
        Assert.Equal("Alchemic Ability", (string?)success["submittedTarget"]!["name"]);
        Assert.NotNull(success["targeting"]!["candidates"]);
        Assert.Null(success["receipt"]);
        Assert.DoesNotContain("payment", success.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attempt", success.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailureNamesTheMissingSettlementWithoutPersistentState()
    {
        var submission = new TargetingSubmission(TargetingPreflight.VerificationFailed,
            TargetingNativeStage.Verification, NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(1, 1, 0), Guid.Empty, "failed");
        var failure = GameMcpTestHarness.Json(GameMcpTargetingProjection.Project(in submission));
        Assert.Equal("target request settlement", (string?)failure["missingOutcome"]);
        Assert.Single(failure.Properties());
    }

    [Fact]
    public void AdmissionAndOwnershipUseOneTargetingCapability()
    {
        var world = World();
        Assert.True(GameMcpEntityCapabilityMap.Contains(
            world, First, GameMcpCommandKind.Targeting, out var submitReason), submitReason);
        Assert.True(GameMcpEntityCapabilityMap.Contains(
            world, Guid.Empty, GameMcpCommandKind.Targeting, out var cancelReason), cancelReason);
        Assert.True(GameMcpEntityCapabilityMap.Supports("targeting", GameMcpCommandKind.Targeting));
        var registry = new ActionFamilyOwnershipRegistry();
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile()).Current;
        using var ownership = new AutomataActionFamilyOwnership(registry);
        ownership.Refresh(configuration, lifecycleReady: true);
        Assert.True(ownership.TryBeginGameMcpOperation(
            GameMcpCommandKind.Targeting, "submit", out var scope, out var reason), reason);
        using (scope) Assert.True(ownership.TryCaptureTargetingMutationPermit());
        Assert.False(ownership.TryCaptureTargetingMutationPermit());
    }

    private static GameMcpProtocolResponse Call(
        GameMcpProtocolRouter router, int id, JObject arguments) => router.Handle(
            GameMcpAcceptanceFixture.Request(id, "tools/call", new JObject
            {
                ["name"] = "game_targeting", ["arguments"] = arguments,
            }));

    private static GameMcpCommand Command(string mode, Guid id) => new(
        1, GameMcpCommandKind.Targeting, 9, 3, mode, id, Guid.Empty,
        mode == "submit" ? "StructureSO" : "TargetingManager+TargetLink",
        1, string.Empty, string.Empty, false);

    [Fact]
    public void NothingPendingIsItsOwnRefusalRatherThanAnUnsupportedTarget()
    {
        var operation = GameMcpProtocolRouter.BuildOperation(
            "game_targeting", new JObject { ["mode"] = "randomize" });
        var world = World() with
        {
            Targeting = PublicationTable<WorldTargetingRequest>.Create(
                Array.Empty<WorldTargetingRequest>()),
        };

        // The verb exists and the target is fine; the screen simply has no selection open, and
        // "unsupported_action_target" sent a caller looking for a different target.
        Assert.False(Plugin.TryPrepareGameMcpCommand(
            new GameMcpFrameOperation(1, operation),
            GameMcpTestHarness.Context(world),
            out _,
            out var failure));
        Assert.Equal("no_pending_target", failure.Code);
        Assert.Contains("no target selection", failure.Reason, StringComparison.Ordinal);
    }

    private static GameWorldState World()
    {
        var candidates = PublicationTable<WorldTargetingCandidate>.Create(new[]
        {
            new WorldTargetingCandidate(0, First), new WorldTargetingCandidate(1, Second),
        });
        return new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = GameMcpTestHarness.EntityCatalog,
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "targeting", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
            Targeting = PublicationTable<WorldTargetingRequest>.Create(new[]
            {
                new WorldTargetingRequest(
                    "Targeted effect", "EffectSO", "TargetStructure", true, candidates),
            }),
            Structures = PublicationTable<WorldStructure>.Create(new[]
            {
                Structure(First, 3, 5), Structure(Second, 7, 7),
            }),
        };
    }

    private static WorldStructure Structure(Guid id, int committed, int effective)
    {
        var modifiers = new RawStructureModifiers(
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero);
        var raw = new RawStructureSample(
            id, Guid.Empty, new BigDouble(committed), BigDouble.Zero, true,
            0, 0, 0, BigDouble.Zero, BigDouble.Zero, false, 0, 0f,
            false, false, 0, false, 0, Guid.Empty, in modifiers);
        return new WorldStructure(in raw, new BigDouble(committed), false,
            new BigDouble(effective), 0d);
    }
}
