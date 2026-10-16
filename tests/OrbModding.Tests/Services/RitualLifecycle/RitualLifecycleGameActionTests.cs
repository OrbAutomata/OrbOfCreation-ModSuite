using System;
using System.Collections;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.RitualLifecycle;

public sealed class RitualLifecycleGameActionTests : IDisposable
{
    private const long Epoch = 113;
    private readonly IDictionary _registry = new Hashtable();

    public RitualLifecycleGameActionTests()
    {
        RitualManager.instance = new RitualManager();
        BattleManager.instance = new BattleManager();
    }

    public void Dispose()
    {
        RitualManager.instance = null;
        BattleManager.instance = null;
    }

    [Fact]
    public void Select_and_deselect_use_the_visible_toggle_and_observe_selection()
    {
        var ritual = Ritual();
        Register(ritual);
        using var boundary = Boundary();

        var selected = Submit(boundary, ritual, RitualLifecycleActionKind.Select);
        var deselected = Submit(boundary, ritual, RitualLifecycleActionKind.Deselect);

        Assert.True(selected.Verified, selected.Reason);
        Assert.True(deselected.Verified, deselected.Reason);
        Assert.False(RitualManager.instance!.selectedRitual.IsItem(ritual));
    }

    [Fact]
    public void Starting_level_uses_the_native_clamp_route_and_one_level_sentinel()
    {
        var ritual = Ritual();
        ritual.NativeMaximumSelectedLevel = 7;
        Register(ritual);
        RitualManager.instance!.selectedRitual.ToggleValue(ritual);
        using var boundary = Boundary();

        var changed = Submit(boundary, ritual, RitualLifecycleActionKind.SetLevel, level: 5);
        ritual.SuppressLevelChange = true;
        var missing = Submit(boundary, ritual, RitualLifecycleActionKind.SetLevel, level: 6);

        Assert.True(changed.Verified, changed.Reason);
        Assert.Equal(5, ritual.selectedLevel);
        Assert.Equal(RitualLifecyclePreflight.VerificationFailed, missing.Preflight);
    }

    [Fact]
    public void Starting_level_refuses_below_the_native_control_floor_and_names_both_bounds()
    {
        var ritual = Ritual();
        ritual.NativeMaximumSelectedLevel = 7;
        Register(ritual);
        RitualManager.instance!.selectedRitual.ToggleValue(ritual);
        ritual.selectedLevel = 3;
        using var boundary = Boundary();

        var refused = Submit(boundary, ritual, RitualLifecycleActionKind.SetLevel, level: 0);

        Assert.Equal(RitualLifecyclePreflight.LevelOutOfRange, refused.Preflight);
        Assert.Equal(1, refused.MinimumAmount);
        Assert.Equal(7, refused.MaximumAmount);
        Assert.Contains("between 1 and 7", refused.Reason, StringComparison.Ordinal);
        Assert.Equal(3, ritual.selectedLevel);
    }

    /// <summary>
    /// Both verbs act on the selected ritual, and the game's selection variable holds whichever
    /// ritual its toggle was last pressed with. A caller that asked for a level or an activation was
    /// refused for a step it could not see; the tool makes that press, whatever was selected before.
    /// </summary>
    [Fact]
    public void Level_and_activation_press_the_screens_own_selection_first()
    {
        var held = Ritual();
        var wanted = Ritual();
        var resource = new ResourceSO { quantity = new BigDouble(10) };
        resource.SetGuid(Guid.NewGuid());
        wanted.activationCost.costs.Add(new ResourceTuple(resource, new BigDouble(3)));
        wanted.NativeMaximumSelectedLevel = 7;
        Register(held);
        Register(wanted);
        RitualManager.instance!.selectedRitual.ToggleValue(held);
        using var boundary = Boundary();

        var leveled = Submit(boundary, wanted, RitualLifecycleActionKind.SetLevel, level: 5);
        var activated = Submit(boundary, wanted, RitualLifecycleActionKind.Activate);

        Assert.True(leveled.Verified, leveled.Reason);
        Assert.Equal(5, wanted.selectedLevel);
        Assert.True(activated.Verified, activated.Reason);
        Assert.True(wanted.inBattle);
        Assert.True(RitualManager.instance.selectedRitual.IsItem(wanted));
    }

    [Fact]
    public void Activate_revalidates_and_pays_the_screen_cost_before_the_manager_callback()
    {
        var ritual = Ritual();
        var resource = new ResourceSO { quantity = new BigDouble(10) };
        resource.SetGuid(Guid.NewGuid());
        ritual.activationCost.costs.Add(new ResourceTuple(resource, new BigDouble(3)));
        Register(ritual);
        RitualManager.instance!.selectedRitual.ToggleValue(ritual);
        using var boundary = Boundary();

        var result = Submit(boundary, ritual, RitualLifecycleActionKind.Activate);

        Assert.True(result.Verified, result.Reason);
        Assert.True(ritual.inBattle);
        Assert.Equal(1, ritual.activationCost.PerformCalls);
        Assert.Equal(new BigDouble(7), resource.quantity);
    }

    [Fact]
    public void Activate_no_op_fails_the_game_written_in_battle_sentinel()
    {
        var ritual = Ritual();
        Register(ritual);
        RitualManager.instance!.selectedRitual.ToggleValue(ritual);
        RitualManager.instance.SuppressActivation = true;
        using var boundary = Boundary();

        var result = Submit(boundary, ritual, RitualLifecycleActionKind.Activate);

        Assert.Equal(RitualLifecyclePreflight.VerificationFailed, result.Preflight);
        Assert.False(ritual.inBattle);
    }

    [Fact]
    public void Cancel_duration_clears_game_owned_effect_instances_and_no_op_fails()
    {
        var ritual = Ritual();
        ritual.durationRewardBlocks.Add(new object());
        ritual.ritualInstances = new System.Collections.Generic.List<object> { new object() };
        Register(ritual);
        using var boundary = Boundary();

        var canceled = Submit(boundary, ritual, RitualLifecycleActionKind.CancelDuration);
        ritual.ritualInstances!.Add(new object());
        ritual.SuppressCancel = true;
        var missing = Submit(boundary, ritual, RitualLifecycleActionKind.CancelDuration);

        Assert.True(canceled.Verified, canceled.Reason);
        Assert.Equal(RitualLifecyclePreflight.VerificationFailed, missing.Preflight);
    }

    [Fact]
    public void End_battle_targets_the_active_ritual_and_observes_the_native_clear()
    {
        var ritual = Ritual();
        Register(ritual);
        ritual.inBattle = true;
        BattleManager.instance!.activeRitual.ToggleValue(ritual);
        using var boundary = Boundary();

        var ended = Submit(boundary, ritual, RitualLifecycleActionKind.EndBattle);

        Assert.True(ended.Verified, ended.Reason);
        Assert.False(BattleManager.instance.IsInCombat());
        Assert.False(ritual.inBattle);
    }

    [Fact]
    public void End_battle_refuses_the_wrong_target_and_no_op_fails_the_active_sentinel()
    {
        var active = Ritual();
        var requested = Ritual();
        Register(active);
        Register(requested);
        BattleManager.instance!.activeRitual.ToggleValue(active);
        using var boundary = Boundary();

        var wrong = Submit(boundary, requested, RitualLifecycleActionKind.EndBattle);
        Assert.Equal(RitualLifecyclePreflight.WrongActiveRitual, wrong.Preflight);

        BattleManager.instance.activeRitual.ToggleValue(active);
        BattleManager.instance.activeRitual.ToggleValue(requested);
        BattleManager.instance.SuppressEndRitual = true;
        var noOp = Submit(boundary, requested, RitualLifecycleActionKind.EndBattle);
        Assert.Equal(RitualLifecyclePreflight.VerificationFailed, noOp.Preflight);
    }

    [Fact]
    public void Unity_thread_is_refused_before_identity_or_native_state()
    {
        var ritual = Ritual();
        Register(ritual);
        using var boundary = Boundary();

        var result = ForeignThread.Run(() =>
            Submit(boundary, ritual, RitualLifecycleActionKind.Select));

        Assert.Equal(RitualLifecyclePreflight.WrongThread, result.Preflight);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_lifecycle_binding_set()
    {
        foreach (var missing in RitualLifecycleNativeBindings.ContractIds)
        {
            using var boundary = Boundary(includeContract: id => id != missing);
            Assert.False(boundary.BindingsAvailable);
            Assert.Contains(missing, boundary.BindingFailure, StringComparison.Ordinal);
        }
    }

    private RitualLifecycleGameAction Boundary(Func<string, bool>? includeContract = null)
    {
        var resolver = new TypedRegistryResolver(() => Epoch,
            () => TypedRegistrySourceSnapshot.Ready(_registry),
            value => value is IdScriptableObject item ? item.GetGuid() : null);
        return new RitualLifecycleGameAction(() => Epoch, static () => true,
            static () => "RitualLifecycle ownership was revoked.",
            includeContract: includeContract, registry: resolver);
    }

    private static RitualLifecycleSubmission Submit(
        RitualLifecycleGameAction boundary,
        RitualSO ritual,
        RitualLifecycleActionKind kind,
        int level = 0)
    {
        var action = new RitualLifecycleAction(kind, ritual.GetGuid(), level, Epoch);
        return boundary.Submit(in action);
    }

    private void Register(RitualSO ritual) => _registry.Add(ritual.GetGuid(), ritual);

    private static RitualSO Ritual()
    {
        var ritual = new RitualSO { discovered = true };
        ritual.SetGuid(Guid.NewGuid());
        return ritual;
    }
}
