using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using Xunit;

namespace OrbModding.Tests;

[Trait("Category", "AutoBuyReliability")]
[Collection(GameActionFaultLogCollection.Name)]
public sealed class NativeMultiBuyScopeTests : IDisposable
{
    private static readonly MonotonicDuration FirstRetry = MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1));
    private static readonly MonotonicDuration OneTick = new(1);
    private readonly VirtualMonotonicClock _clock = new();
    private readonly List<string> _logged = new();
    private readonly List<string> _faults = new();

    public NativeMultiBuyScopeTests()
    {
        NativeMultiBuyScope.ResetForTests(clock: _clock);
        GlobalVariables.MultiBuy = new IntVariable { Value = 7 };
    }

    [Fact]
    public void SetterWritesThenThrows_RestoresOriginalAndLetsTheNextEntryIn()
    {
        GlobalVariables.MultiBuy.ThrowAfterWriteFor = 1;

        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out var reason));

        Assert.Contains("SetValue(1) threw", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("after write", reason, StringComparison.Ordinal);
        Assert.Contains("restoration to 7 verified", reason, StringComparison.Ordinal);
        Assert.Equal(7, GlobalVariables.MultiBuy.Value);

        GlobalVariables.MultiBuy.ThrowAfterWriteFor = null;
        Assert.True(NativeMultiBuyScope.TryEnterOne(out var scope, out reason), reason);
        scope.Dispose();
        Assert.Equal(7, GlobalVariables.MultiBuy.Value);
    }

    [Fact]
    public void ARefusedEntryIsLoggedOnceWhileItKeepsRepeating()
    {
        NativeMultiBuyScope.ConfigureFailureLog(_logged.Add);
        GlobalVariables.MultiBuy.ThrowAfterWriteFor = 1;

        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out var reason));
        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out _));

        Assert.Equal(reason, Assert.Single(_logged));
    }

    [Fact]
    public void AGameThrownTargetInvocationExceptionIsNotAnExpectedFailureAndEscapes()
    {
        GlobalVariables.MultiBuy.SetFailure = message =>
            new TargetInvocationException(new InvalidOperationException(message));
        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = 1;

        Assert.Throws<TargetInvocationException>(() => NativeMultiBuyScope.TryEnterOne(out _, out _));

        Assert.Equal(7, GlobalVariables.MultiBuy.Value);
        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = null;
        AssertEntersAndRestores(7);
    }

    [Fact]
    public void RestorationThrowsWithoutRestoring_HoldsEntriesWhileTheRestoreIsOutstanding()
    {
        FailTheRestore();

        Assert.Equal(1, GlobalVariables.MultiBuy.Value);
        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out var reason));
        Assert.Contains("restoration to 7 is outstanding", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("before write", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ExceptionInsideSuccessfulScope_RestoresOriginalValue()
    {
        Assert.True(NativeMultiBuyScope.TryEnterOne(out var scope, out var reason), reason);

        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using (scope)
            {
                Assert.Equal(1, GlobalVariables.MultiBuy.Value);
                throw new InvalidOperationException("simulated purchase failure");
            }
        }));

        Assert.Equal(7, GlobalVariables.MultiBuy.Value);
        AssertEntersAndRestores(7);
    }

    [Fact]
    public void EveryEntryWritesAndRestoresTheVariableTheGameHandsOutNow()
    {
        var first = GlobalVariables.MultiBuy;
        Assert.True(NativeMultiBuyScope.TryEnterOne(out var firstScope, out var reason), reason);
        firstScope.Dispose();

        var second = new IntVariable { Value = 9 };
        GlobalVariables.MultiBuy = second;
        Assert.True(NativeMultiBuyScope.TryEnterOne(out var secondScope, out reason), reason);
        Assert.Equal(7, first.Value);
        Assert.Equal(1, second.Value);
        secondScope.Dispose();

        Assert.Equal(9, second.Value);
        Assert.Equal(7, first.Value);
    }

    [Fact]
    public void AnUnexpectedSetterThrowAfterTheWriteEscapesWithThePlayersValueRestored()
    {
        GlobalVariables.MultiBuy.SetFailure = message => new NullReferenceException(message);
        GlobalVariables.MultiBuy.ThrowAfterWriteFor = 1;

        Assert.Throws<NullReferenceException>(() => NativeMultiBuyScope.TryEnterOne(out _, out _));

        Assert.Equal(7, GlobalVariables.MultiBuy.Value);
        GlobalVariables.MultiBuy.ThrowAfterWriteFor = null;
        AssertEntersAndRestores(7);
    }

    [Fact]
    public void AnUnexpectedThrowVerifyingTheWriteEscapesWithThePlayersValueRestored()
    {
        GlobalVariables.MultiBuy.ReadFailure = new NullReferenceException("injected read failure");
        GlobalVariables.MultiBuy.ReadFailureWhileValueIs = 1;

        Assert.Throws<NullReferenceException>(() => NativeMultiBuyScope.TryEnterOne(out _, out _));

        Assert.Equal(7, GlobalVariables.MultiBuy.Value);
        GlobalVariables.MultiBuy.ReadFailure = null;
        AssertEntersAndRestores(7);
    }

    [Fact]
    public void AnUnexpectedThrowRestoringThePlayersValueEscapesAndLeavesTheRestoreOutstanding()
    {
        Assert.True(NativeMultiBuyScope.TryEnterOne(out var scope, out var reason), reason);
        GlobalVariables.MultiBuy.SetFailure = message => new NullReferenceException(message);
        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = 7;

        Assert.Throws<NullReferenceException>(scope.Dispose);

        Assert.Equal(1, GlobalVariables.MultiBuy.Value);
        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out reason));
        Assert.Contains("restoration to 7 is outstanding", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOutstandingRestoreNeverTakesTheLeftoverAsThePlayersValue()
    {
        FailTheRestore();
        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = null;
        var setCalls = GlobalVariables.MultiBuy.SetCalls;

        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out _));
        Assert.False(NativeMultiBuyScope.TryEnter(3, out _, out _));

        Assert.Equal(setCalls, GlobalVariables.MultiBuy.SetCalls);
        Assert.Equal(1, GlobalVariables.MultiBuy.Value);
        _clock.Advance(FirstRetry);
        AssertEntersAndRestores(7);
    }

    [Fact]
    public void ARetryThatComesDueRestoresThePlayersValueAndEntriesProceedAgain()
    {
        NativeMultiBuyScope.ConfigureFailureLog(_logged.Add);
        FailTheRestore();
        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = null;
        _clock.Advance(FirstRetry);

        Assert.True(NativeMultiBuyScope.TryEnter(3, out var scope, out var reason), reason);

        Assert.Equal(3, GlobalVariables.MultiBuy.Value);
        scope.Dispose();
        Assert.Equal(7, GlobalVariables.MultiBuy.Value);
        Assert.Contains("restoration to 7 verified", _logged.Last(), StringComparison.Ordinal);
        AssertEntersAndRestores(7);
        AssertEntersAndRestores(7);
    }

    [Fact]
    public void ARetryThatFailsAgainStaysOutstandingWarnsAgainAndWaitsLonger()
    {
        NativeMultiBuyScope.ConfigureFailureLog(_logged.Add);
        FailTheRestore();
        Assert.Single(_logged);

        _clock.Advance(FirstRetry);
        var setCalls = GlobalVariables.MultiBuy.SetCalls;
        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out var reason));

        Assert.Contains("restoration to 7 is outstanding", reason, StringComparison.Ordinal);
        Assert.Equal(setCalls + 1, GlobalVariables.MultiBuy.SetCalls);
        Assert.Equal(2, _logged.Count);
        Assert.Contains("attempt 2", _logged[1], StringComparison.Ordinal);

        _clock.Advance(FirstRetry);
        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out _));
        Assert.Equal(setCalls + 1, GlobalVariables.MultiBuy.SetCalls);
        Assert.Equal(2, _logged.Count);

        _clock.Advance(FirstRetry);
        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out _));
        Assert.Equal(setCalls + 2, GlobalVariables.MultiBuy.SetCalls);
        Assert.Equal(3, _logged.Count);
        Assert.Equal(1, GlobalVariables.MultiBuy.Value);
    }

    [Fact]
    public void EntriesBeforeTheRetryIsDueTouchNothingAndDoNotPushItBack()
    {
        NativeMultiBuyScope.ConfigureFailureLog(_logged.Add);
        FailTheRestore();
        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = null;
        var setCalls = GlobalVariables.MultiBuy.SetCalls;

        _clock.Advance(FirstRetry - OneTick);
        for (var entry = 0; entry < 3; entry++)
            Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out _));

        Assert.Equal(setCalls, GlobalVariables.MultiBuy.SetCalls);
        Assert.Single(_logged);
        _clock.Advance(OneTick);
        AssertEntersAndRestores(7);
    }

    [Fact]
    public void AnUnexpectedThrowDuringARetryEscapesAndTheRetryStillWaits()
    {
        FailTheRestore();
        GlobalVariables.MultiBuy.SetFailure = message => new NullReferenceException(message);
        _clock.Advance(FirstRetry);

        Assert.Throws<NullReferenceException>(() => NativeMultiBuyScope.TryEnterOne(out _, out _));

        var setCalls = GlobalVariables.MultiBuy.SetCalls;
        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out var reason));
        Assert.Contains("restoration to 7 is outstanding", reason, StringComparison.Ordinal);
        Assert.Equal(setCalls, GlobalVariables.MultiBuy.SetCalls);
    }

    [Fact]
    public void AValueThePlayerSetWhileARestoreIsOutstandingStands()
    {
        FailTheRestore();
        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = null;
        GlobalVariables.MultiBuy.Value = 4;
        _clock.Advance(FirstRetry);

        AssertEntersAndRestores(4);
    }

    [Fact]
    public void AnEntrySetterThrowWhoseRestoreFailsLogsTheEntrysOwnException()
    {
        LogFaults();
        GlobalVariables.MultiBuy.ThrowAfterWriteFor = 1;
        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = 7;

        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out _));

        var fault = FaultNamedBy(Assert.Single(_logged));
        Assert.Contains("setter rejected 1 after write", fault, StringComparison.Ordinal);
        Assert.Contains("setter rejected 7 before write", fault, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedEntryWhoseRestoreRecoveredLogsTheRestoreSettersException()
    {
        LogFaults();
        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = 1;
        GlobalVariables.MultiBuy.ThrowAfterWriteFor = 7;

        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out _));

        Assert.Equal(7, GlobalVariables.MultiBuy.Value);
        var fault = FaultNamedBy(Assert.Single(_logged));
        Assert.Contains("setter rejected 1 before write", fault, StringComparison.Ordinal);
        Assert.Contains("setter rejected 7 after write", fault, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnexpectedEntryThrowWhoseRestoreRecoveredLogsTheRestoreSettersException()
    {
        LogFaults();
        GlobalVariables.MultiBuy.SetFailure = message => message.Contains(" 1 ", StringComparison.Ordinal)
            ? new NullReferenceException(message)
            : new InvalidOperationException(message);
        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = 1;
        GlobalVariables.MultiBuy.ThrowAfterWriteFor = 7;

        Assert.Throws<NullReferenceException>(() => NativeMultiBuyScope.TryEnterOne(out _, out _));

        Assert.Equal(7, GlobalVariables.MultiBuy.Value);
        var fault = FaultNamedBy(Assert.Single(_logged));
        Assert.Contains("setter rejected 7 after write", fault, StringComparison.Ordinal);
    }

    [Fact]
    public void ARetryWhoseSetterThrewButVerifiedLogsThatException()
    {
        FailTheRestore();
        LogFaults();
        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = null;
        GlobalVariables.MultiBuy.ThrowAfterWriteFor = 7;
        _clock.Advance(FirstRetry);

        Assert.True(NativeMultiBuyScope.TryEnterOne(out var scope, out var reason), reason);
        GlobalVariables.MultiBuy.ThrowAfterWriteFor = null;
        scope.Dispose();

        var retried = Assert.Single(_logged);
        Assert.Contains("restoration setter threw but value 7 was verified", retried, StringComparison.Ordinal);
        Assert.Contains("setter rejected 7 after write", FaultNamedBy(retried), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnexpectedThrowReadingTheSettingForARetryEscapesAndTheRetryStillWaits()
    {
        FailTheRestore();
        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = null;
        GlobalVariables.MultiBuy.ReadFailure = new NullReferenceException("injected read failure");
        GlobalVariables.MultiBuy.ReadFailureWhileValueIs = 1;
        _clock.Advance(FirstRetry);

        Assert.Throws<NullReferenceException>(() => NativeMultiBuyScope.TryEnterOne(out _, out _));

        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out var reason));
        Assert.Contains("restoration to 7 is outstanding", reason, StringComparison.Ordinal);
        GlobalVariables.MultiBuy.ReadFailure = null;
        _clock.Advance(FirstRetry);
        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out _));
        _clock.Advance(FirstRetry);
        AssertEntersAndRestores(7);
    }

    public static IEnumerable<object[]> ContractIds() =>
        NativeMultiBuyBindings.ContractIds.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(ContractIds))]
    public void EveryMissingMemberDisablesTheCompleteSet(string missing)
    {
        NativeMultiBuyScope.ResetForTests(includeContract: id => id != missing, clock: _clock);

        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out var reason));

        Assert.Contains("multi-buy binding set is unavailable", reason, StringComparison.Ordinal);
        Assert.Contains(missing, reason, StringComparison.Ordinal);
        Assert.Equal(7, GlobalVariables.MultiBuy.Value);
        Assert.Equal(0, GlobalVariables.MultiBuy.SetCalls);
    }

    public void Dispose()
    {
        GameActionFaultLog.ConfigureLog(null);
        NativeMultiBuyScope.ConfigureFailureLog(null);
        GlobalVariables.MultiBuy = new IntVariable();
        NativeMultiBuyScope.ResetForTests();
    }

    private void LogFaults()
    {
        _logged.Clear();
        NativeMultiBuyScope.ConfigureFailureLog(_logged.Add);
        GameActionFaultLog.ConfigureLog(_faults.Add);
    }

    private string FaultNamedBy(string line)
    {
        var reference = Regex.Match(line, "MCP-[0-9A-F]{8}");
        Assert.True(reference.Success, line);
        return Assert.Single(_faults, fault => fault.Contains(reference.Value, StringComparison.Ordinal));
    }

    private static void FailTheRestore()
    {
        Assert.True(NativeMultiBuyScope.TryEnterOne(out var scope, out var reason), reason);
        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = 7;
        scope.Dispose();
        Assert.Equal(1, GlobalVariables.MultiBuy.Value);
    }

    private static void AssertEntersAndRestores(int playerValue)
    {
        Assert.True(NativeMultiBuyScope.TryEnterOne(out var scope, out var reason), reason);
        Assert.Equal(1, GlobalVariables.MultiBuy.Value);
        scope.Dispose();
        Assert.Equal(playerValue, GlobalVariables.MultiBuy.Value);
    }
}
