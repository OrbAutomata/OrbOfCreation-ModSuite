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
        var target = Register(Challenge());
        ChallengeManager.instance.activeChallenges.value.Add(target);
        using var boundary = Boundary();

        var selected = Submit(boundary, ChallengeActionKind.Select, target);
        var unselected = Submit(boundary, ChallengeActionKind.Select, target);
        ChallengeManager.instance.preferredChallenges.RestrictedChallenges.Add(target);
        var restricted = Submit(boundary, ChallengeActionKind.Select, target);

        Assert.True(selected.Verified, selected.Reason);
        Assert.True(unselected.Verified, unselected.Reason);
        Assert.Equal(ChallengePreflight.SelectionRestricted, restricted.Preflight);
        Assert.Equal(2, ChallengeManager.instance.preferredChallenges.ToggleCalls);
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
    public void First_time_fetch_sets_fetched_without_spending_a_reroll_and_materializes_time_offers()
    {
        var first = Register(Challenge());
        var second = Register(Challenge());
        ChallengeManager.instance.NextChallenges.Add(first);
        ChallengeManager.instance.NextChallenges.Add(second);
        using var boundary = Boundary();

        var result = Submit(boundary, ChallengeActionKind.FetchTime);

        Assert.True(result.Verified, result.Reason);
        Assert.True(PersistentResetManager.instance.hasFetchedChallenges.value);
        Assert.Equal(2, PersistentResetManager.instance.challengeRerollsLeft.AsInt());
        Assert.Equal(new[] { first, second }, ChallengeManager.instance.activeChallenges.value);
        Assert.All(ChallengeManager.instance.activeChallenges.value,
            challenge => Assert.Equal(ChallengeSO.ChallengeState.QueuedStart, challenge.state));
    }

    [Fact]
    public void Later_prestige_fetch_spends_one_reroll_then_returns_the_new_ordered_offer_state()
    {
        PersistentResetManager.instance.hasFetchedChallenges.value = true;
        var first = Register(Challenge());
        var second = Register(Challenge());
        PersistentResetManager.instance.NextChallenges.Add(first);
        PersistentResetManager.instance.NextChallenges.Add(second);
        using var boundary = Boundary();

        var result = Submit(boundary, ChallengeActionKind.FetchPrestige);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(1, PersistentResetManager.instance.challengeRerollsLeft.AsInt());
        Assert.Equal(new[] { first, second }, PersistentResetManager.instance.activeChallenges.value);
    }

    [Fact]
    public void Fetch_commits_when_the_requested_offer_list_materializes()
    {
        var target = Register(Challenge());
        target.SuppressQueueActivation = true;
        ChallengeManager.instance.NextChallenges.Add(target);
        using var boundary = Boundary();

        var result = Submit(boundary, ChallengeActionKind.FetchTime);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(new[] { target }, ChallengeManager.instance.activeChallenges.value);
        Assert.NotEqual(ChallengeSO.ChallengeState.QueuedStart, target.state);
    }

    [Fact]
    public void FetchFailsVerificationWhenTheNativeOfferListDoesNotChange()
    {
        var target = Register(Challenge());
        ChallengeManager.instance.NextChallenges.Add(target);
        ChallengeManager.instance.SuppressFetch = true;
        using var boundary = Boundary();

        var result = Submit(boundary, ChallengeActionKind.FetchTime);

        Assert.Equal(ChallengePreflight.VerificationFailed, result.Preflight);
        Assert.True(PersistentResetManager.instance.hasFetchedChallenges.value);
        Assert.Empty(ChallengeManager.instance.activeChallenges.value);
        Assert.Equal(1, ChallengeManager.instance.FetchCalls);
    }

    [Fact]
    public void Fetch_refusals_happen_before_flags_rerolls_or_native_callbacks()
    {
        PersistentResetManager.instance.hasFetchedChallenges.value = true;
        PersistentResetManager.instance.challengeRerollsLeft.Value = 0;
        using var boundary = Boundary();

        var noRerolls = Submit(boundary, ChallengeActionKind.FetchTime);
        PersistentResetManager.instance.hasCompleteWorldCycle.value = false;
        var incomplete = Submit(boundary, ChallengeActionKind.FetchPrestige);

        Assert.Equal(ChallengePreflight.NoRerolls, noRerolls.Preflight);
        Assert.Equal(0, noRerolls.RerollsLeft);
        Assert.Equal(ChallengePreflight.FetchUnavailable, incomplete.Preflight);
        Assert.Equal(-1, incomplete.RerollsLeft);
        Assert.Equal(0, ChallengeManager.instance.FetchCalls);
        Assert.Equal(0, PersistentResetManager.instance.FetchCalls);
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
        ChallengeActionKind kind, ChallengeSO? target = null)
    {
        var action = new ChallengeAction(kind, target?.GetGuid() ?? Guid.Empty, Epoch);
        return boundary.Submit(in action);
    }

    private ChallengeSO Register(ChallengeSO target)
    {
        _registry.Add(target.GetGuid(), target);
        IdScriptableObject.RuntimeLookup[target.GetGuid()] = target;
        return target;
    }

    private static ChallengeSO Challenge()
    {
        var challenge = new ChallengeSO { maxLevel = 10, difficulty = 12, baseReward = 30 };
        challenge.SetGuid(Guid.NewGuid());
        return challenge;
    }
}
