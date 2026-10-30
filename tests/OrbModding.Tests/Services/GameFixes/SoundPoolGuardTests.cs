using System;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Services.GameFixes;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GameFixesCollection
{
    public const string Name = "Game fixes static switches";
}

[Collection(GameFixesCollection.Name)]
public sealed class SoundPoolGuardTests : IDisposable
{
    private readonly SoundPoolGuard _guard = new();

    public SoundPoolGuardTests() => _guard.Apply(true);

    public void Dispose() => _guard.Apply(false);

    [Fact]
    public void AnExhaustedPoolHandsOutOneSilentElementClonedFromThePoolPrefabAndCountsIt()
    {
        var manager = Manager();
        var before = _guard.Fired;

        var first = HandOut(manager, null);
        var second = HandOut(manager, null);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.NotSame(manager.audioElement, first);
        Assert.Equal(manager.audioElement.name, first!.name);
        Assert.True(SoundOverflowElement.IsOverflow(first));
        Assert.True(first.SourceMuted);
        Assert.False(manager.audioElement.SourceMuted);
        Assert.Equal(before + 2, _guard.Fired);
        Assert.Contains("2 sounds played silently", _guard.DescribeFired(2));
    }

    [Fact]
    public void ADestroyedOverflowElementOrANewSoundManagerGetsAFreshOne()
    {
        var manager = Manager();
        var first = HandOut(manager, null)!;

        first.destroyed = true;
        var afterSceneChange = HandOut(manager, null);
        var otherManager = HandOut(Manager(), null);

        Assert.NotSame(first, afterSceneChange);
        Assert.NotSame(afterSceneChange, otherManager);
        Assert.True(SoundOverflowElement.IsOverflow(otherManager!));
        Assert.True(afterSceneChange!.SourceMuted);
        Assert.True(otherManager!.SourceMuted);
    }

    [Fact]
    public void AnInactiveGuardLeavesTheGamesNullForTheGame()
    {
        _guard.Apply(false);
        var before = _guard.Fired;

        Assert.Null(HandOut(Manager(), null));
        Assert.Equal(before, _guard.Fired);
    }

    [Fact]
    public void AManagerWithoutAPrefabKeepsTheGamesAnswer()
    {
        var manager = Manager();
        manager.audioElement = null!;

        Assert.Null(HandOut(manager, null));
    }

    [Fact]
    public void APooledElementForgetsItsPreviousOwnersDelayedPlayAndFadeBeforeTheGameReusesIt()
    {
        var pooled = new AudioElement();

        Assert.Same(pooled, HandOut(Manager(), pooled));

        Assert.Equal(1, pooled.StopAllCoroutinesCalls);
        Assert.Equal(1, pooled.CancelFadeCalls);
        Assert.False(SoundOverflowElement.IsOverflow(pooled));
    }

    [Fact]
    public void AnInactiveGuardLeavesAPooledElementAsTheGameHandsItOut()
    {
        _guard.Apply(false);
        var pooled = new AudioElement();

        HandOut(Manager(), pooled);

        Assert.Equal(0, pooled.StopAllCoroutinesCalls);
        Assert.Equal(0, pooled.CancelFadeCalls);
    }

    [Fact]
    public void OnlyTheOverflowElementIsKeptFromPlayingEvenAfterTheGuardIsSwitchedOff()
    {
        var overflow = HandOut(Manager(), null)!;
        var pooled = new AudioElement();
        _guard.Apply(false);
        AudioElement result = null!;

        Assert.False(SoundOverflowElement.StaySilent(overflow, ref result));
        Assert.Same(overflow, result);
        Assert.True(SoundOverflowElement.StaySilent(pooled, ref result));
    }

    [Fact]
    public void ASpellsHoldAndChargeLoopsAreFadedOutBeforeTheGameOverwritesThem()
    {
        var hold = new AudioElement();
        var charge = new AudioElement();
        AudioElement? holdField = hold;
        AudioElement? chargeField = charge;

        SoundLoopRelease.ReleaseHoldLoop(ref holdField);
        SoundLoopRelease.ReleaseChargeLoop(ref chargeField);

        Assert.Null(holdField);
        Assert.Null(chargeField);
        Assert.Equal(0.5f, hold.FadeOutDestroyTime);
        Assert.Equal(0.5f, charge.FadeOutDestroyTime);
    }

    [Fact]
    public void AnInactiveGuardLeavesASpellsLoopToTheGame()
    {
        _guard.Apply(false);
        var hold = new AudioElement();
        AudioElement? field = hold;
        AudioElement? none = null;

        SoundLoopRelease.ReleaseHoldLoop(ref field);
        SoundLoopRelease.ReleaseChargeLoop(ref none);

        Assert.Same(hold, field);
        Assert.Null(hold.FadeOutDestroyTime);
    }

    [Fact]
    public void ADisabledResearchOrBrewingItemReleasesItsLoopThroughTheGamesOwnRelease()
    {
        var research = new UIResearchItem();
        var brewing = new UIBrewingStation();

        SoundLoopRelease.ReleaseItemLoop(research);
        SoundLoopRelease.ReleaseItemLoop(brewing);
        SoundLoopRelease.ReleaseItemLoop(new UIRenderGroupElement());
        _guard.Apply(false);
        SoundLoopRelease.ReleaseItemLoop(research);

        Assert.Equal(1, research.DestroyResearchSoundCalls);
        Assert.Equal(1, brewing.DestroyLoopingAudioCalls);
    }

    private static SoundManager Manager() => new() { audioElement = new AudioElement { name = "Audio Element" } };

    private static AudioElement? HandOut(SoundManager manager, AudioElement? pooled)
    {
        var result = pooled;
        SoundPoolGuard.GuardHandOut(manager, ref result);
        return result;
    }
}
