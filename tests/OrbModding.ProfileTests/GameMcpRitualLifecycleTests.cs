using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpRitualLifecycleTests
{
    private static readonly Guid RitualId =
        Guid.Parse("fa000000-0000-0000-0000-000000000001");
    private static readonly Guid ResourceId =
        Guid.Parse("fa000000-0000-0000-0000-000000000002");

    [Fact]
    public void Tool_exposes_only_the_live_ritual_list_controls()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_ritual");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        Assert.Equal(new[] { "mode", "uuid" },
            tool["inputSchema"]!["required"]!.Values<string>());
        Assert.Equal(
            new[] { "select", "deselect", "set_level", "activate", "cancel_duration", "end" },
            tool["inputSchema"]!["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.Null(tool["inputSchema"]!["properties"]!["expectedNativeType"]);
        var operation = GameMcpProtocolRouter.BuildOperation("game_ritual", new JObject
        {
            ["mode"] = "activate",
            ["uuid"] = RitualId.ToString("D"),
        });
        Assert.Equal(GameMcpOperationClass.Gameplay, operation.Classification);
    }

    [Fact]
    public void Set_level_requires_level_and_other_modes_reject_it()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(1, "tools/call",
            new JObject
            {
                ["name"] = "game_ritual",
                ["arguments"] = new JObject
                {
                    ["mode"] = "set_level",
                    ["uuid"] = RitualId.ToString("D"),
                },
            }));
        var extra = router.Handle(GameMcpAcceptanceFixture.Request(2, "tools/call",
            new JObject
            {
                ["name"] = "game_ritual",
                ["arguments"] = new JObject
                {
                    ["mode"] = "select",
                    ["uuid"] = RitualId.ToString("D"),
                    ["level"] = 2,
                },
            }));

        Assert.Contains(missing.Body!["error"]!["data"]!["validationErrors"]!.Values<JObject>(),
            error => (string?)error["code"] == "missing_required" &&
                     (string?)error["field"] == "level");
        Assert.Contains(extra.Body!["error"]!["data"]!["validationErrors"]!.Values<JObject>(),
            error => (string?)error["code"] == "unexpected_for_mode" &&
                     (string?)error["field"] == "level");
    }

    [Fact]
    public void Set_level_refuses_the_level_the_native_minus_button_cannot_reach()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_ritual");
        Assert.Equal(1, (int)tool["inputSchema"]!["properties"]!["level"]!["minimum"]!);

        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var response = router.Handle(GameMcpAcceptanceFixture.Request(1, "tools/call",
            new JObject
            {
                ["name"] = "game_ritual",
                ["arguments"] = new JObject
                {
                    ["mode"] = "set_level",
                    ["uuid"] = RitualId.ToString("D"),
                    ["level"] = 0,
                },
            }));

        // The floor is real and is stated. The ceiling was int.MaxValue - 1, and printing it beside
        // the floor published 2147483646 as if the game had chosen it; the game's own ceiling on
        // this dial was 240.
        Assert.Contains(
            "level must be 1 or greater",
            response.Body!["error"]!.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "2147483646",
            response.Body!["error"]!.ToString(),
            StringComparison.Ordinal);
        Assert.Null(tool["inputSchema"]!["properties"]!["level"]!["maximum"]);
    }

    [Fact]
    public void Selected_ritual_publishes_both_bounds_of_the_native_starting_level_control()
    {
        var world = World(selected: true, level: 4, activeInstances: 1);
        var response = Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, generation: 803),
            "rituals", RitualId.ToString("D")).Freeze(), world);

        var setLevel = response["row"]!["setLevel"]!;
        Assert.Equal(1, (int)setLevel["minimum"]!);
        Assert.Equal(8, (int)setLevel["maximum"]!);
    }

    [Fact]
    public void Selected_ritual_detail_carries_only_the_live_next_decisions_and_named_costs()
    {
        var world = World(selected: true, level: 4, activeInstances: 1);
        var response = Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, generation: 801),
            "rituals", RitualId.ToString("D")).Freeze(), world);

        var row = response["row"]!;
        Assert.Equal("Moon Rite", (string?)row["name"]);
        Assert.True((bool)row["selected"]!);
        Assert.True((bool)row["setLevel"]!["available"]!);
        Assert.Equal(8, (int)row["setLevel"]!["maximum"]!);
        Assert.True((bool)row["activate"]!["available"]!);
        var cost = Assert.Single(row["activate"]!["costs"]!.Values<JObject>());
        Assert.Equal("Knowledge", (string?)cost["resource"]!["name"]);
        Assert.Equal("5", (string?)cost["cost"]);
        Assert.Equal("80", (string?)cost["spendableAmount"]);

        // The same per-row verdict every other cost row carries, so no caller compares two
        // formatted magnitudes for itself.
        Assert.True((bool)cost["affordable"]!);
        Assert.True((bool)row["cancelDuration"]!["available"]!);
    }

    [Fact]
    public void Unselected_ritual_has_no_speculative_price_ledger()
    {
        var world = World(selected: false, level: 0, activeInstances: 0);
        var response = Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, generation: 802),
            "rituals", RitualId.ToString("D")).Freeze(), world);

        var activate = response["row"]!["activate"]!;
        Assert.False((bool)activate["available"]!);
        Assert.Equal("not_selected", (string?)activate["reasonCode"]);
        Assert.Null(activate["affordable"]);
        Assert.Null(activate["costs"]);
        Assert.Null(activate["completionCosts"]);
    }

    [Fact]
    public void Finished_run_leaves_its_record_on_the_row_the_next_read_returns()
    {
        // The results modal is gone the moment it is dismissed, and the record behind it survives
        // until the next activation. A caller that reads the ritual afterwards asked "how did that
        // go", and the row answered with nothing at all.
        var world = World(
            selected: true, level: 4, activeInstances: 1, inBattle: false, wavesCompleted: 7,
            spoils: new[] { new WorldRitualSpoil(ResourceId, new BigDouble(12)) });
        var row = Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, generation: 804),
            "rituals", RitualId.ToString("D")).Freeze(), world)["row"]!;

        Assert.Equal(13, (int)row["waveTotal"]!);
        Assert.Equal("succeeded", (string?)row["lastRun"]!["result"]);
        Assert.Equal(7, (int)row["lastRun"]!["wavesCompleted"]!);
        var spoil = Assert.Single(row["lastRun"]!["spoils"]!.Values<JObject>());
        Assert.Equal("Knowledge", (string?)spoil!["resource"]!["name"]);
        Assert.Equal("12", (string?)spoil["amount"]);
        Assert.Null(row["wavesCompleted"]);
    }

    [Fact]
    public void Ritual_nobody_has_played_reports_no_run_rather_than_a_failed_one()
    {
        // IsFailedRun() is wavesCompleted < 5, so an unconditional verdict calls every untouched
        // ritual a failure. A cleared count with no spoils is exactly the never-run state.
        var world = World(selected: false, level: 0, activeInstances: 0);
        var row = Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, generation: 805),
            "rituals", RitualId.ToString("D")).Freeze(), world)["row"]!;

        Assert.Null(row["lastRun"]);
        Assert.Null(row["wavesCompleted"]);
        Assert.Equal(1, (int)row["waveTotal"]!);
    }

    [Fact]
    public void Run_in_progress_reports_its_own_progress_and_never_a_verdict()
    {
        var world = World(
            selected: true, level: 4, activeInstances: 0, inBattle: true, wavesCompleted: 3,
            spoils: new[] { new WorldRitualSpoil(ResourceId, new BigDouble(4)) });
        var row = Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, generation: 806),
            "rituals", RitualId.ToString("D")).Freeze(), world)["row"]!;

        Assert.Equal(3, (int)row["wavesCompleted"]!);
        Assert.Equal(13, (int)row["waveTotal"]!);
        Assert.Equal("4", (string?)Assert.Single(row["spoils"]!.Values<JObject>())!["amount"]);
        Assert.Null(row["lastRun"]);
    }

    [Fact]
    public void Settled_select_delta_uses_the_new_world_and_returns_the_next_decision()
    {
        var before = World(selected: false, level: 0, activeInstances: 0);
        var after = World(selected: true, level: 3, activeInstances: 0);
        var command = new GameMcpCommand(1, GameMcpCommandKind.RitualLifecycle,
            9, 3, "select", RitualId, Guid.Empty, "RitualSO",
            1, string.Empty, string.Empty, false, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 91));

        var delta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 92), command,
            GameMcpCommandResult.Committed("committed", 9, 3)), after);

        Assert.False((bool)delta["selected"]!["before"]!);
        Assert.True((bool)delta["selected"]!["after"]!);
        Assert.True((bool)delta["next"]!["activate"]!["available"]!);
        Assert.Equal("5", (string?)delta["next"]!["activate"]!["costs"]![0]!["cost"]);
    }

    [Fact]
    public void Settled_end_delta_reports_the_observed_active_battle_clear()
    {
        var before = World(selected: true, level: 4, activeInstances: 0, inBattle: true);
        var after = World(selected: true, level: 4, activeInstances: 0, inBattle: false);
        var command = new GameMcpCommand(1, GameMcpCommandKind.RitualLifecycle,
            9, 3, "end", RitualId, Guid.Empty, "RitualSO",
            1, string.Empty, string.Empty, false, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 93));

        var delta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 94), command,
            GameMcpCommandResult.Committed("committed", 9, 3)), after);

        Assert.Equal(RitualId.ToString("D"), (string?)delta["uuid"]);
        Assert.True((bool)delta["activeBattle"]!["before"]!);
        Assert.False((bool)delta["activeBattle"]!["after"]!);
    }

    [Fact]
    public void Settled_end_delta_reports_the_battle_result_and_what_is_possible_next()
    {
        // RitualSO.End() writes neither wavesCompleted nor currentSpoils — only the next Initiate()
        // clears them — so a run stopped on its first wave still reads 1 after the battle ends.
        var before = World(
            selected: true, level: 4, activeInstances: 0, inBattle: true, wavesCompleted: 1);
        var after = World(
            selected: true, level: 4, activeInstances: 2, inBattle: false, wavesCompleted: 1,
            spoils: new[] { new WorldRitualSpoil(ResourceId, new BigDouble(12)) });
        var command = new GameMcpCommand(1, GameMcpCommandKind.RitualLifecycle,
            9, 3, "end", RitualId, Guid.Empty, "RitualSO",
            1, string.Empty, string.Empty, false, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 95));

        var delta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 96), command,
            GameMcpCommandResult.Committed("committed", 9, 3)), after);

        Assert.Equal(1, (int)delta["wavesCompleted"]!["before"]!);
        Assert.Equal(1, (int)delta["wavesCompleted"]!["after"]!);
        Assert.Equal(5, (int)delta["reachedLevel"]!);
        Assert.Equal(2, (int)delta["activeInstances"]!);

        // The results modal says the run failed and lists what it banked; the wire said neither,
        // and a wave count alone cannot separate a clean win from a run stopped on the last wave.
        Assert.Equal("failed", (string?)delta["result"]);
        var spoil = Assert.Single(delta["spoils"]!.Values<JObject>());
        Assert.Equal("Knowledge", (string?)spoil!["resource"]!["name"]);
        Assert.Equal("12", (string?)spoil["amount"]);

        // Parity with select, deselect, set_level and activate: end says what is possible next.
        Assert.True((bool)delta["next"]!["selected"]!);
        Assert.Equal(4, (int)delta["next"]!["setLevel"]!["current"]!);
        Assert.True((bool)delta["next"]!["activate"]!["available"]!);
    }

    [Fact]
    public void A_cleared_run_ends_with_the_verdict_its_own_results_modal_shows()
    {
        var before = World(
            selected: true, level: 4, activeInstances: 0, inBattle: true, wavesCompleted: 6);
        var after = World(
            selected: true, level: 4, activeInstances: 0, inBattle: false, wavesCompleted: 7,
            spoils: new[] { new WorldRitualSpoil(ResourceId, new BigDouble(30)) });
        var command = new GameMcpCommand(1, GameMcpCommandKind.RitualLifecycle,
            9, 3, "end", RitualId, Guid.Empty, "RitualSO",
            1, string.Empty, string.Empty, false, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 97));

        var delta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 98), command,
            GameMcpCommandResult.Committed("committed", 9, 3)), after);

        Assert.Equal(7, (int)delta["wavesCompleted"]!["after"]!);
        Assert.Equal("succeeded", (string?)delta["result"]);
        Assert.Equal("30", (string?)Assert.Single(delta["spoils"]!.Values<JObject>())!["amount"]);
    }

    [Fact]
    public void Activate_reports_the_battle_it_started_and_never_a_verdict_for_it()
    {
        // An activate whose settled capture has not flipped inBattle yet takes the same branch as
        // end. Gating on the battle flag answered it with a verdict and an empty spoils list for a
        // run that is starting; the mode is what says whether a result exists.
        var before = World(selected: true, level: 4, activeInstances: 0, wavesCompleted: 3);
        var after = World(selected: true, level: 4, activeInstances: 0, wavesCompleted: 0);
        var command = new GameMcpCommand(1, GameMcpCommandKind.RitualLifecycle,
            9, 3, "activate", RitualId, Guid.Empty, "RitualSO",
            1, string.Empty, string.Empty, false, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 99));

        var delta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 100), command,
            GameMcpCommandResult.Committed("committed", 9, 3)), after);

        Assert.False((bool)delta["activeBattle"]!["after"]!);
        Assert.Null(delta["result"]);
        Assert.Null(delta["spoils"]);
    }

    private static GameWorldState World(
        bool selected,
        int level,
        int activeInstances,
        bool inBattle = false,
        int wavesCompleted = 0,
        WorldRitualSpoil[]? spoils = null)
    {
        // The verdict is never an independent fixture input: RitualSO.IsFailedRun() is
        // wavesCompleted < 5, so a world that sets the two separately can assert a result the wave
        // count it publishes contradicts.
        var failedRun = wavesCompleted < 5;
        var activation = selected
            ? PublicationTable<WorldRitualCost>.Create(new[]
                { new WorldRitualCost(ResourceId, new BigDouble(5)) })
            : PublicationTable<WorldRitualCost>.Empty;
        var completion = selected
            ? PublicationTable<WorldRitualCost>.Create(new[]
                { new WorldRitualCost(ResourceId, new BigDouble(2)) })
            : PublicationTable<WorldRitualCost>.Empty;
        var decision = new WorldRitualDecision(selected, 8, true, true,
            activation, completion);
        var modifiers = default(RawRitualModifiers);

        // GetRequiredWaves() scales the ritual's own base by the staged level and clamps to the
        // maximum, so the fixture derives it too rather than publishing a wave total that the
        // bounds beside it contradict.
        const int baseWaves = 1;
        const int maxWaves = 20;
        const int wavesPerLevel = 3;
        var requiredWaves = Math.Min(baseWaves + (wavesPerLevel * level), maxWaves);
        var ritual = new WorldRitual(RitualId, true, inBattle, activeInstances,
            6, 5, level, wavesCompleted, 0, 0, 0, 0, 1, BigDouble.Zero, in modifiers,
            false, false, false, 0, baseWaves, maxWaves, requiredWaves, 1d, 0, failedRun, spoils,
            decision: decision);
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var resourceModifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(ResourceId, new BigDouble(80), new BigDouble(100),
            true, BigDouble.Zero, BigDouble.Zero, new BigDouble(100),
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false, false,
            false, 0, Guid.Empty, in rateInputs, in traits, in resourceModifiers);
        var resource = new WorldResource(in reading, true, new BigDouble(20), 0.8, false,
            new BigDouble(80), BigDouble.Zero);
        var identities = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(RitualId, "RitualSO", "Moon Rite", "moon_rite"),
            new EntityIdentityName(ResourceId, "ResourceSO", "Knowledge", "knowledge"),
        }).OrderBy(row => row.EntityId).ToArray();
        return new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(9, identities),
            Rituals = PublicationTable<WorldRitual>.Create(new[] { ritual }),
            Resources = PublicationTable<WorldResource>.Create(new[] { resource }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus("rituals", WorldCategoryOutcome.Collected,
                    1, 0, string.Empty),
                new WorldCollectionCategoryStatus("resources", WorldCategoryOutcome.Collected,
                    1, 0, string.Empty),
            }),
        };
    }

    private static JObject Json(GameMcpValue value, GameWorldState world) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, world.EntityIdentities));
}
