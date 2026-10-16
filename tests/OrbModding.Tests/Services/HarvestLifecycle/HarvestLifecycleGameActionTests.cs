using System;
using System.Collections;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.HarvestLifecycle;

public sealed class HarvestLifecycleGameActionTests
{
    private const long Epoch = 127;
    private readonly IDictionary _registry = new Hashtable();
    private readonly HarvestElementListVariable _elements = new();
    private readonly HarvestActionInstanceListVariable _actions = new();

    public HarvestLifecycleGameActionTests()
    {
        _elements.SetGuid(HarvestLifecycleNativeBindings.ActiveElementsId);
        _actions.SetGuid(HarvestLifecycleNativeBindings.ActiveActionsId);
        Register(_elements);
        Register(_actions);
    }

    [Fact]
    public void Element_add_and_remove_use_the_active_list_and_directional_count_sentinel()
    {
        var element = Element();
        Register(element);
        using var boundary = Boundary();

        var added = Submit(boundary, HarvestLifecycleActionKind.AddElement, element, amount: 2);
        var removed = Submit(boundary, HarvestLifecycleActionKind.RemoveElement, element);

        Assert.True(added.Verified, added.Reason);
        Assert.Equal(1, _elements.GetStacks(element));
        Assert.True(removed.Verified, removed.Reason);
    }

    [Fact]
    public void Action_add_and_remove_use_the_element_owned_prototype_pair()
    {
        var element = Element();
        element.masteryLevel = 3;
        var action = Action();
        element.AddAction(action);
        Register(element);
        Register(action);
        using var boundary = Boundary();

        var added = Submit(boundary, HarvestLifecycleActionKind.AddAction,
            element, action, amount: 2);
        var active = Assert.Single(_actions.value);
        var addedInstances = active.instances;
        var removed = Submit(boundary, HarvestLifecycleActionKind.RemoveAction,
            element, action, amount: 2);

        Assert.True(added.Verified, added.Reason);
        Assert.Equal(2, addedInstances);
        Assert.True(removed.Verified, removed.Reason);
        Assert.Empty(_actions.value);
    }

    [Fact]
    public void Action_from_another_element_is_refused_before_the_native_list()
    {
        var first = Element();
        var second = Element();
        var action = Action();
        second.AddAction(action);
        Register(first);
        Register(second);
        Register(action);
        using var boundary = Boundary();

        var result = Submit(boundary, HarvestLifecycleActionKind.AddAction, first, action);

        Assert.Equal(HarvestLifecyclePreflight.ActionUnavailable, result.Preflight);
        Assert.Empty(_actions.value);
    }

    [Fact]
    public void Element_usage_and_action_mastery_limits_are_live_admission_checks()
    {
        var element = Element();
        element.usageCost.affordable = false;
        var action = Action();
        element.AddAction(action);
        Register(element);
        Register(action);
        using var boundary = Boundary();

        var elementResult = Submit(boundary, HarvestLifecycleActionKind.AddElement, element);
        var actionResult = Submit(boundary, HarvestLifecycleActionKind.AddAction,
            element, action, amount: 2);

        Assert.Equal(HarvestLifecyclePreflight.ElementUsageUnavailable,
            elementResult.Preflight);
        Assert.Equal(HarvestLifecyclePreflight.AmountUnavailable,
            actionResult.Preflight);
    }

    [Theory]
    [InlineData((int)HarvestLifecycleActionKind.AddElement)]
    [InlineData((int)HarvestLifecycleActionKind.AddAction)]
    public void Native_no_op_fails_the_one_directional_count_sentinel(
        int kindValue)
    {
        var kind = (HarvestLifecycleActionKind)kindValue;
        var element = Element();
        var action = Action();
        element.AddAction(action);
        Register(element);
        Register(action);
        _elements.SuppressMutation = true;
        _actions.SuppressMutation = true;
        using var boundary = Boundary();

        var result = Submit(boundary, kind, element, action);

        Assert.Equal(HarvestLifecyclePreflight.VerificationFailed, result.Preflight);
    }

    [Fact]
    public void Unity_thread_is_refused_before_registry_or_list_state()
    {
        var element = Element();
        Register(element);
        using var boundary = Boundary();

        var result = ForeignThread.Run(() =>
            Submit(boundary, HarvestLifecycleActionKind.AddElement, element));

        Assert.Equal(HarvestLifecyclePreflight.WrongThread, result.Preflight);
        Assert.Empty(_elements.value);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_binding_set()
    {
        foreach (var missing in HarvestLifecycleNativeBindings.ContractIds)
        {
            using var boundary = Boundary(includeContract: id => id != missing);
            Assert.False(boundary.BindingsAvailable);
            Assert.Contains(missing, boundary.BindingFailure, StringComparison.Ordinal);
        }
    }

    private HarvestLifecycleGameAction Boundary(Func<string, bool>? includeContract = null)
    {
        var resolver = new TypedRegistryResolver(() => Epoch,
            () => TypedRegistrySourceSnapshot.Ready(_registry),
            value => value is IdScriptableObject item ? item.GetGuid() : null);
        return new HarvestLifecycleGameAction(() => Epoch, static () => true,
            static () => "HarvestLifecycle ownership was revoked.",
            includeContract: includeContract, registry: resolver);
    }

    private static HarvestLifecycleSubmission Submit(
        HarvestLifecycleGameAction boundary,
        HarvestLifecycleActionKind kind,
        HarvestElementSO element,
        HarvestActionSO? action = null,
        int amount = 1)
    {
        var request = new HarvestLifecycleAction(kind, element.GetGuid(),
            action?.GetGuid() ?? Guid.Empty, amount, Epoch);
        return boundary.Submit(in request);
    }

    private void Register(IdScriptableObject value) =>
        _registry.Add(value.GetGuid(), value);

    private static HarvestElementSO Element()
    {
        var element = new HarvestElementSO { masteryLevel = 0, MaximumAdditional = 8 };
        element.SetGuid(Guid.NewGuid());
        return element;
    }

    private static HarvestActionSO Action()
    {
        var action = new HarvestActionSO();
        action.SetGuid(Guid.NewGuid());
        return action;
    }
}
