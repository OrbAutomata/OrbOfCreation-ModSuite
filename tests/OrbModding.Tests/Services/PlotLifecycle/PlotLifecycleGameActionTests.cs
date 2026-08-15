using System;
using System.Collections;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.PlotLifecycle;

public sealed class PlotLifecycleGameActionTests
{
    private const long Epoch = 131;
    private readonly IDictionary _registry = new Hashtable();
    private readonly PlotNodeActionInstanceListVariable _actions = new();

    public PlotLifecycleGameActionTests()
    {
        _actions.SetGuid(PlotLifecycleNativeBindings.ActiveActionsId);
        Register(_actions);
    }

    [Fact]
    public void Arbitrary_offered_pair_adds_decrements_and_cancels_at_the_native_minimum()
    {
        var plot = Plot();
        var action = Action();
        plot.AddAction(action);
        Register(plot);
        Register(action);
        using var boundary = Boundary();

        var added = Submit(boundary, PlotLifecycleActionKind.Add, plot, action, amount: 2);
        var active = Assert.Single(_actions.value);
        var decremented = Submit(boundary, PlotLifecycleActionKind.Remove, plot, action);
        var canceled = Submit(boundary, PlotLifecycleActionKind.Remove, plot, action);

        Assert.True(added.Verified, added.Reason);
        Assert.True(decremented.Verified, decremented.Reason);
        Assert.True(canceled.Verified, canceled.Reason);
        Assert.Equal(0, added.BeforeQuantity);
        Assert.Equal(2, added.AfterQuantity);
        Assert.Equal(2, decremented.BeforeQuantity);
        Assert.Equal(1, decremented.AfterQuantity);
        Assert.Equal(1, canceled.BeforeQuantity);
        Assert.Equal(0, canceled.AfterQuantity);
        Assert.Equal(0, active.GetActualQuantity());
        Assert.False(active.IsEngaged());
    }

    [Fact]
    public void Remove_refuses_an_amount_larger_than_the_exact_active_quantity()
    {
        var plot = Plot();
        var action = Action();
        plot.AddAction(action);
        Register(plot);
        Register(action);
        using var boundary = Boundary();

        Assert.True(Submit(boundary, PlotLifecycleActionKind.Add, plot, action, amount: 4).Verified);
        var refused = Submit(boundary, PlotLifecycleActionKind.Remove, plot, action, amount: 10);
        var active = Assert.Single(_actions.value);

        Assert.Equal(PlotLifecyclePreflight.QuantityUnavailable, refused.Preflight);
        Assert.Contains("only 4 active instances", refused.Reason, StringComparison.Ordinal);
        Assert.Equal(4, active.GetActualQuantity());
    }

    [Fact]
    public void Action_not_owned_by_the_plot_is_refused_before_the_active_list()
    {
        var plot = Plot();
        var action = Action();
        Register(plot);
        Register(action);
        using var boundary = Boundary();

        var result = Submit(boundary, PlotLifecycleActionKind.Add, plot, action);

        Assert.Equal(PlotLifecyclePreflight.ActionUnavailable, result.Preflight);
        Assert.Empty(_actions.value);
    }

    [Fact]
    public void Visibility_quantity_and_list_room_are_live_admission_checks()
    {
        var plot = Plot();
        var action = Action();
        var prototype = plot.AddAction(action);
        prototype.EnoughForOneInstance = false;
        Register(plot);
        Register(action);
        using var boundary = Boundary();

        var insufficient = Submit(boundary, PlotLifecycleActionKind.Add, plot, action);
        plot.visible = false;
        var hidden = Submit(boundary, PlotLifecycleActionKind.Add, plot, action);

        Assert.Equal(PlotLifecyclePreflight.QuantityUnavailable, insufficient.Preflight);
        Assert.Equal(PlotLifecyclePreflight.PlotUnavailable, hidden.Preflight);
        Assert.Empty(_actions.value);
    }

    [Fact]
    public void Native_no_op_fails_the_one_quantity_direction_sentinel()
    {
        var plot = Plot();
        var action = Action();
        plot.AddAction(action);
        Register(plot);
        Register(action);
        _actions.SuppressMutation = true;
        using var boundary = Boundary();

        var result = Submit(boundary, PlotLifecycleActionKind.Add, plot, action);

        Assert.Equal(PlotLifecyclePreflight.VerificationFailed, result.Preflight);
    }

    [Fact]
    public void Unity_thread_is_refused_before_registry_or_list_state()
    {
        var plot = Plot();
        var action = Action();
        plot.AddAction(action);
        Register(plot);
        Register(action);
        using var boundary = Boundary();

        var result = ForeignThread.Run(() =>
            Submit(boundary, PlotLifecycleActionKind.Add, plot, action));

        Assert.Equal(PlotLifecyclePreflight.WrongThread, result.Preflight);
        Assert.Empty(_actions.value);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_binding_set()
    {
        foreach (var missing in PlotLifecycleNativeBindings.ContractIds)
        {
            using var boundary = Boundary(includeContract: id => id != missing);
            Assert.False(boundary.BindingsAvailable);
            Assert.Contains(missing, boundary.BindingFailure, StringComparison.Ordinal);
        }
    }

    private PlotLifecycleGameAction Boundary(Func<string, bool>? includeContract = null)
    {
        var resolver = new TypedRegistryResolver(() => Epoch,
            () => TypedRegistrySourceSnapshot.Ready(_registry),
            value => value is IdScriptableObject item ? item.GetGuid() : null);
        return new PlotLifecycleGameAction(() => Epoch, static () => true,
            static () => "HarvestAction ownership was revoked.",
            includeContract: includeContract, registry: resolver);
    }

    private static PlotLifecycleSubmission Submit(
        PlotLifecycleGameAction boundary,
        PlotLifecycleActionKind kind,
        PlotNodeSO plot,
        PlotNodeActionSO action,
        int amount = 1)
    {
        var request = new PlotLifecycleAction(
            kind, plot.GetGuid(), action.GetGuid(), amount, Epoch);
        return boundary.Submit(in request);
    }

    private void Register(IdScriptableObject value) =>
        _registry.Add(value.GetGuid(), value);

    private static PlotNodeSO Plot()
    {
        var plot = new PlotNodeSO { visible = true };
        plot.SetGuid(Guid.NewGuid());
        return plot;
    }

    private static PlotNodeActionSO Action()
    {
        var action = new PlotNodeActionSO();
        action.SetGuid(Guid.NewGuid());
        action.prerequisites.NativeCheckResult = true;
        return action;
    }
}
