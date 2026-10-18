using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.EquipmentLoadout;

[Collection(GameActionFaultLogCollection.Name)]
public sealed class EquipmentLoadoutGameActionTests : IDisposable
{
    private const long Epoch = 83;
    private readonly IDictionary _registry = new Hashtable();

    public EquipmentLoadoutGameActionTests()
    {
        EquipmentManager.instance = new EquipmentManager();
        EquipmentManager.instance.equippedEquipment.Maximum = 3;
        GlobalVariables.MultiBuy = new IntVariable { Value = 2 };
    }

    public void Dispose()
    {
        EquipmentManager.instance = new EquipmentManager();
        GlobalVariables.MultiBuy = new IntVariable { Value = 1 };
        GameActionFaultLog.ConfigureLog(null);
        NativeMultiBuyScope.ResetQuarantineForTests();
    }

    [Fact]
    public void Equip_uses_the_explicit_amount_and_restores_the_screen_strip()
    {
        var target = Equipment();
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, EquipmentLoadoutActionKind.Equip);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(2, EquipmentManager.instance.equippedEquipment.GetStacks(target));
        Assert.Equal(2, GlobalVariables.MultiBuy.Value);
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 1), result.Outcome.CallOutcome);
    }

    [Fact]
    public void Unequip_uses_the_same_native_multi_buy_and_returns_the_remaining_stack()
    {
        var target = Equipment();
        EquipmentManager.instance.equippedEquipment.Stack(target, 3);
        target.Equip(3);
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, EquipmentLoadoutActionKind.Unequip);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(1, EquipmentManager.instance.equippedEquipment.GetStacks(target));
    }

    [Fact]
    public void Ui_type_slot_and_global_slot_rules_refuse_before_the_native_manager()
    {
        var type = EquipmentType(1);
        var occupied = Equipment(type);
        EquipmentManager.instance.equippedEquipment.Stack(occupied, 1);
        occupied.Equip(1);
        var target = Equipment(type);
        Register(target);
        using var boundary = Boundary();

        var typeFull = Submit(boundary, target, EquipmentLoadoutActionKind.Equip);
        type.maxTypeSlots = new ValueModifierRecord(new BigDouble(2));
        EquipmentManager.instance.equippedEquipment.Maximum = 1;
        var globalFull = Submit(boundary, target, EquipmentLoadoutActionKind.Equip);

        Assert.Equal(EquipmentLoadoutActionResultCodes.EquipmentTypeFull, typeFull.Outcome.Code);
        Assert.Equal(EquipmentLoadoutActionResultCodes.LoadoutFull, globalFull.Outcome.Code);
        Assert.Equal(0, EquipmentManager.instance.EquipCalls);
    }

    [Fact]
    public void Native_usage_affordability_and_creation_gate_before_the_mutation_permit()
    {
        var target = Equipment();
        target.isCreated = false;
        Register(target);
        var permits = 0;
        using var boundary = Boundary(permit: () => { permits++; return true; });

        var notCreated = Submit(boundary, target, EquipmentLoadoutActionKind.Equip);
        target.isCreated = true;
        target.usageCost.affordable = false;
        var unaffordable = Submit(boundary, target, EquipmentLoadoutActionKind.Equip);

        Assert.Equal(EquipmentLoadoutActionResultCodes.NotCreated, notCreated.Outcome.Code);
        Assert.Equal(EquipmentLoadoutActionResultCodes.UsageUnaffordable, unaffordable.Outcome.Code);
        Assert.Equal(0, permits);
        Assert.Equal(0, EquipmentManager.instance.EquipCalls);
    }

    [Fact]
    public void Amounts_beyond_live_capacity_refuse_without_clamping_or_native_calls()
    {
        var target = Equipment();
        Register(target);
        using var boundary = Boundary();

        var equip = Submit(boundary, target, EquipmentLoadoutActionKind.Equip, amount: 5);
        EquipmentManager.instance.equippedEquipment.Stack(target, 2);
        target.Equip(2);
        var unequip = Submit(boundary, target, EquipmentLoadoutActionKind.Unequip, amount: 3);

        Assert.Equal(EquipmentLoadoutActionResultCodes.AmountUnavailable, equip.Outcome.Code);
        Assert.Contains("at most 4", equip.Outcome.Reason, StringComparison.Ordinal);
        Assert.Equal(EquipmentLoadoutActionResultCodes.AmountUnavailable, unequip.Outcome.Code);
        Assert.Contains("only 2", unequip.Outcome.Reason, StringComparison.Ordinal);
        Assert.Equal(0, EquipmentManager.instance.EquipCalls);
        Assert.Equal(0, EquipmentManager.instance.UnEquipCalls);
    }

    [Fact]
    public void Missing_outcome_revalidates_on_the_next_call()
    {
        var target = Equipment();
        Register(target);
        EquipmentManager.instance.SuppressMutation = true;
        using var boundary = Boundary();

        var failed = Submit(boundary, target, EquipmentLoadoutActionKind.Equip);
        var secondFailure = Submit(boundary, target, EquipmentLoadoutActionKind.Equip);
        EquipmentManager.instance.SuppressMutation = false;
        boundary.InvalidateLifecycle();
        var retry = Submit(boundary, target, EquipmentLoadoutActionKind.Equip);

        Assert.Equal(EquipmentLoadoutActionResultCodes.VerificationFailed, failed.Outcome.Code);
        Assert.Equal(EquipmentLoadoutActionResultCodes.VerificationFailed, secondFailure.Outcome.Code);
        Assert.True(retry.Outcome.Verified, retry.Outcome.Reason);
    }

    [Fact]
    public void Exception_after_the_requested_stack_transition_commits()
    {
        var target = Equipment();
        Register(target);
        EquipmentManager.instance.ThrowAfterMutation = true;
        using var boundary = Boundary();

        var result = Submit(boundary, target, EquipmentLoadoutActionKind.Equip);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(2, EquipmentManager.instance.equippedEquipment.GetStacks(target));
    }

    [Fact]
    public void Exception_after_the_exact_stack_outcome_commits()
    {
        var target = Equipment();
        EquipmentManager.instance.equippedEquipment.Stack(target, 2);
        target.Equip(2);
        Register(target);
        EquipmentManager.instance.ThrowAfterMutationWithoutReadablePostState = true;
        using var boundary = Boundary();

        var result = Submit(boundary, target, EquipmentLoadoutActionKind.Unequip);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(0, EquipmentManager.instance.equippedEquipment.GetStacks(target));
    }

    [Fact]
    public void A_landed_equip_whose_multi_buy_restore_throws_commits_and_quarantines_the_strip()
    {
        var logged = new List<string>();
        GameActionFaultLog.ConfigureLog(logged.Add);
        var target = Equipment();
        Register(target);
        GlobalVariables.MultiBuy.SetFailure = message => new NullReferenceException(message);
        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = 2;
        using var boundary = Boundary();

        var result = Submit(boundary, target, EquipmentLoadoutActionKind.Equip, amount: 1);
        var next = Submit(boundary, target, EquipmentLoadoutActionKind.Equip, amount: 1);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(1, EquipmentManager.instance.equippedEquipment.GetStacks(target));
        var line = Assert.Single(logged);
        Assert.Matches("MCP-[0-9A-F]{8}", line);
        Assert.Contains(typeof(NullReferenceException).FullName!, line);
        Assert.True(NativeMultiBuyScope.IsMutationQuarantined);
        Assert.Equal(EquipmentLoadoutActionResultCodes.MultiBuyUnavailable, next.Outcome.Code);
        Assert.Equal(1, EquipmentManager.instance.EquipCalls);
    }

    [Fact]
    public void An_equip_that_did_not_land_lets_an_unexpected_throw_escape_with_the_strip_restored()
    {
        var target = Equipment();
        Register(target);
        EquipmentManager.instance.ThrowBeforeMutation = new NullReferenceException("injected defect before equip");
        using var boundary = Boundary();

        Assert.Throws<NullReferenceException>(() =>
            Submit(boundary, target, EquipmentLoadoutActionKind.Equip, amount: 1));

        Assert.Equal(0, EquipmentManager.instance.equippedEquipment.GetStacks(target));
        Assert.Equal(2, GlobalVariables.MultiBuy.Value);
        Assert.False(NativeMultiBuyScope.IsMutationQuarantined);
    }

    [Fact]
    public void An_unreadable_lifecycle_epoch_faults_before_the_native_call()
    {
        var target = Equipment();
        Register(target);
        using var boundary = Boundary(epoch: () => throw new InvalidOperationException("lifecycle unavailable"));

        var result = Submit(boundary, target, EquipmentLoadoutActionKind.Equip);

        Assert.True(result.Outcome.IsFault);
        Assert.Equal(EquipmentLoadoutActionResultCodes.ContractUnavailable, result.Outcome.Code);
        Assert.Equal(0, result.Outcome.CallOutcome.MutationAttempts);
        Assert.Equal(0, EquipmentManager.instance.EquipCalls);
    }

    [Fact]
    public void Unity_thread_is_revalidated_before_identity_or_native_state()
    {
        var target = Equipment();
        Register(target);
        using var boundary = Boundary();

        var result = ForeignThread.Run(() => Submit(boundary, target, EquipmentLoadoutActionKind.Equip));

        Assert.Equal(EquipmentLoadoutActionResultCodes.WrongThread, result.Outcome.Code);
        Assert.Equal(0, EquipmentManager.instance.EquipCalls);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_lifecycle_binding_set()
    {
        foreach (var missing in EquipmentLoadoutNativeBindings.ContractIds)
        {
            using var boundary = Boundary(includeContract: id => id != missing);
            Assert.False(boundary.BindingsAvailable);
            Assert.Contains(missing, boundary.BindingFailure, StringComparison.Ordinal);
        }
    }

    private EquipmentLoadoutGameAction Boundary(Func<long>? epoch = null,
        Func<bool>? permit = null, Func<string, bool>? includeContract = null)
    {
        var readEpoch = epoch ?? (() => Epoch);
        var resolver = new TypedRegistryResolver(readEpoch,
            () => TypedRegistrySourceSnapshot.Ready(_registry),
            value => value is IHasGuid item ? item.GetGuid() : Guid.Empty);
        return new EquipmentLoadoutGameAction(readEpoch, permit ?? (() => true),
            static () => "EquipmentLoadout ownership was revoked.",
            includeContract: includeContract, registry: resolver);
    }

    private static EquipmentLoadoutSubmission Submit(EquipmentLoadoutGameAction boundary,
        EquipmentSO target, EquipmentLoadoutActionKind kind, long epoch = Epoch, int amount = 2)
    {
        var action = new EquipmentLoadoutAction(kind, target.GetGuid(), amount, epoch);
        return boundary.Submit(in action);
    }

    private void Register(EquipmentSO target) => _registry.Add(target.GetGuid(), target);

    private static EquipmentSO Equipment(EquipmentTypeSO? type = null)
    {
        var target = new EquipmentSO
        {
            isCreated = true,
            equipmentType = type ?? EquipmentType(3),
            NativeMaximumStacks = 4,
        };
        target.SetGuid(Guid.NewGuid());
        return target;
    }

    private static EquipmentTypeSO EquipmentType(int maximumSlots)
    {
        var type = new EquipmentTypeSO
        {
            maxTypeSlots = new ValueModifierRecord(new BigDouble(maximumSlots)),
        };
        type.SetGuid(Guid.NewGuid());
        return type;
    }
}
