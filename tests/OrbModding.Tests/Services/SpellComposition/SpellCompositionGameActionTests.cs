using System;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.SpellComposition;

public sealed class SpellCompositionGameActionTests : IDisposable
{
    private const long Epoch = 73;

    public SpellCompositionGameActionTests()
    {
        IdScriptableObject.RuntimeLookup.Clear();
        Player.Current = new Player();
        Player.GetSpellOutputLevel().Value = 2;
        Player.Current.maxSpellOutputLevel.Value = 12;
        Player.GetReserveLevel().Value = 3;
        Player.Current.maxReserveLevel.Value = 9;
        Player.Current.druidryOutputLevel.Value = 3;
        Player.Current.maxDruidryOutputLevel.Value = 3;
        Player.Current.alchemyOutputLevel.Value = 5;
        Player.Current.maxAlchemyOutputLevel.Value = 5;
    }

    /// <summary>
    /// The view that draws a dial's -/+ adjuster. Its one prerequisite is the dial's unlock
    /// upgrade, so an unbought unlock is a view that is not available.
    /// </summary>
    private static void AdjusterView(Guid viewId, bool unlocked)
    {
        var view = new ViewSO { available = unlocked };
        view.SetGuid(viewId);
        IdScriptableObject.RuntimeLookup[view.GetGuid()] = view;
    }

    /// <summary>
    /// Buying Raise Druidry Lv carries the dial up with its cap, and a player who wants cheaper,
    /// slower chops turns it back down with the -/+ adjuster.
    /// </summary>
    [Fact]
    public void DruidryLevelTurnsDownInsideItsRaisedCap()
    {
        AdjusterView(KnownEntities.WorldAgromancyDruidryLvs.Uuid, unlocked: true);
        using var action = Action();

        var result = action.Submit(new SpellCompositionAction(LevelDial.Druidry, 1, Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(1, Player.Current.druidryOutputLevel.AsInt());
    }

    [Fact]
    public void AlchemyLevelTurnsDownThroughTheSameDialBoundary()
    {
        AdjusterView(KnownEntities.AlchAlchemyLevel.Uuid, unlocked: true);
        using var action = Action();

        var result = action.Submit(new SpellCompositionAction(LevelDial.Alchemy, 2, Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(2, Player.Current.alchemyOutputLevel.AsInt());
    }

    [Theory]
    [InlineData("druidry", "Unlock Druidry Levels")]
    [InlineData("alchemy", "Level Alchemy")]
    public void ALockedAdjusterRefusesWithItsUnlockUpgradeBeforeMutation(string name, string unlock)
    {
        var dial = name == "druidry" ? LevelDial.Druidry : LevelDial.Alchemy;
        AdjusterView(KnownEntities.WorldAgromancyDruidryLvs.Uuid, unlocked: false);
        AdjusterView(KnownEntities.AlchAlchemyLevel.Uuid, unlocked: false);
        using var action = Action();

        var result = action.Submit(new SpellCompositionAction(dial, 1, Epoch));

        Assert.Equal(SpellCompositionPreflight.ControlLocked, result.Preflight);
        Assert.Contains(unlock, result.Reason);
        Assert.Equal(0, Player.Current.druidryOutputLevel.SetCalls);
        Assert.Equal(0, Player.Current.alchemyOutputLevel.SetCalls);
    }

    [Fact]
    public void DruidryLevelAboveItsCapRefusesWithTheLiveRange()
    {
        AdjusterView(KnownEntities.WorldAgromancyDruidryLvs.Uuid, unlocked: true);
        using var action = Action();

        var result = action.Submit(new SpellCompositionAction(LevelDial.Druidry, 4, Epoch));

        Assert.Equal(SpellCompositionPreflight.LevelOutOfRange, result.Preflight);
        Assert.Equal(1, result.Minimum);
        Assert.Equal(3, result.Maximum);
        Assert.Equal(0, Player.Current.druidryOutputLevel.SetCalls);
    }

    [Fact]
    public void OutputLevelCommitsTheExactGlobalSelectorOutcome()
    {
        using var action = Action();

        var result = action.Submit(Output(7));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(7, Player.GetSpellOutputLevel().AsInt());
    }

    [Fact]
    public void ReserveLevelCommitsThroughTheSameGlobalDialBoundary()
    {
        using var action = Action();

        var result = action.Submit(Reserve(8));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(8, Player.GetReserveLevel().AsInt());
    }

    [Fact]
    public void OutputSetterThrowAfterOutcomeStillCommitsWithoutQuarantine()
    {
        Player.GetSpellOutputLevel().ThrowAfterWriteFor = 5;
        using var action = Action();

        var result = action.Submit(Output(5));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(5, Player.GetSpellOutputLevel().AsInt());
    }

    [Fact]
    public void OutputLevelRangeAndNoOpRefuseBeforeMutation()
    {
        using var action = Action();

        var range = action.Submit(Output(13));
        var noOp = action.Submit(Output(2));

        Assert.Equal(SpellCompositionPreflight.LevelOutOfRange, range.Preflight);
        Assert.Equal(SpellCompositionPreflight.AlreadyInRequestedState, noOp.Preflight);
        Assert.Equal(0, Player.GetSpellOutputLevel().SetCalls);
    }

    [Fact]
    public void OffThreadSubmissionRefusesBeforeNativeExecution()
    {
        using var action = Action();

        var result = ForeignThread.Run(() => action.Submit(Output(4)));

        Assert.Equal(SpellCompositionPreflight.WrongThread, result.Preflight);
        Assert.Equal(2, Player.GetSpellOutputLevel().AsInt());
    }

    [Fact]
    public void EveryMissingLifecycleBindingFailsClosed()
    {
        Assert.Equal(12, SpellCompositionNativeBindings.ContractIds.Length);
        Assert.DoesNotContain(
            SpellCompositionNativeBindings.ContractIds,
            id => id.Contains("augment", StringComparison.OrdinalIgnoreCase));

        foreach (var missing in SpellCompositionNativeBindings.ContractIds)
        {
            using var action = Action(include: id => id != missing);
            var result = action.Submit(Output(4));
            Assert.Equal(SpellCompositionPreflight.ContractUnavailable, result.Preflight);
        }
    }

    [Fact]
    public void StaleLifecycleAndMissingPermitRefuseWithoutMutation()
    {
        using var stale = Action(epoch: Epoch + 1);
        using var unowned = Action(permit: false);

        Assert.Equal(
            SpellCompositionPreflight.LifecycleReplaced,
            stale.Submit(Output(4)).Preflight);
        Assert.Equal(
            SpellCompositionPreflight.MutationPermitUnavailable,
            unowned.Submit(Output(4)).Preflight);
        Assert.Equal(2, Player.GetSpellOutputLevel().AsInt());
    }

    private static SpellCompositionAction Output(int level) =>
        new(LevelDial.Output, level, Epoch);

    private static SpellCompositionAction Reserve(int level) =>
        new(LevelDial.Reserve, level, Epoch);

    private static SpellCompositionGameAction Action(
        long epoch = Epoch,
        bool permit = true,
        Func<string, bool>? include = null)
    {
        var action = new SpellCompositionGameAction(
            () => epoch,
            () => permit,
            () => "test ownership unavailable",
            name => typeof(SpellManager).Assembly.GetTypes()
                .FirstOrDefault(type => type.Name == name || type.FullName == name),
            include ?? (_ => true));
        if (include is null) Assert.True(action.BindingsAvailable, action.BindingFailure);
        return action;
    }

    public void Dispose()
    {
        IdScriptableObject.RuntimeLookup.Clear();
        Player.Current = new Player();
    }
}
