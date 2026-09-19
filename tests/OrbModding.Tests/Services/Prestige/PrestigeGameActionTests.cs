using System;
using System.Collections.Generic;
using System.Threading;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.Prestige;

public sealed class PrestigeGameActionTests : IDisposable
{
    private long _epoch = 10;

    public PrestigeGameActionTests()
    {
        PersistentResetManager.instance = new PersistentResetManager();
        PersistentResetManager.instance.hasCompleteWorldCycle.value = true;
        PersistentResetManager.instance.hasFetchedChallenges.value = true;
        PersistentResetManager.PersistentResetSignal = () => _epoch++;
        GameManager.PersistentResetCalls = 0;
        GameManager.CleanGameCalls = 0;
    }

    public void Dispose() => PersistentResetManager.PersistentResetSignal = null;

    [Fact]
    public void Complete_native_transaction_commits_on_the_lifecycle_identity_transition()
    {
        PersistentResetManager.instance.persistentResetCount.Value = 4;
        using var boundary = Boundary();

        var submission = Submit(boundary);

        Assert.True(submission.Verified);
        Assert.Equal(11, _epoch);
        Assert.Equal(1, PersistentResetManager.instance.ResetCalls);
        Assert.Equal(5, PersistentResetManager.instance.persistentResetCount.AsInt());
        Assert.False(PersistentResetManager.instance.hasCompleteWorldCycle.value);
        Assert.False(PersistentResetManager.instance.hasFetchedChallenges.value);
        Assert.Equal(1, GameManager.PersistentResetCalls);
        Assert.Equal(1, GameManager.CleanGameCalls);
    }

    [Fact]
    public void Ui_admission_flags_refuse_before_the_mutation_permit_and_native_call()
    {
        var permitCalls = 0;
        PersistentResetManager.instance.hasCompleteWorldCycle.value = false;
        using var boundary = Boundary(permit: () => { permitCalls++; return true; });
        var incomplete = Submit(boundary);
        PersistentResetManager.instance.hasCompleteWorldCycle.value = true;
        PersistentResetManager.instance.hasFetchedChallenges.value = false;
        var unfetched = Submit(boundary);

        Assert.Equal(PrestigePreflight.WorldCycleIncomplete, incomplete.Preflight);
        Assert.Equal(PrestigePreflight.ChallengesNotFetched, unfetched.Preflight);
        Assert.Equal(0, permitCalls);
        Assert.Equal(0, PersistentResetManager.instance.ResetCalls);
    }

    [Fact]
    public void Returned_transaction_without_lifecycle_replacement_faults_and_revalidates()
    {
        PersistentResetManager.PersistentResetSignal = null;
        using var boundary = Boundary();

        var first = Submit(boundary);
        var retry = Submit(boundary);

        Assert.Equal(PrestigePreflight.VerificationFailed, first.Preflight);
        Assert.Equal(PrestigePreflight.WorldCycleIncomplete, retry.Preflight);
        Assert.Equal(1, PersistentResetManager.instance.ResetCalls);
    }

    [Fact]
    public void More_than_one_lifecycle_transition_is_not_the_requested_single_reset()
    {
        PersistentResetManager.PersistentResetSignal = () => _epoch += 2;
        using var boundary = Boundary();

        var submission = Submit(boundary);

        Assert.Equal(PrestigePreflight.VerificationFailed, submission.Preflight);
        Assert.Equal(12, _epoch);
    }

    [Fact]
    public void Exception_after_the_lifecycle_sentinel_commits_the_requested_reset()
    {
        PersistentResetManager.instance.ThrowAfterReset = true;
        using var boundary = Boundary();

        var submission = Submit(boundary);

        Assert.True(submission.Verified, submission.Reason);
        Assert.Equal(11, _epoch);
    }

    [Fact]
    public void Wrong_thread_refuses_before_native_access()
    {
        using var boundary = Boundary();
        PrestigeSubmission submission = default;
        var thread = new Thread(() => submission = Submit(boundary));
        thread.Start();
        thread.Join();

        Assert.Equal(PrestigePreflight.WrongThread, submission.Preflight);
        Assert.Equal(0, PersistentResetManager.instance.ResetCalls);
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void Every_missing_lifecycle_member_fails_closed_at_binding(string withheld)
    {
        using var boundary = Boundary(include: id => id != withheld);

        var submission = Submit(boundary);

        Assert.False(boundary.BindingsAvailable);
        Assert.Equal(PrestigePreflight.ContractUnavailable, submission.Preflight);
        Assert.Contains(withheld, boundary.BindingFailure, StringComparison.Ordinal);
        Assert.Equal(0, PersistentResetManager.instance.ResetCalls);
    }

    public static IEnumerable<object[]> Contracts()
    {
        foreach (var id in PrestigeNativeBindings.ContractIds) yield return new object[] { id };
    }

    private PrestigeGameAction Boundary(Func<bool>? permit = null,
        Func<string, bool>? include = null) => new(
        () => _epoch,
        permit ?? (() => true),
        static () => "PrestigeLifecycle ownership was revoked.",
        name => name switch
        {
            "PersistentResetManager" => typeof(PersistentResetManager),
            "IntVariable" => typeof(IntVariable),
            "BoolVariable" => typeof(BoolVariable),
            _ => null,
        },
        include);

    private PrestigeSubmission Submit(PrestigeGameAction boundary) =>
        boundary.Submit(new PrestigeAction(_epoch));
}
