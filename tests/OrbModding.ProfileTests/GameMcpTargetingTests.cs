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
        Assert.Equal("uuid", (string?)missing.Body!["error"]!["data"]!["validationErrors"]![0]!["field"]);
        Assert.Equal("missing_required", (string?)missing.Body["error"]!["data"]!["validationErrors"]![0]!["code"]);
        Assert.Equal("uuid", (string?)unexpected.Body!["error"]!["data"]!["validationErrors"]![0]!["field"]);
        Assert.Equal("unexpected_for_mode", (string?)unexpected.Body["error"]!["data"]!["validationErrors"]![0]!["code"]);
    }

    [Fact]
    public void ReadSurfaceCarriesNamedOrderedCandidatesAndCurrentHoldings()
    {
        var json = GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(World()), "targeting", 0, 10));
        Assert.True(json["rows"] is JArray, json.ToString());
        var rows = (JArray)json["rows"]!;
        var row = Assert.IsType<JObject>(Assert.Single(rows));
        Assert.True((bool)row["pending"]!);
        Assert.Equal("Targeted effect", (string?)row["owner"]);
        var candidates = row["candidates"]!.OfType<JObject>().ToArray();
        Assert.Equal(new[] { First.ToString("D"), Second.ToString("D") },
            candidates.Select(candidate => (string?)candidate["uuid"]));
        Assert.Equal(new[] { "Alchemic Ability", "Alchemic Command" },
            candidates.Select(candidate => (string?)candidate["name"]));
        Assert.Equal(3, (int)candidates[0]["level"]!);
        Assert.Equal(0, (int)candidates[0]["queuedLevels"]!);
        Assert.Equal(5, (int)candidates[0]["effectiveLevel"]!);

        // The sum of built and building levels has no badge and no name on the wire.
        Assert.Null(candidates[0]["committedLevel"]);
        Assert.True((bool)candidates[0]["available"]!);
        Assert.True((bool)row["randomize"]!["available"]!);
        Assert.Null(row["cancel"]);
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
        1, string.Empty, string.Empty, false, false);

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
