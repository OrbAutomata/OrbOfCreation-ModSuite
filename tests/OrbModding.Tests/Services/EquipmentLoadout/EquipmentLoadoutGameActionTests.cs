using System;
using System.Collections;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.EquipmentLoadout;

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
    }

    [Fact]
    public void Equip_uses_the_explicit_amount_and_restores_the_screen_strip()
    {
        var target = Equipment();
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, EquipmentLoadoutActionKind.Equip);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(2, EquipmentManager.instance.equippedEquipment.GetStacks(target));
        Assert.Equal(2, GlobalVariables.MultiBuy.Value);
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 1), result.CallOutcome);
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

        Assert.True(result.Verified, result.Reason);
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

        Assert.Equal(EquipmentLoadoutPreflight.EquipmentTypeFull, typeFull.Preflight);
        Assert.Equal(EquipmentLoadoutPreflight.LoadoutFull, globalFull.Preflight);
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

        Assert.Equal(EquipmentLoadoutPreflight.NotCreated, notCreated.Preflight);
        Assert.Equal(EquipmentLoadoutPreflight.UsageUnaffordable, unaffordable.Preflight);
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

        Assert.Equal(EquipmentLoadoutPreflight.AmountUnavailable, equip.Preflight);
        Assert.Contains("at most 4", equip.Reason, StringComparison.Ordinal);
        Assert.Equal(EquipmentLoadoutPreflight.AmountUnavailable, unequip.Preflight);
        Assert.Contains("only 2", unequip.Reason, StringComparison.Ordinal);
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

        Assert.Equal(EquipmentLoadoutPreflight.VerificationFailed, failed.Preflight);
        Assert.Equal(EquipmentLoadoutPreflight.VerificationFailed, secondFailure.Preflight);
        Assert.True(retry.Verified, retry.Reason);
    }

    [Fact]
    public void Exception_after_the_requested_stack_transition_commits()
    {
        var target = Equipment();
        Register(target);
        EquipmentManager.instance.ThrowAfterMutation = true;
        using var boundary = Boundary();

        var result = Submit(boundary, target, EquipmentLoadoutActionKind.Equip);

        Assert.True(result.Verified, result.Reason);
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

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(0, EquipmentManager.instance.equippedEquipment.GetStacks(target));
    }

    [Fact]
    public void Unity_thread_is_revalidated_before_identity_or_native_state()
    {
        var target = Equipment();
        Register(target);
        using var boundary = Boundary();

        var result = ForeignThread.Run(() => Submit(boundary, target, EquipmentLoadoutActionKind.Equip));

        Assert.Equal(EquipmentLoadoutPreflight.WrongThread, result.Preflight);
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
            value => value is IHasGuid item ? item.GetGuid() : null);
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
