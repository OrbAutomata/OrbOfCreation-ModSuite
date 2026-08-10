using System;
using System.Collections;
using System.Threading.Tasks;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.Research;

public sealed class ResearchGameActionTests : IDisposable
{
    private const long Epoch = 91;
    private readonly IDictionary _registry = new Hashtable();

    public ResearchGameActionTests()
    {
        SettingsManager.ResearchQueueMode = false;
        GlobalVariables.MultiBuy = new IntVariable { Value = 1 };
    }

    public void Dispose()
    {
        SettingsManager.ResearchQueueMode = false;
        GlobalVariables.MultiBuy = new IntVariable { Value = 1 };
    }

    [Fact]
    public void Develop_immediate_uses_purchase_level_and_verifies_active_development()
    {
        var target = Research();
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, ResearchActionKind.Develop);

        Assert.True(result.Verified, result.Reason);
        Assert.True(target.isDeveloping);
        Assert.True(target.isActive);
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 1), result.CallOutcome);
    }

    [Fact]
    public void Queue_mode_replays_the_native_cumulative_cost_loop_and_queues_only_affordable_levels()
    {
        SettingsManager.ResearchQueueMode = true;
        GlobalVariables.MultiBuy = new IntVariable { Value = 3 };
        var resource = new ResourceSO { quantity = new BigDouble(15) };
        var target = Research(maxLevel: 5);
        target.researchCost.costs.Add(new ResourceTuple(resource, new BigDouble(10)));
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, ResearchActionKind.Develop);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(0, target.queuedLevels);
        Assert.True(target.isDeveloping);
    }

    [Fact]
    public void Pause_resume_and_cancel_verify_only_the_requested_target_state()
    {
        var target = Research();
        target.isDeveloping = true;
        target.isActive = true;
        target.queuedLevels = 2;
        Register(target);
        using var boundary = Boundary();

        var paused = Submit(boundary, target, ResearchActionKind.Pause);
        var resumed = Submit(boundary, target, ResearchActionKind.Resume);
        var cancelled = Submit(boundary, target, ResearchActionKind.Cancel);

        Assert.True(paused.Verified, paused.Reason);
        Assert.True(resumed.Verified, resumed.Reason);
        Assert.True(cancelled.Verified, cancelled.Reason);
        Assert.False(target.isDeveloping);
        Assert.False(target.isActive);
        Assert.Equal(0, target.queuedLevels);
    }

    [Fact]
    public void Bonus_uses_free_research_type_capacity_and_verifies_self_bonus_identity()
    {
        var type = new ResearchTypeSO { FreeBonusLevels = 2 };
        type.SetGuid(Guid.NewGuid());
        var target = Research();
        target.researchTypes.Add(type);
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, ResearchActionKind.Bonus);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(1, target.selfBonusLevels);
        Assert.Equal(1, type.UsedBonusLevels);
    }

    [Fact]
    public void Zero_available_levels_name_the_native_gate_that_closed()
    {
        var target = Research();
        target.researchCost.affordable = false;
        Register(target);
        var permits = 0;
        using var boundary = Boundary(permit: () => { permits++; return true; });

        var result = Submit(boundary, target, ResearchActionKind.Develop);

        // No amount would have been admitted, so the answer is the cause, not the ceiling.
        Assert.Equal(ResearchPreflight.Unaffordable, result.Preflight);
        Assert.DoesNotContain("at most", result.Reason, StringComparison.Ordinal);
        Assert.Equal(0, permits);
        Assert.False(target.isDeveloping);
    }

    [Fact]
    public void Exhausted_leeway_refuses_only_once_a_cap_is_also_reached()
    {
        var target = Research();
        target.stillHasLeeway = false;
        Register(target);
        using var boundary = Boundary();

        // Leeway alone is not the gate: the game develops on leeway OR on both caps being open.
        var openCaps = Submit(boundary, target, ResearchActionKind.Develop);

        target.isDeveloping = false;
        target.isActive = false;
        target.belowArtificialMaxLevel = false;
        var closedCap = Submit(boundary, target, ResearchActionKind.Develop);

        Assert.True(openCaps.Verified, openCaps.Reason);
        Assert.Equal(ResearchPreflight.LeewayExhausted, closedCap.Preflight);
        Assert.Contains("leeway", closedCap.Reason, StringComparison.Ordinal);
        Assert.Contains("level cap", closedCap.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_running_development_is_named_as_the_reason_a_second_develop_is_refused()
    {
        var target = Research();
        Register(target);
        using var boundary = Boundary();

        Assert.True(Submit(boundary, target, ResearchActionKind.Develop).Verified);
        var second = Submit(boundary, target, ResearchActionKind.Develop);

        Assert.Equal(ResearchPreflight.AlreadyDeveloping, second.Preflight);
    }

    [Fact]
    public void Develop_refuses_an_amount_larger_than_the_live_available_levels()
    {
        var target = Research();
        Register(target);
        var permits = 0;
        using var boundary = Boundary(permit: () => { permits++; return true; });

        var result = Submit(boundary, target, ResearchActionKind.Develop, amount: 3);

        Assert.Equal(ResearchPreflight.AmountUnavailable, result.Preflight);
        Assert.Contains("at most 1 level", result.Reason, StringComparison.Ordinal);

        // The cap is this call's, not a budget: the sentence names the mode that caps it.
        Assert.Contains("this call takes", result.Reason, StringComparison.Ordinal);
        Assert.Contains("Research Queue Mode is off", result.Reason, StringComparison.Ordinal);
        Assert.Equal(0, permits);
        Assert.False(target.isDeveloping);
    }

    /// <summary>
    /// Nothing is wrong with the mode a caller named when the game's own queue setting is what
    /// closes pause and resume, so the refusal is the same state class every other precondition
    /// answers with rather than the class reserved for a bad argument.
    /// </summary>
    [Fact]
    public void Ui_mode_and_state_gates_are_preserved_for_pause_and_bonus()
    {
        SettingsManager.ResearchQueueMode = true;
        var target = Research();
        target.isDeveloping = true;
        target.isActive = true;
        var type = new ResearchTypeSO { FreeBonusLevels = 1 };
        type.SetGuid(Guid.NewGuid());
        target.researchTypes.Add(type);
        Register(target);
        using var boundary = Boundary();

        var pause = Submit(boundary, target, ResearchActionKind.Pause);
        var bonus = Submit(boundary, target, ResearchActionKind.Bonus);

        Assert.Equal(ResearchPreflight.InvalidState, pause.Preflight);
        Assert.Equal(ResearchPreflight.InvalidState, bonus.Preflight);
    }

    [Fact]
    public void Missing_outcome_revalidates_but_throw_after_requested_outcome_commits()
    {
        var target = Research();
        Register(target);
        target.SuppressAction = true;
        using var boundary = Boundary();

        var failed = Submit(boundary, target, ResearchActionKind.Develop);
        var retry = Submit(boundary, target, ResearchActionKind.Develop);
        target.SuppressAction = false;
        boundary.InvalidateLifecycle();
        target.ThrowAfterAction = true;
        var committed = Submit(boundary, target, ResearchActionKind.Develop);

        Assert.Equal(ResearchPreflight.VerificationFailed, failed.Preflight);
        Assert.Equal(ResearchPreflight.VerificationFailed, retry.Preflight);
        Assert.True(committed.Verified, committed.Reason);
    }

    [Fact]
    public async Task Unity_thread_is_revalidated_before_identity_or_native_state()
    {
        var target = Research();
        Register(target);
        using var boundary = Boundary();

        var result = await Task.Run(() => Submit(boundary, target, ResearchActionKind.Develop));

        Assert.Equal(ResearchPreflight.WrongThread, result.Preflight);
        Assert.False(target.isDeveloping);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_lifecycle_binding_set()
    {
        foreach (var missing in ResearchNativeBindings.ContractIds)
        {
            using var boundary = Boundary(includeContract: id => id != missing);
            Assert.False(boundary.BindingsAvailable);
            Assert.Contains(missing, boundary.BindingFailure, StringComparison.Ordinal);
        }
    }

    private ResearchGameAction Boundary(Func<bool>? permit = null,
        Func<string, bool>? includeContract = null)
    {
        static long ReadEpoch() => Epoch;
        var resolver = new TypedRegistryResolver(ReadEpoch,
            () => TypedRegistrySourceSnapshot.Ready(_registry),
            value => value is ResearchSO research ? research.GetGuid() : null);
        return new ResearchGameAction(ReadEpoch, permit ?? (() => true),
            static () => "ResearchLifecycle ownership was revoked.",
            includeContract: includeContract, registry: resolver);
    }

    private static ResearchSubmission Submit(ResearchGameAction boundary,
        ResearchSO target, ResearchActionKind kind, int amount = 1)
    {
        var action = new ResearchAction(kind, target.GetGuid(), amount, Epoch);
        return boundary.Submit(in action);
    }

    private void Register(ResearchSO target) => _registry.Add(target.GetGuid(), target);

    private static ResearchSO Research(int maxLevel = 4) => new()
    {
        uuid = Guid.NewGuid().ToString("D"),
        maxLevel = maxLevel,
        available = true,
    };
}
