using System;
using System.Linq;
using OrbAutomata;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.SpellLoadout;

public sealed class SpellLoadoutGameActionTests : IDisposable
{
    private const long Epoch = 91;

    public SpellLoadoutGameActionTests() => SpellManager.instance = new SpellManager();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RemoveCommitsExactAbsenceForHoleOrShift(bool preserveSlot)
    {
        var first = Spell("First");
        var target = Spell("Target");
        var last = Spell("Last");
        SpellManager.instance!.activeSpells.PreserveSlotsOnRemove = preserveSlot;
        SpellManager.instance.activeSpells.value.AddRange(new[] { first, target, last });
        using var action = Action();

        var result = action.Submit(Remove(target));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(new[] { first, last }, SpellManager.instance.activeSpells.value.Where(x => !x.IsEmpty()));
    }

    [Fact]
    public void NativeCanRemoveRefusalStopsBeforePermitAndMutation()
    {
        var spell = Spell("Casting");
        spell.NativeCasting = true;
        SpellManager.instance!.activeSpells.value.Add(spell);
        var permitCalls = 0;
        using var action = Action(permit: () => { permitCalls++; return true; });

        var result = action.Submit(Remove(spell));

        Assert.Equal(SpellLoadoutPreflight.NativeRemoveRefused, result.Preflight);
        Assert.Equal(0, permitCalls);
        Assert.Equal(0, SpellManager.instance.RemoveCalls);
        Assert.Contains(spell, SpellManager.instance.activeSpells.value);
    }

    [Fact]
    public void RemoveThrowAfterExactOutcomeStillCommits()
    {
        var spell = Spell("Transient");
        SpellManager.instance!.activeSpells.value.Add(spell);
        SpellManager.instance.ThrowAfterRemoval = true;
        using var action = Action();

        var result = action.Submit(Remove(spell));

        Assert.True(result.Verified, result.Reason);
        Assert.DoesNotContain(spell, SpellManager.instance.activeSpells.value);
    }

    [Fact]
    public void MissingRemoveOutcomeFaultsEachAttemptWithoutPersistentActionState()
    {
        var spell = Spell("Stuck");
        SpellManager.instance!.activeSpells.value.Add(spell);
        SpellManager.instance.SuppressRemoval = true;
        using var action = Action();

        var failed = action.Submit(Remove(spell));
        var retry = action.Submit(Remove(spell));

        Assert.Equal(SpellLoadoutPreflight.VerificationFailed, failed.Preflight);
        Assert.Equal(SpellLoadoutPreflight.VerificationFailed, retry.Preflight);
        Assert.Contains(spell, SpellManager.instance.activeSpells.value);
    }

    [Fact]
    public void MoveCommitsOneExactSwapAndNotifiesTheNativeList()
    {
        var first = Spell("First");
        var middle = Spell("Middle");
        var last = Spell("Last");
        var list = SpellManager.instance!.activeSpells;
        list.value.AddRange(new[] { first, middle, last });
        using var action = Action();

        var result = action.Submit(Move(first, 2));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(new[] { last, middle, first }, list.value);
        Assert.Equal(1, list.SwapCalls);
        Assert.Equal(1, list.UpdateObservableCalls);
    }

    [Fact]
    public void MoveIntoAnEmptyNativeSlotKeepsTheExactSlotSequence()
    {
        var spell = Spell("Mover");
        var empty = new Spell { NativeEmpty = true, guidContainer = new GuidContainer(Guid.Empty) };
        var list = SpellManager.instance!.activeSpells;
        list.value.AddRange(new[] { spell, empty });
        using var action = Action();

        var result = action.Submit(Move(spell, 1));

        Assert.True(result.Verified, result.Reason);
        Assert.True(list.value[0].IsEmpty());
        Assert.Same(spell, list.value[1]);
    }

    [Fact]
    public void SwapThrowAfterExactOutcomeCommitsEvenWhenNotificationDidNotRun()
    {
        var first = Spell("First");
        var second = Spell("Second");
        var list = SpellManager.instance!.activeSpells;
        list.value.AddRange(new[] { first, second });
        list.ThrowAfterSwap = true;
        using var action = Action();

        var result = action.Submit(Move(first, 1));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(new[] { second, first }, list.value);
        Assert.Equal(0, list.UpdateObservableCalls);
    }

    [Fact]
    public void MissingSwapOutcomeFaultsEachAttempt()
    {
        var first = Spell("First");
        var second = Spell("Second");
        var list = SpellManager.instance!.activeSpells;
        list.value.AddRange(new[] { first, second });
        list.SuppressSwap = true;
        using var action = Action();

        var failed = action.Submit(Move(first, 1));
        var retry = action.Submit(Move(first, 1));

        Assert.Equal(SpellLoadoutPreflight.VerificationFailed, failed.Preflight);
        Assert.Equal(SpellLoadoutPreflight.VerificationFailed, retry.Preflight);
        Assert.Equal(new[] { first, second }, list.value);
    }

    [Fact]
    public void IdentityRangeAndNoOpRefuseBeforeNativeMutation()
    {
        var spell = Spell("Only");
        var list = SpellManager.instance!.activeSpells;
        list.value.Add(spell);
        using var action = Action();

        var missing = action.Submit(new SpellLoadoutAction(
            SpellLoadoutActionKind.Remove, Guid.NewGuid(), 0, Epoch));
        var outOfRange = action.Submit(Move(spell, 4));
        var noOp = action.Submit(Move(spell, 0));

        Assert.Equal(SpellLoadoutPreflight.IdentityUnavailable, missing.Preflight);
        Assert.Equal(SpellLoadoutPreflight.DestinationOutOfRange, outOfRange.Preflight);
        Assert.Equal(SpellLoadoutPreflight.AlreadyInRequestedState, noOp.Preflight);
        Assert.Equal(0, SpellManager.instance.RemoveCalls);
        Assert.Equal(0, list.SwapCalls);
    }

    [Fact]
    public void OffThreadSubmissionRefusesBeforeNativeExecution()
    {
        var spell = Spell("Threaded");
        SpellManager.instance!.activeSpells.value.Add(spell);
        using var action = Action();

        var result = ForeignThread.Run(() => action.Submit(Remove(spell)));

        Assert.Equal(SpellLoadoutPreflight.WrongThread, result.Preflight);
        Assert.Equal(0, SpellManager.instance.RemoveCalls);
    }

    [Fact]
    public void EveryMissingLifecycleBindingFailsClosed()
    {
        var spell = Spell("Contract");
        SpellManager.instance!.activeSpells.value.Add(spell);
        foreach (var missing in SpellLoadoutNativeBindings.ContractIds)
        {
            using var action = Action(include: id => id != missing);
            Assert.Equal(
                SpellLoadoutPreflight.ContractUnavailable,
                action.Submit(Remove(spell)).Preflight);
        }
    }

    [Fact]
    public void StaleLifecycleAndMissingPermitRefuseWithoutMutation()
    {
        var spell = Spell("Owned");
        SpellManager.instance!.activeSpells.value.Add(spell);
        using var stale = Action(epoch: Epoch + 1);
        using var unowned = Action(permit: () => false);

        Assert.Equal(SpellLoadoutPreflight.LifecycleReplaced, stale.Submit(Remove(spell)).Preflight);
        Assert.Equal(SpellLoadoutPreflight.MutationPermitUnavailable, unowned.Submit(Remove(spell)).Preflight);
        Assert.Equal(0, SpellManager.instance.RemoveCalls);
    }

    private static Spell Spell(string name) => new(new SpellRecipeSO())
    {
        DisplayName = name,
        NativeChargeAvailable = true,
    };

    private static SpellLoadoutAction Remove(Spell spell) => new(
        SpellLoadoutActionKind.Remove,
        spell.guidContainer.guid,
        0,
        Epoch);

    private static SpellLoadoutAction Move(Spell spell, int destination) => new(
        SpellLoadoutActionKind.Move,
        spell.guidContainer.guid,
        destination,
        Epoch);

    private static SpellLoadoutGameAction Action(
        long epoch = Epoch,
        Func<bool>? permit = null,
        Func<string, bool>? include = null)
    {
        var action = new SpellLoadoutGameAction(
            () => epoch,
            permit ?? (() => true),
            () => "test ownership unavailable",
            name => typeof(SpellManager).Assembly.GetTypes()
                .FirstOrDefault(type => type.Name == name || type.FullName == name),
            include ?? (_ => true));
        if (include is null) Assert.True(action.BindingsAvailable, action.BindingFailure);
        return action;
    }

    public void Dispose() => SpellManager.instance = null;
}
