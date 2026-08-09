using System;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// One scene, one answer. Round 6 read the Start menu three ways after Back to Menu:
/// <c>suite_health</c> reported a live runtime and world generation 72814, <c>world_overview</c>
/// served the destroyed run's economy as <c>available</c>, and <c>game_probe</c> alone said
/// <c>NoGame</c>. These pin the three onto the one lifecycle fact.
/// </summary>
public sealed class GameMcpLifecycleBoundaryTests
{
    private static WorldPublication<GameWorldState> LiveWorld(ulong generation = 72814)
    {
        var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(
            GameWorldStateDefaults.Empty with
            {
                CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
                EntityIdentities = GameMcpTestHarness.EntityCatalog,
            },
            new WorldGeneration(generation));
        return publisher.ReadLatest();
    }

    /// <summary>
    /// The flushed publication is the one the boundary leaves behind. Its shape is exactly the
    /// pre-run seed, so nothing downstream needs a second "was this a real reading" rule.
    /// </summary>
    private static WorldPublication<GameWorldState> FlushedWorld()
    {
        var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(
            GameWorldStateDefaults.Empty with { CollectedAtUtcTicks = DateTime.UtcNow.Ticks },
            new WorldGeneration(72814));
        publisher.Flush(GameWorldStateDefaults.Empty);
        return publisher.ReadLatest();
    }

    /// <summary>
    /// The class says which kind of no this is. Naming the state is the sentence's job, and the
    /// four sentences stay distinct, so one generic class never makes four situations read alike.
    /// </summary>
    [Theory]
    [InlineData(GameLifecycleState.NoGame, "no save is loaded")]
    [InlineData(GameLifecycleState.Initializing, "the save is still loading")]
    [InlineData(GameLifecycleState.Resetting, "is replacing the run")]
    [InlineData(GameLifecycleState.SceneExit, "the play scene is unloading")]
    public void AWorldReadOnADeadLifecycleRefusesAndNamesTheState(
        GameLifecycleState state,
        string expectedSentence)
    {
        var context = GameMcpTestHarness.Context(
            FlushedWorld(),
            lifecycleState: state,
            sceneName: "Start");

        var overview = GameMcpTestHarness.Json(GameMcpWorldQuery.Overview(context));

        Assert.Equal("unavailable", (string?)overview["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)overview["reasonCode"]);
        Assert.Equal(state.ToString(), (string?)overview["lifecycleState"]);
        Assert.Contains(expectedSentence, (string?)overview["reason"]);
        Assert.Null(overview["economy"]);
    }

    [Fact]
    public void EveryWorldReaderRefusesTheSameWayOnTheSameDeadLifecycle()
    {
        var context = GameMcpTestHarness.Context(
            FlushedWorld(),
            lifecycleState: GameLifecycleState.NoGame,
            sceneName: "Start");

        var readers = new[]
        {
            GameMcpTestHarness.Json(GameMcpWorldQuery.Overview(context)),
            GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(context, "structures", 0, 10)),
            GameMcpTestHarness.Json(GameMcpWorldQuery.Search(context, "orb", 0, 10)),
            GameMcpTestHarness.Json(GameMcpWorldQuery.GetRows(
                context, "structures", new[] { Guid.NewGuid().ToString("D") })),
        };

        foreach (var reader in readers)
        {
            Assert.Equal("unavailable", (string?)reader["status"]);
            Assert.Equal("ERR_UNAVAILABLE", (string?)reader["reasonCode"]);
            Assert.Equal("NoGame", (string?)reader["lifecycleState"]);
        }
    }

    /// <summary>
    /// The pre-run and post-run Start-menu health readings agree on the two facts that describe the
    /// run: there is no game, and no world is published.
    /// </summary>
    [Fact]
    public void HealthReadsTheSameOnTheStartMenuBeforeAndAfterARun()
    {
        var beforeAnyRun = OrbModding.Plugin.ProjectGameMcpHealthText(
            GameMcpTestHarness.Context(
                world: null,
                lifecycleGeneration: 11,
                lifecycleState: GameLifecycleState.NoGame,
                sceneName: "Start"));
        var afterTheRun = OrbModding.Plugin.ProjectGameMcpHealthText(
            GameMcpTestHarness.Context(
                FlushedWorld(),
                lifecycleGeneration: 11,
                lifecycleState: GameLifecycleState.NoGame,
                sceneName: "Start"));

        Assert.Contains("lifecycle: NoGame, generation 11", beforeAnyRun, StringComparison.Ordinal);
        Assert.Contains("world: not published", beforeAnyRun, StringComparison.Ordinal);
        Assert.Equal(beforeAnyRun, afterTheRun);
    }

    [Fact]
    public void HealthPublishesTheLiveWorldGenerationWhileTheRunIsPlaying()
    {
        var text = OrbModding.Plugin.ProjectGameMcpHealthText(
            GameMcpTestHarness.Context(LiveWorld(), lifecycleGeneration: 9));

        Assert.Contains("lifecycle: Playing, generation 9", text, StringComparison.Ordinal);
        Assert.Contains("world: generation 72814", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A world that is genuinely still being collected while the run plays keeps its own code: the
    /// lifecycle codes describe a missing game, not a slow collector.
    /// </summary>
    [Fact]
    public void APlayingLifecycleWithNoCollectionYetStillReadsAsNotPublished()
    {
        var overview = GameMcpTestHarness.Json(
            GameMcpWorldQuery.Overview(GameMcpTestHarness.Context(FlushedWorld())));

        Assert.Equal("ERR_UNAVAILABLE", (string?)overview["reasonCode"]);
        Assert.Equal("Playing", (string?)overview["lifecycleState"]);
    }
}
