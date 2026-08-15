using System;
using System.Linq;
using OrbAutomata;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.SpellComposition;

public sealed class SpellCompositionGameActionTests : IDisposable
{
    private const long Epoch = 73;

    public SpellCompositionGameActionTests()
    {
        Player.Current = new Player();
        Player.GetSpellOutputLevel().Value = 2;
        Player.Current.maxSpellOutputLevel.Value = 12;
        Player.GetReserveLevel().Value = 3;
        Player.Current.maxReserveLevel.Value = 9;
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
        Assert.Equal(7, SpellCompositionNativeBindings.ContractIds.Length);
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
        new(CastingDial.Output, level, Epoch);

    private static SpellCompositionAction Reserve(int level) =>
        new(CastingDial.Reserve, level, Epoch);

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

    public void Dispose() => Player.Current = new Player();
}
