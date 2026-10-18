using System;
using System.Collections.Generic;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

[Trait("Category", "AutoBuyReliability")]
public sealed class NativeMultiBuyScopeTests : IDisposable
{
    public NativeMultiBuyScopeTests()
    {
        NativeMultiBuyScope.ResetQuarantineForTests();
        GlobalVariables.MultiBuy = new IntVariable { Value = 7 };
    }

    [Fact]
    public void SetterWritesThenThrows_RestoresOriginalAndDoesNotQuarantine()
    {
        GlobalVariables.MultiBuy.ThrowAfterWriteFor = 1;

        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out var reason));

        Assert.Contains("after write", reason, StringComparison.Ordinal);
        Assert.Contains("restoration to 7 verified", reason, StringComparison.Ordinal);
        Assert.Equal(7, GlobalVariables.MultiBuy.Value);
        Assert.False(NativeMultiBuyScope.IsMutationQuarantined);

        GlobalVariables.MultiBuy.ThrowAfterWriteFor = null;
        Assert.True(NativeMultiBuyScope.TryEnterOne(out var scope, out reason), reason);
        scope.Dispose();
        Assert.Equal(7, GlobalVariables.MultiBuy.Value);
    }

    [Fact]
    public void RestorationThrowsWithoutRestoring_QuarantinesFurtherMutation()
    {
        Assert.True(NativeMultiBuyScope.TryEnterOne(out var scope, out var reason), reason);
        Assert.Equal(1, GlobalVariables.MultiBuy.Value);
        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = 7;

        scope.Dispose();

        Assert.Equal(1, GlobalVariables.MultiBuy.Value);
        Assert.True(NativeMultiBuyScope.IsMutationQuarantined);
        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out reason));
        Assert.Contains("quarantined", reason, StringComparison.OrdinalIgnoreCase);
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
        Assert.False(NativeMultiBuyScope.IsMutationQuarantined);
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
        Assert.False(NativeMultiBuyScope.IsMutationQuarantined);
    }

    [Fact]
    public void AnUnexpectedThrowVerifyingTheWriteEscapesWithThePlayersValueRestored()
    {
        GlobalVariables.MultiBuy.ReadFailure = new NullReferenceException("injected read failure");
        GlobalVariables.MultiBuy.ReadFailureWhileValueIs = 1;

        Assert.Throws<NullReferenceException>(() => NativeMultiBuyScope.TryEnterOne(out _, out _));

        Assert.Equal(7, GlobalVariables.MultiBuy.Value);
        Assert.False(NativeMultiBuyScope.IsMutationQuarantined);
    }

    [Fact]
    public void AnUnexpectedThrowRestoringThePlayersValueEscapesAndQuarantines()
    {
        Assert.True(NativeMultiBuyScope.TryEnterOne(out var scope, out var reason), reason);
        GlobalVariables.MultiBuy.SetFailure = message => new NullReferenceException(message);
        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = 7;

        Assert.Throws<NullReferenceException>(scope.Dispose);

        Assert.Equal(1, GlobalVariables.MultiBuy.Value);
        Assert.True(NativeMultiBuyScope.IsMutationQuarantined);
        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out reason));
        Assert.Contains("quarantined", reason, StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<object[]> ContractIds() =>
        NativeMultiBuyBindings.ContractIds.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(ContractIds))]
    public void EveryMissingMemberDisablesTheCompleteSet(string missing)
    {
        NativeMultiBuyScope.ResetQuarantineForTests(includeContract: id => id != missing);

        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out var reason));

        Assert.Contains("multi-buy binding set is unavailable", reason, StringComparison.Ordinal);
        Assert.Contains(missing, reason, StringComparison.Ordinal);
        Assert.Equal(7, GlobalVariables.MultiBuy.Value);
        Assert.Equal(0, GlobalVariables.MultiBuy.SetCalls);
    }

    public void Dispose()
    {
        GlobalVariables.MultiBuy = new IntVariable();
        NativeMultiBuyScope.ResetQuarantineForTests();
    }
}
