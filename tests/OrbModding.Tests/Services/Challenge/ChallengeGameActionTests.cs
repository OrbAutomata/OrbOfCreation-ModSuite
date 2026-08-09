using System;
using System.Collections;
using System.Threading.Tasks;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.Challenge;

public sealed class ChallengeGameActionTests : IDisposable
{
    private const long Epoch = 91;
    private readonly IDictionary _registry = new Hashtable();

    public ChallengeGameActionTests()
    {
        ChallengeManager.instance = new ChallengeManager();
        PersistentResetManager.instance = new PersistentResetManager();
        PersistentResetManager.instance.hasCompleteWorldCycle.value = true;
        PersistentResetManager.instance.challengeRerollsLeft.Value = 2;
        PersistentResetManager.instance.challengeRerollsMax.Value = 3;
    }

    public void Dispose()
    {
        ChallengeManager.instance = new ChallengeManager();
        PersistentResetManager.instance = new PersistentResetManager();
        IdScriptableObject.RuntimeLookup.Clear();
    }

    [Fact]
    public void Select_toggles_the_exact_offered_identity_and_respects_native_restrictions()
    {
        var unique = LimitedType();
        var target = Register(Challenge(unique));
        var conflicting = Register(Challenge(unique));
        ChallengeManager.instance.activeChallenges.value.Add(target);
        ChallengeManager.instance.preferredChallenges.Maximum = 2;
        using var boundary = Boundary();

        var selected = Submit(boundary, ChallengeActionKind.Select, target);
        var unselected = Submit(boundary, ChallengeActionKind.Select, target);
        ChallengeManager.instance.preferredChallenges.value.Add(conflicting);
        var restricted = Submit(boundary, ChallengeActionKind.Select, target);

        Assert.True(selected.Verified, selected.Reason);
        Assert.True(unselected.Verified, unselected.Reason);
        Assert.Equal(ChallengePreflight.SelectionRestricted, restricted.Preflight);
        Assert.Equal(2, ChallengeManager.instance.preferredChallenges.ToggleCalls);
    }

    /// <summary>
    /// The game reads a type conflict off the selection as it stands, so the row being given up is
    /// still counted until its press lands. Asked before that press, the conflict refuses exactly
    /// the swap the screen performs: give up the one holding the type, then take the one that wants
    /// it.
    /// </summary>
    [Fact]
    public void A_swap_asks_the_type_conflict_of_the_selection_the_give_up_press_leaves_behind()
    {
        var unique = LimitedType();
        var held = Register(Challenge(unique));
        var wanted = Register(Challenge(unique));
        ChallengeManager.instance.activeChallenges.value.Add(wanted);
        ChallengeManager.instance.preferredChallenges.Maximum = 1;
        ChallengeManager.instance.preferredChallenges.value.Add(held);
        using var boundary = Boundary();

        var swapped = Submit(boundary, ChallengeActionKind.Select, wanted, held);

        Assert.True(swapped.Verified, swapped.Reason);
        Assert.Equal(new[] { wanted }, ChallengeManager.instance.preferredChallenges.value);
    }

    /// <summary>
    /// A conflict the give-up does not clear still refuses, and the row offered up comes back: a
    /// caller that asked for a swap and was told no must not be left one selection poorer.
    /// </summary>
    [Fact]
    public void A_conflict_the_give_up_does_not_clear_refuses_and_puts_the_given_up_row_back()
    {
        var unique = LimitedType();
        var held = Register(Challenge());
        var blocking = Register(Challenge(unique));
        var wanted = Register(Challenge(unique));
        ChallengeManager.instance.activeChallenges.value.Add(wanted);
        ChallengeManager.instance.preferredChallenges.Maximum = 2;
        ChallengeManager.instance.preferredChallenges.value.Add(held);
        ChallengeManager.instance.preferredChallenges.value.Add(blocking);
        using var boundary = Boundary();

        var refused = Submit(boundary, ChallengeActionKind.Select, wanted, held);

        Assert.Equal(ChallengePreflight.SelectionRestricted, refused.Preflight);
        Assert.Equal(new[] { blocking, held }, ChallengeManager.instance.preferredChallenges.value);
        Assert.Equal(0, refused.CallOutcome.MutationsCommitted);
    }

    /// <summary>
    /// Selecting past a full list is two presses on the screen — give up a row, then take the one
    /// you want — and a caller that only ever saw the refusal had no way to learn the first press
    /// existed. Both halves are the postcondition: half a swap is a selection nobody asked for.
    /// </summary>
    [Fact]
    public void A_full_selection_gives_up_the_named_row_before_taking_the_one_asked_for()
    {
        var held = Register(Challenge());
        var wanted = Register(Challenge());
        ChallengeManager.instance.activeChallenges.value.Add(held);
        ChallengeManager.instance.activeChallenges.value.Add(wanted);
        ChallengeManager.instance.preferredChallenges.Maximum = 1;
        ChallengeManager.instance.preferredChallenges.value.Add(held);
        using var boundary = Boundary();

        var swapped = Submit(boundary, ChallengeActionKind.Select, wanted, held);

        Assert.True(swapped.Verified, swapped.Reason);
        Assert.Equal(new[] { wanted }, ChallengeManager.instance.preferredChallenges.value);
    }

    [Fact]
    public void A_full_selection_with_more_than_one_row_to_give_up_is_refused_as_the_callers_choice()
    {
        var first = Register(Challenge());
        var second = Register(Challenge());
        var wanted = Register(Challenge());
        ChallengeManager.instance.activeChallenges.value.Add(wanted);
        ChallengeManager.instance.preferredChallenges.Maximum = 2;
        ChallengeManager.instance.preferredChallenges.value.Add(first);
        ChallengeManager.instance.preferredChallenges.value.Add(second);
        using var boundary = Boundary();

        var refused = Submit(boundary, ChallengeActionKind.Select, wanted);

        Assert.Equal(ChallengePreflight.SelectionFull, refused.Preflight);
        Assert.Contains("the caller's choice", refused.Reason, StringComparison.Ordinal);
        Assert.Equal(0, ChallengeManager.instance.preferredChallenges.ToggleCalls);
    }

    [Fact]
    public void Queue_toggles_only_idle_or_queued_offers_and_abandon_gates_on_active_state()
    {
        var target = Register(Challenge());
        ChallengeManager.instance.activeChallenges.value.Add(target);
        using var boundary = Boundary();

        var queued = Submit(boundary, ChallengeActionKind.Queue, target);
        var queuedState = target.state;
        var idle = Submit(boundary, ChallengeActionKind.Queue, target);
        var refusedAbandon = Submit(boundary, ChallengeActionKind.Abandon, target);
        target.state = ChallengeSO.ChallengeState.CurrentlyActive;
        var abandoned = Submit(boundary, ChallengeActionKind.Abandon, target);

        Assert.True(queued.Verified, queued.Reason);
        Assert.Equal(ChallengeSO.ChallengeState.QueuedStart, queuedState);
        Assert.True(idle.Verified, idle.Reason);
        Assert.Equal(ChallengePreflight.InvalidState, refusedAbandon.Preflight);
        Assert.True(abandoned.Verified, abandoned.Reason);
        Assert.Equal(ChallengeSO.ChallengeState.Failed, target.state);
    }

    [Fact]
    public void The_first_draw_sets_fetched_without_spending_a_reroll_and_materializes_the_offers()
    {
        var first = Register(Challenge());
        var second = Register(Challenge());
        ChallengeManager.instance.NextChallenges.Add(first);
        ChallengeManager.instance.NextChallenges.Add(second);
        using var boundary = Boundary();

        var result = Submit(boundary, ChallengeActionKind.Reroll);

        Assert.True(result.Verified, result.Reason);
        Assert.True(PersistentResetManager.instance.hasFetchedChallenges.value);
        Assert.Equal(2, PersistentResetManager.instance.challengeRerollsLeft.AsInt());
        Assert.Equal(new[] { first, second }, ChallengeManager.instance.activeChallenges.value);
        Assert.All(ChallengeManager.instance.activeChallenges.value,
            challenge => Assert.Equal(ChallengeSO.ChallengeState.QueuedStart, challenge.state));
    }

    [Fact]
    public void A_later_reroll_spends_one_reroll_then_returns_the_new_ordered_offer_state()
    {
        PersistentResetManager.instance.hasFetchedChallenges.value = true;
        var first = Register(Challenge());
        var second = Register(Challenge());
        ChallengeManager.instance.NextChallenges.Add(first);
        ChallengeManager.instance.NextChallenges.Add(second);
        using var boundary = Boundary();

        var result = Submit(boundary, ChallengeActionKind.Reroll);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(1, PersistentResetManager.instance.challengeRerollsLeft.AsInt());
        Assert.Equal(new[] { first, second }, ChallengeManager.instance.activeChallenges.value);
    }

    [Fact]
    public void Reroll_commits_when_the_requested_offer_list_materializes()
    {
        var target = Register(Challenge());
        target.SuppressQueueActivation = true;
        ChallengeManager.instance.NextChallenges.Add(target);
        using var boundary = Boundary();

        var result = Submit(boundary, ChallengeActionKind.Reroll);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(new[] { target }, ChallengeManager.instance.activeChallenges.value);
        Assert.NotEqual(ChallengeSO.ChallengeState.QueuedStart, target.state);
    }

    /// <summary>
    /// The eligible pool can be small enough that an honest redraw hands back the same offers in
    /// the same order. The press still spent, so it committed: the budget decrement the game's own
    /// button performs is the postcondition, and the offer set is not a promise the game makes.
    /// </summary>
    [Fact]
    public void An_identical_redraw_commits_because_the_budget_moved()
    {
        PersistentResetManager.instance.hasFetchedChallenges.value = true;
        var target = Register(Challenge());
        ChallengeManager.instance.activeChallenges.value.Add(target);
        ChallengeManager.instance.NextChallenges.Add(target);
        using var boundary = Boundary();

        var result = Submit(boundary, ChallengeActionKind.Reroll);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(1, PersistentResetManager.instance.challengeRerollsLeft.AsInt());
        Assert.Equal(new[] { target }, ChallengeManager.instance.activeChallenges.value);
        Assert.Equal(1, ChallengeManager.instance.FetchCalls);
    }

    /// <summary>
    /// A press that failed still has to say where the scarce budget landed, so the failure hands
    /// over the settled budget on both sides instead of leaving the caller to infer a spend from an
    /// absent key.
    /// </summary>
    [Fact]
    public void A_failed_press_publishes_the_budget_on_both_sides()
    {
        PersistentResetManager.instance.hasFetchedChallenges.value = true;
        PersistentResetManager.instance.challengeRerollsLeft.ThrowBeforeWriteFor = 1;
        var target = Register(Challenge());
        ChallengeManager.instance.NextChallenges.Add(target);
        using var boundary = Boundary();

        var result = Submit(boundary, ChallengeActionKind.Reroll);

        Assert.Equal(ChallengePreflight.PostCommitFault, result.Preflight);
        Assert.Equal(2, result.RerollsLeft);
        Assert.Equal(2, result.RerollsLeftAfter);
        Assert.Equal(0, ChallengeManager.instance.FetchCalls);
    }

    [Fact]
    public void Reroll_refusals_happen_before_flags_rerolls_or_native_callbacks()
    {
        PersistentResetManager.instance.hasFetchedChallenges.value = true;
        PersistentResetManager.instance.challengeRerollsLeft.Value = 0;
        using var boundary = Boundary();

        var noRerolls = Submit(boundary, ChallengeActionKind.Reroll);
        PersistentResetManager.instance.hasCompleteWorldCycle.value = false;
        var incomplete = Submit(boundary, ChallengeActionKind.Reroll);

        Assert.Equal(ChallengePreflight.NoRerolls, noRerolls.Preflight);
        Assert.Equal(0, noRerolls.RerollsLeft);
        Assert.Equal(ChallengePreflight.FetchUnavailable, incomplete.Preflight);
        Assert.Equal(-1, incomplete.RerollsLeft);
        Assert.Equal(0, ChallengeManager.instance.FetchCalls);
    }

    [Fact]
    public void Missing_target_outcome_revalidates_but_throw_after_observable_outcome_commits()
    {
        var target = Register(Challenge());
        ChallengeManager.instance.activeChallenges.value.Add(target);
        target.SuppressQueueToggle = true;
        using var failedBoundary = Boundary();

        var failed = Submit(failedBoundary, ChallengeActionKind.Queue, target);
        var retry = Submit(failedBoundary, ChallengeActionKind.Queue, target);
        failedBoundary.Dispose();
        target.SuppressQueueToggle = false;
        target.ThrowAfterQueueToggle = true;
        using var committedBoundary = Boundary();
        var committed = Submit(committedBoundary, ChallengeActionKind.Queue, target);

        Assert.Equal(ChallengePreflight.VerificationFailed, failed.Preflight);
        Assert.Equal(ChallengePreflight.VerificationFailed, retry.Preflight);
        Assert.True(committed.Verified, committed.Reason);
    }

    [Fact]
    public async Task Unity_thread_and_complete_binding_set_are_fail_closed()
    {
        var target = Register(Challenge());
        ChallengeManager.instance.activeChallenges.value.Add(target);
        using var boundary = Boundary();
        var wrongThread = await Task.Run(() => Submit(boundary, ChallengeActionKind.Queue, target));
        Assert.Equal(ChallengePreflight.WrongThread, wrongThread.Preflight);

        foreach (var missing in ChallengeNativeBindings.ContractIds)
        {
            using var incomplete = Boundary(includeContract: id => id != missing);
            Assert.False(incomplete.BindingsAvailable);
            Assert.Contains(missing, incomplete.BindingFailure, StringComparison.Ordinal);
        }
    }

    private ChallengeGameAction Boundary(Func<string, bool>? includeContract = null)
    {
        var resolver = new TypedRegistryResolver(() => Epoch,
            () => TypedRegistrySourceSnapshot.Ready(_registry),
            value => value is IdScriptableObject item ? item.GetGuid() : null);
        return new ChallengeGameAction(() => Epoch, () => true,
            static () => "ChallengeLifecycle ownership was revoked.",
            includeContract: includeContract, registry: resolver);
    }

    private static ChallengeSubmission Submit(ChallengeGameAction boundary,
        ChallengeActionKind kind, ChallengeSO? target = null, ChallengeSO? replaced = null)
    {
        var action = new ChallengeAction(kind, target?.GetGuid() ?? Guid.Empty,
            replaced?.GetGuid() ?? Guid.Empty, Epoch);
        return boundary.Submit(in action);
    }

    private ChallengeSO Register(ChallengeSO target)
    {
        _registry.Add(target.GetGuid(), target);
        IdScriptableObject.RuntimeLookup[target.GetGuid()] = target;
        return target;
    }

    private static ChallengeSO Challenge(params ChallengeTypeSO[] types)
    {
        var challenge = new ChallengeSO { maxLevel = 10, difficulty = 12, baseReward = 30 };
        challenge.SetGuid(Guid.NewGuid());
        challenge.challengeTypes.AddRange(types);
        return challenge;
    }

    private static ChallengeTypeSO LimitedType()
    {
        var type = new ChallengeTypeSO { limitedToOneInstance = true };
        type.SetGuid(Guid.NewGuid());
        return type;
    }
}
