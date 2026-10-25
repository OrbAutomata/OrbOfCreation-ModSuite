using OrbModding.Common.Runtime.World;
using System;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Services.TestSupport;
using Xunit;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Tests.Services.SpellComposition;

[Collection(GameActionFaultLogCollection.Name)]
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

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(1, Player.Current.druidryOutputLevel.AsInt());
    }

    [Fact]
    public void An_unreadable_lifecycle_epoch_faults_before_the_native_call()
    {
        AdjusterView(KnownEntities.WorldAgromancyDruidryLvs.Uuid, unlocked: true);
        using var action = Action(readEpoch: () => throw new InvalidOperationException("lifecycle unavailable"));

        var result = action.Submit(new SpellCompositionAction(LevelDial.Druidry, 1, Epoch));

        Assert.True(result.Outcome.IsFault);
        Assert.Equal(SpellCompositionActionResultCodes.ContractUnavailable, result.Outcome.Code);
        Assert.Equal(0, result.Outcome.CallOutcome.MutationAttempts);
        Assert.Equal(3, Player.Current.druidryOutputLevel.AsInt());
    }

    [Fact]
    public void AlchemyLevelTurnsDownThroughTheSameDialBoundary()
    {
        AdjusterView(KnownEntities.AlchAlchemyLevel.Uuid, unlocked: true);
        using var action = Action();

        var result = action.Submit(new SpellCompositionAction(LevelDial.Alchemy, 2, Epoch));

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
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

        Assert.Equal(SpellCompositionActionResultCodes.ControlLocked, result.Outcome.Code);
        Assert.Contains(unlock, result.Outcome.Reason);
        Assert.Equal(0, Player.Current.druidryOutputLevel.SetCalls);
        Assert.Equal(0, Player.Current.alchemyOutputLevel.SetCalls);
    }

    [Fact]
    public void DruidryLevelAboveItsCapRefusesWithTheLiveRange()
    {
        AdjusterView(KnownEntities.WorldAgromancyDruidryLvs.Uuid, unlocked: true);
        using var action = Action();

        var result = action.Submit(new SpellCompositionAction(LevelDial.Druidry, 4, Epoch));

        Assert.Equal(SpellCompositionActionResultCodes.LevelOutOfRange, result.Outcome.Code);
        Assert.Equal(1, result.Minimum);
        Assert.Equal(3, result.Maximum);
        Assert.Equal(0, Player.Current.druidryOutputLevel.SetCalls);
    }

    [Fact]
    public void OutputLevelCommitsTheExactGlobalSelectorOutcome()
    {
        using var action = Action();

        var result = action.Submit(Output(7));

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(7, Player.GetSpellOutputLevel().AsInt());
    }

    [Fact]
    public void ReserveLevelCommitsThroughTheSameGlobalDialBoundary()
    {
        using var action = Action();

        var result = action.Submit(Reserve(8));

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(8, Player.GetReserveLevel().AsInt());
    }

    [Fact]
    public void OutputSetterThrowAfterOutcomeStillCommitsWithoutQuarantine()
    {
        Player.GetSpellOutputLevel().ThrowAfterWriteFor = 5;
        using var action = Action();

        var result = action.Submit(Output(5));

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(5, Player.GetSpellOutputLevel().AsInt());
    }

    /// <summary>
    /// A dial's fault sends the caller to the screen that dial is drawn on, not to the casting
    /// screen the other two dials share.
    /// </summary>
    [Fact]
    public void ADialThatErrorsInTheGameNamesTheScreenItIsDrawnOn()
    {
        AdjusterView(KnownEntities.WorldAgromancyDruidryLvs.Uuid, unlocked: true);
        Player.Current.druidryOutputLevel.ThrowBeforeWriteFor = 1;
        using var action = Action();

        var result = action.Submit(new SpellCompositionAction(LevelDial.Druidry, 1, Epoch));

        Assert.Equal(SpellCompositionActionResultCodes.PostCommitFault, result.Outcome.Code);
        Assert.Contains("Check World > Agromancy before retrying.", result.Outcome.Reason, StringComparison.Ordinal);
        Assert.Equal(3, Player.Current.druidryOutputLevel.AsInt());
    }

    [Fact]
    public void UnexpectedExceptionFromANativeReadEscapesInsteadOfReadingAsUnavailable()
    {
        Player.GetSpellOutputLevel().ReadFailure = new MemberAccessException("injected dial read failure");
        using var action = Action();

        Assert.Throws<MemberAccessException>(() => action.Submit(Output(4)));
        Assert.Equal(0, Player.GetSpellOutputLevel().SetCalls);
    }

    [Fact]
    public void OutputLevelRangeAndNoOpRefuseBeforeMutation()
    {
        using var action = Action();

        var range = action.Submit(Output(13));
        var noOp = action.Submit(Output(2));

        Assert.Equal(SpellCompositionActionResultCodes.LevelOutOfRange, range.Outcome.Code);
        Assert.Equal(SpellCompositionActionResultCodes.AlreadyInRequestedState, noOp.Outcome.Code);
        Assert.Equal(0, Player.GetSpellOutputLevel().SetCalls);
    }

    [Fact]
    public void OffThreadSubmissionRefusesBeforeNativeExecution()
    {
        using var action = Action();

        var result = ForeignThread.Run(() => action.Submit(Output(4)));

        Assert.Equal(SpellCompositionActionResultCodes.WrongThread, result.Outcome.Code);
        Assert.Equal(2, Player.GetSpellOutputLevel().AsInt());
    }

    [Fact]
    public void EveryMissingLifecycleBindingFailsClosed()
    {
        Assert.Equal(16, SpellCompositionNativeBindings.ContractIds.Length);
        Assert.DoesNotContain(
            SpellCompositionNativeBindings.ContractIds,
            id => id.Contains("augment", StringComparison.OrdinalIgnoreCase));

        foreach (var missing in SpellCompositionNativeBindings.ContractIds)
        {
            using var action = Action(include: id => id != missing);
            var result = action.Submit(Output(4));
            Assert.Equal(SpellCompositionActionResultCodes.ContractUnavailable, result.Outcome.Code);
        }
    }

    [Fact]
    public void StaleLifecycleAndMissingPermitRefuseWithoutMutation()
    {
        using var stale = Action(epoch: Epoch + 1);
        using var unowned = Action(permit: false);

        Assert.Equal(
            CommonActionResultCodes.LifecycleReplaced,
            stale.Submit(Output(4)).Outcome.Code);
        Assert.Equal(
            SpellCompositionActionResultCodes.MutationPermitUnavailable,
            unowned.Submit(Output(4)).Outcome.Code);
        Assert.Equal(2, Player.GetSpellOutputLevel().AsInt());
    }

    [Theory]
    [InlineData((int)LevelDial.Output)]
    [InlineData((int)LevelDial.Reserve)]
    [InlineData((int)LevelDial.Druidry)]
    [InlineData((int)LevelDial.Alchemy)]
    public void AutomationRefusesAPlayerChangedDialBeforeConsideringItsRequestedValue(int dialValue)
    {
        var dial = (LevelDial)dialValue;
        var variable = PrepareAutomationDial(dial);
        var captured = variable.AsInt();
        variable.Value = captured - 1;
        using var action = Action();

        var result = action.Submit(new SpellCompositionAction(dial, captured - 1, Epoch,
            captured, AutoLevelsService.Target(dial)));

        Assert.Equal(SpellCompositionActionResultCodes.StateChanged, result.Outcome.Code);
        Assert.Contains("changed since the automation reading", result.Outcome.Reason);
        Assert.Equal(0, result.Outcome.CallOutcome.MutationAttempts);
        Assert.Equal(0, variable.SetCalls);
        Assert.Equal(captured - 1, variable.AsInt());
    }

    [Theory]
    [InlineData((int)LevelDial.Output)]
    [InlineData((int)LevelDial.Reserve)]
    [InlineData((int)LevelDial.Druidry)]
    [InlineData((int)LevelDial.Alchemy)]
    public void AutomationCommitsWhenTheCapturedIdentityAndCurrentLevelStillMatch(int dialValue)
    {
        var dial = (LevelDial)dialValue;
        var variable = PrepareAutomationDial(dial);
        var captured = variable.AsInt();
        using var action = Action();

        var result = action.Submit(new SpellCompositionAction(dial, captured - 1, Epoch,
            captured, AutoLevelsService.Target(dial)));

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(1, variable.SetCalls);
        Assert.Equal(captured - 1, variable.AsInt());
    }

    [Theory]
    [InlineData((int)LevelDial.Output)]
    [InlineData((int)LevelDial.Reserve)]
    [InlineData((int)LevelDial.Druidry)]
    [InlineData((int)LevelDial.Alchemy)]
    public void AutomationRefusesAReplacementVariableEvenWhenItsLevelMatches(int dialValue)
    {
        var dial = (LevelDial)dialValue;
        var variable = PrepareAutomationDial(dial);
        var captured = variable.AsInt();
        variable.uuid = Guid.Parse("3df842ae-d6be-45c1-a281-d2bfd5d77691");
        using var action = Action();

        var result = action.Submit(new SpellCompositionAction(dial, captured - 1, Epoch,
            captured, AutoLevelsService.Target(dial)));

        Assert.Equal(SpellCompositionActionResultCodes.StateChanged, result.Outcome.Code);
        Assert.Contains("The game replaced this", result.Outcome.Reason);
        Assert.Equal(0, result.Outcome.CallOutcome.MutationAttempts);
        Assert.Equal(0, variable.SetCalls);
        Assert.Equal(captured, variable.AsInt());
    }

    [Fact]
    public void AnUnreadableAutomationIdentityRefusesBeforeTheNativeSetter()
    {
        var variable = PrepareAutomationDial(LevelDial.Output);
        variable.GetGuidFailure = new InvalidOperationException("dial identity unreadable");
        var faults = new System.Collections.Generic.List<string>();
        GameActionFaultLog.ConfigureLog(faults.Add);
        using var action = Action();

        var result = action.Submit(new SpellCompositionAction(LevelDial.Output, 1, Epoch,
            variable.AsInt(), AutoLevelsService.Target(LevelDial.Output)));

        Assert.Equal(SpellCompositionActionResultCodes.ContractUnavailable, result.Outcome.Code);
        Assert.Contains("Nothing was sent to the game", result.Outcome.Reason);
        Assert.Contains(faults, detail => detail.Contains("dial identity unreadable", StringComparison.Ordinal));
        Assert.Equal(0, result.Outcome.CallOutcome.MutationAttempts);
        Assert.Equal(0, variable.SetCalls);
    }

    [Fact]
    public void PlayerDrivenDialUsesTheLiveControlWithoutAnAutomationSnapshot()
    {
        var variable = Player.GetSpellOutputLevel();
        variable.Value = 3;
        variable.GetGuidFailure = new InvalidOperationException("No automation identity was requested.");
        using var action = Action();

        var result = action.Submit(Output(4));

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(4, variable.AsInt());
        Assert.Equal(1, variable.SetCalls);
    }

    private static IntVariable PrepareAutomationDial(LevelDial dial)
    {
        AdjusterView(KnownEntities.WorldAgromancyDruidryLvs.Uuid, unlocked: true);
        AdjusterView(KnownEntities.AlchAlchemyLevel.Uuid, unlocked: true);
        var variable = dial switch
        {
            LevelDial.Output => Player.GetSpellOutputLevel(),
            LevelDial.Reserve => Player.GetReserveLevel(),
            LevelDial.Druidry => Player.Current.druidryOutputLevel,
            LevelDial.Alchemy => Player.Current.alchemyOutputLevel,
            _ => throw new ArgumentOutOfRangeException(nameof(dial)),
        };
        variable.uuid = AutoLevelsService.Target(dial);
        return variable;
    }

    private static SpellCompositionAction Output(int level) =>
        new(LevelDial.Output, level, Epoch);

    private static SpellCompositionAction Reserve(int level) =>
        new(LevelDial.Reserve, level, Epoch);

    private static SpellCompositionGameAction Action(
        long epoch = Epoch,
        bool permit = true,
        Func<string, bool>? include = null,
        Func<long>? readEpoch = null)
    {
        var action = new SpellCompositionGameAction(
            readEpoch ?? (() => epoch),
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
        Player.Current = new Player();
        GameActionFaultLog.ConfigureLog(null);
    }
}
