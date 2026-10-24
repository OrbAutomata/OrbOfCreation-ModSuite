using OrbModding.TestSupport;
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

        Assert.True(selected.Outcome.Verified, selected.Outcome.Reason);
        Assert.True(deselected.Outcome.Verified, deselected.Outcome.Reason);
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

        Assert.True(changed.Outcome.Verified, changed.Outcome.Reason);
        Assert.Equal(5, ritual.selectedLevel);
        Assert.Equal(RitualLifecycleActionResultCodes.VerificationFailed, missing.Outcome.Code);
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

        Assert.Equal(RitualLifecycleActionResultCodes.LevelOutOfRange, refused.Outcome.Code);
        Assert.Equal(1, refused.MinimumAmount);
        Assert.Equal(7, refused.MaximumAmount);
        Assert.Contains("between 1 and 7", refused.Outcome.Reason, StringComparison.Ordinal);
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

        Assert.True(leveled.Outcome.Verified, leveled.Outcome.Reason);
        Assert.Equal(5, wanted.selectedLevel);
        Assert.True(activated.Outcome.Verified, activated.Outcome.Reason);
        Assert.True(wanted.inBattle);
        Assert.True(RitualManager.instance.selectedRitual.IsItem(wanted));
    }

    /// <summary>
    /// The selection press overwrites whatever was selected and the game keeps no undo for it, so
    /// a press that did not select the ritual is an attempt the caller must check, never a refusal
    /// with nothing sent, and nothing is paid after it.
    /// </summary>
    [Fact]
    public void A_selection_press_that_does_not_select_is_an_attempted_fault_before_any_payment()
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
        RitualManager.instance.selectedRitual.SuppressToggle = true;
        using var boundary = Boundary();

        var leveled = Submit(boundary, wanted, RitualLifecycleActionKind.SetLevel, level: 5);
        var activated = Submit(boundary, wanted, RitualLifecycleActionKind.Activate);

        foreach (var result in new[] { leveled, activated })
        {
            Assert.True(result.Outcome.IsFault);
            Assert.Equal(RitualLifecycleActionResultCodes.VerificationFailed, result.Outcome.Code);
            Assert.Equal(1, result.Outcome.CallOutcome.MutationAttempts);
            Assert.Contains("did not become the selected one", result.Outcome.Reason,
                StringComparison.Ordinal);
        }
        Assert.Equal(0, wanted.activationCost.PerformCalls);
        Assert.Equal(new BigDouble(10), resource.quantity);
        Assert.NotEqual(5, wanted.selectedLevel);
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

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
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

        Assert.Equal(RitualLifecycleActionResultCodes.VerificationFailed, result.Outcome.Code);
        Assert.False(ritual.inBattle);
    }

    [Fact]
    public void Cancel_duration_clears_game_owned_effect_instances_and_no_op_fails()
    {
        var ritual = Ritual();
        ritual.durationRewardBlocks.Add(new PersistentEffectBlock());
        ritual.ritualInstances = new System.Collections.Generic.List<object> { new object() };
        Register(ritual);
        using var boundary = Boundary();

        var canceled = Submit(boundary, ritual, RitualLifecycleActionKind.CancelDuration);
        ritual.ritualInstances!.Add(new object());
        ritual.SuppressCancel = true;
        var missing = Submit(boundary, ritual, RitualLifecycleActionKind.CancelDuration);

        Assert.True(canceled.Outcome.Verified, canceled.Outcome.Reason);
        Assert.Equal(RitualLifecycleActionResultCodes.VerificationFailed, missing.Outcome.Code);
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

        Assert.True(ended.Outcome.Verified, ended.Outcome.Reason);
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
        Assert.Equal(RitualLifecycleActionResultCodes.WrongActiveRitual, wrong.Outcome.Code);

        BattleManager.instance.activeRitual.ToggleValue(active);
        BattleManager.instance.activeRitual.ToggleValue(requested);
        BattleManager.instance.SuppressEndRitual = true;
        var noOp = Submit(boundary, requested, RitualLifecycleActionKind.EndBattle);
        Assert.Equal(RitualLifecycleActionResultCodes.VerificationFailed, noOp.Outcome.Code);
    }

    [Fact]
    public void An_unreadable_lifecycle_epoch_faults_before_the_native_call()
    {
        var ritual = Ritual();
        Register(ritual);
        using var boundary = Boundary(epoch: () => throw new InvalidOperationException("lifecycle unavailable"));

        var result = Submit(boundary, ritual, RitualLifecycleActionKind.Select);

        Assert.True(result.Outcome.IsFault);
        Assert.Equal(RitualLifecycleActionResultCodes.ContractUnavailable, result.Outcome.Code);
        Assert.Equal(0, result.Outcome.CallOutcome.MutationAttempts);
        Assert.False(RitualManager.instance!.selectedRitual.IsItem(ritual));
    }

    [Fact]
    public void Unity_thread_is_refused_before_identity_or_native_state()
    {
        var ritual = Ritual();
        Register(ritual);
        using var boundary = Boundary();

        var result = ForeignThread.Run(() =>
            Submit(boundary, ritual, RitualLifecycleActionKind.Select));

        Assert.Equal(RitualLifecycleActionResultCodes.WrongThread, result.Outcome.Code);
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

    private RitualLifecycleGameAction Boundary(
        Func<string, bool>? includeContract = null,
        Func<long>? epoch = null)
    {
        var resolver = TestRegistryResolver.For(() => Epoch, _registry);
        return new RitualLifecycleGameAction(epoch ?? (() => Epoch), static () => true,
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
