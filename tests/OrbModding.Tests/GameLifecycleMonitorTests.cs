using System;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class GameLifecycleMonitorTests
{
    [Fact]
    public void TracksReadinessAndRejectsWorkFromOlderGenerations()
    {
        var thread = 7;
        var monitor = new GameLifecycleMonitor(() => thread);
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.SceneEntered, 1, "Main"));
        var initializing = monitor.CaptureLease();

        Assert.Equal(GameLifecycleState.Initializing, monitor.Current.State);
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.RuntimeReady, 2, "Main"));
        Assert.Equal(GameLifecycleState.Playing, monitor.Current.State);
        Assert.False(monitor.IsCurrent(initializing));
        Assert.True(monitor.IsCurrent(monitor.CaptureLease()));
    }

    [Fact]
    public void CoalescesEquivalentSignalsFromMultiplePlugins()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        Assert.True(monitor.TryObserve(
            new GameLifecycleObservation(GameLifecycleTransitionKind.SaveLoaded, 10, "Main", "Automata"),
            out _,
            out _));

        Assert.False(monitor.TryObserve(
            new GameLifecycleObservation(GameLifecycleTransitionKind.SaveLoaded, 10, "Main", "Mentor"),
            out _,
            out var duplicateReason));

        Assert.Contains("already observed", duplicateReason);
        Assert.Equal(1, monitor.Current.Generation);
        Assert.Single(monitor.Diagnostics);
    }

    [Fact]
    public void CoalescesInterleavedSceneSignalsFromMultiplePlugins()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.SceneEntered, 1, "Main"));
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.RuntimeReady, 2, "Main"));

        Assert.True(Observe(monitor, GameLifecycleTransitionKind.SceneExited, 10, "Main"));
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.SceneEntered, 10, "Menu"));
        Assert.False(Observe(monitor, GameLifecycleTransitionKind.SceneExited, 10, "Main"));
        Assert.False(Observe(monitor, GameLifecycleTransitionKind.SceneEntered, 10, "Menu"));
        Assert.False(Observe(monitor, GameLifecycleTransitionKind.SceneExited, 10, "Main"));
        Assert.False(Observe(monitor, GameLifecycleTransitionKind.SceneEntered, 10, "Menu"));

        Assert.Equal(4, monitor.Current.Generation);
        Assert.Equal(GameLifecycleState.NoGame, monitor.Current.State);
    }

    [Fact]
    public void RepeatedInitializationOfTheSameLiveSceneIsIdempotent()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.SceneEntered, 1, "Main"));
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.RuntimeReady, 2, "Main"));

        Assert.False(monitor.TryObserve(
            new GameLifecycleObservation(GameLifecycleTransitionKind.SceneEntered, 10, "Main", "late plugin"),
            out _,
            out var reason));

        Assert.Contains("already observed", reason);
        Assert.Equal(GameLifecycleState.Playing, monitor.Current.State);
        Assert.Equal(2, monitor.Current.Generation);
    }

    [Fact]
    public void CompletedInGameSaveSwitchReturnsToPlayingButFailedLoadDoesNot()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.SceneEntered, 1, "Main"));
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.RuntimeReady, 2, "Main"));
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.SaveLoadStarted, 3, "Main"));
        Assert.Equal(GameLifecycleState.Resetting, monitor.Current.State);
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.SaveLoaded, 4, "Main"));
        Assert.Equal(GameLifecycleState.Playing, monitor.Current.State);

        Assert.True(Observe(monitor, GameLifecycleTransitionKind.SaveLoadStarted, 5, "Main"));
        Assert.Equal(GameLifecycleState.Resetting, monitor.Current.State);
    }

    [Fact]
    public void CoversSaveSwitchResetNewGamePlusAndRapidSceneChanges()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.SceneEntered, 1, "Main"));
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.SaveLoadStarted, 2, "Main"));
        Assert.Equal(GameLifecycleState.Resetting, monitor.Current.State);
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.SaveLoaded, 3, "Main"));
        Assert.Equal(GameLifecycleState.Initializing, monitor.Current.State);
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.RegistryRebuilt, 3, "Main"));
        Assert.Equal(GameLifecycleState.Initializing, monitor.Current.State);
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.RuntimeReady, 3, "Main"));
        Assert.Equal(GameLifecycleState.Playing, monitor.Current.State);
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.ResetStarted, 4, "Main"));
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.ResetCompleted, 5, "Main"));
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.NewGamePlusStarted, 6, "Main"));
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.RuntimeReady, 7, "Main"));
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.SceneExited, 8, "Main"));
        Assert.Equal(GameLifecycleState.SceneExit, monitor.Current.State);
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.SceneEntered, 8, "Menu"));
        Assert.Equal(GameLifecycleState.NoGame, monitor.Current.State);
        Assert.Equal(11, monitor.Current.Generation);
    }

    [Fact]
    public void IsolatesFailingSubscribersAndBoundsDispatchDiagnostics()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        var successfulCalls = 0;
        monitor.Transitioned += _ => throw new InvalidOperationException("simulated subscriber failure");
        monitor.Transitioned += _ => successfulCalls++;

        for (var frame = 0; frame < 40; frame++)
        {
            Assert.True(Observe(
                monitor,
                frame % 2 == 0 ? GameLifecycleTransitionKind.ResetStarted : GameLifecycleTransitionKind.ResetCompleted,
                frame,
                "Main"));
        }

        Assert.Equal(40, successfulCalls);
        Assert.Equal(32, monitor.DispatchFailures.Count);
        Assert.Contains("InvalidOperationException", monitor.DispatchFailures[31].ExceptionType);
    }

    [Fact]
    public void RejectsOffThreadAndInvalidFrameTransitions()
    {
        var thread = 1;
        var monitor = new GameLifecycleMonitor(() => thread);
        Assert.True(Observe(monitor, GameLifecycleTransitionKind.SceneEntered, 1, "Main"));
        thread = 2;

        Assert.False(monitor.TryObserve(
            new GameLifecycleObservation(GameLifecycleTransitionKind.RuntimeReady, 2, "Main", "test"),
            out _,
            out var threadReason));
        Assert.Contains("off the main thread", threadReason);

        thread = 1;
        Assert.False(monitor.TryObserve(
            new GameLifecycleObservation(GameLifecycleTransitionKind.RuntimeReady, -1, "Main", "test"),
            out _,
            out var frameReason));
        Assert.Contains("non-negative", frameReason);
    }

    [Fact]
    public void BoundsStructuredDiagnostics()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        for (var frame = 0; frame < 40; frame++)
        {
            Assert.True(Observe(
                monitor,
                frame % 2 == 0 ? GameLifecycleTransitionKind.ResetStarted : GameLifecycleTransitionKind.ResetCompleted,
                frame,
                "Main"));
        }

        Assert.Equal(32, monitor.Diagnostics.Count);
        Assert.Equal(9, monitor.Diagnostics[0].Generation);
        Assert.Equal(40, monitor.Diagnostics[31].Generation);
    }

    /// <summary>
    /// Every reference the suite holds invalidates on the epoch number alone, and that number is all
    /// the numeric trace can carry, so the reason behind an epoch reaches the log or it reaches
    /// nobody. Naming the prestige behind one mid-session epoch change took a topology line, two
    /// clock anchors, and a file timestamp.
    /// </summary>
    [Fact]
    public void ATransitionSaysWhichEpochItIsAndWhyItHappened()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        Assert.True(monitor.TryObserve(
            new GameLifecycleObservation(GameLifecycleTransitionKind.SceneEntered, 1, "Main", "Automata"),
            out _,
            out _));

        Assert.True(monitor.TryObserve(
            new GameLifecycleObservation(
                GameLifecycleTransitionKind.ResetCompleted,
                48815,
                "Main",
                "Prestige"),
            out var transition,
            out _));

        Assert.Equal(
            "Game lifecycle 1 -> 2: ResetCompleted | state=Playing | scene=Main | source=Prestige " +
            "| frame=48815.",
            transition.Describe());
    }

    [Fact]
    public void AnUnattributedTransitionSaysSoRatherThanLeavingItsFieldsBlank()
    {
        var monitor = new GameLifecycleMonitor(() => 1);

        Assert.True(monitor.TryObserve(
            new GameLifecycleObservation(
                GameLifecycleTransitionKind.RuntimeReady,
                4,
                string.Empty,
                string.Empty),
            out var transition,
            out _));

        Assert.Contains("scene=unnamed", transition.Describe());
        Assert.Contains("source=unnamed", transition.Describe());
    }

    private static bool Observe(
        GameLifecycleMonitor monitor,
        GameLifecycleTransitionKind kind,
        long frame,
        string scene) =>
        monitor.TryObserve(
            new GameLifecycleObservation(kind, frame, scene, "test"),
            out _,
            out _);
}
