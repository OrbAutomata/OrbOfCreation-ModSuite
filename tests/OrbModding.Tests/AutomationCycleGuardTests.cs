using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomationCycleGuardTests : IDisposable
{
    private readonly List<string> _warnings = new();
    private readonly List<string> _faults = new();

    public AutomationCycleGuardTests() => GameActionFaultLog.ConfigureLog(_faults.Add);

    public void Dispose() => GameActionFaultLog.ConfigureLog(null);

    [Fact]
    public void ARepeatingFaultIsLoggedOnceUntilAnActionLands()
    {
        var guard = Guard();
        var fault = NativeActionOutcome.FaultBeforeCall(
            CommonActionResultCodes.AdapterFault,
            "The live queue contradicted the plan.");
        var landed = NativeActionOutcome.Commit(new NativeMutationCallOutcome(1, 1, 1), "Queued.");

        Assert.Equal(ServiceActionDisposition.Faulted, guard.Finish(in fault, "slot 2").Disposition);
        guard.Finish(in fault, "slot 2");
        Assert.Equal(ServiceActionDisposition.Committed, guard.Finish(in landed, "slot 2").Disposition);
        guard.Finish(in fault, "slot 2");

        Assert.Equal(
            new[]
            {
                "Test failed on slot 2: The live queue contradicted the plan.",
                "Test failed on slot 2: The live queue contradicted the plan.",
            },
            _warnings);
    }

    [Fact]
    public void RefusalsAndSkipsReachTheResultAndNeverTheLog()
    {
        var guard = Guard();
        var refusal = NativeActionOutcome.Reject(
            CommonActionResultCodes.NativeRejected,
            "The game refused the purchase.");
        var skip = NativeActionOutcome.Skip(CommonActionResultCodes.Skipped, "The plan raced the game.");

        var refused = guard.Finish(in refusal, "slot 1");
        var skipped = guard.Finish(in skip, "slot 1");

        Assert.Equal(ServiceActionDisposition.Rejected, refused.Disposition);
        Assert.Equal("The game refused the purchase.", refused.Reason);
        Assert.Equal(ServiceActionDisposition.Skipped, skipped.Disposition);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void AnExpectedThrowFaultsUnderAReferenceToItsLoggedCause()
    {
        var guard = Guard();

        var fault = guard.Fault(new InvalidOperationException("native list was null"), "slot 3");
        var result = guard.Finish(in fault, "slot 3");

        var logged = Assert.Single(_faults);
        Assert.Contains("native list was null", logged);
        var reference = " Reference " + logged.Split(' ')[3] + " is in the suite log.";
        Assert.Equal(CommonActionResultCodes.AdapterFault, result.Code);
        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal("Test faulted at the native boundary." + reference, result.Reason);
        Assert.Equal(
            "Test failed on slot 3: adapter fault (native list was null)." + reference,
            Assert.Single(_warnings));
    }

    [Fact]
    public void AnUnreadableEpochFaultsUnderAReferenceToItsLoggedCause()
    {
        var guard = Guard();

        Assert.False(guard.TryAdmitEpoch(
            static () => throw new InvalidOperationException("epoch read failed"), 7, "slot 1", out var refusal));

        var logged = Assert.Single(_faults);
        Assert.Contains("epoch read failed", logged);
        Assert.Equal(ServiceActionDisposition.Faulted, refusal.Disposition);
        Assert.Equal(
            "Test faulted at the native boundary. Reference " + logged.Split(' ')[3] + " is in the suite log.",
            refusal.Reason);
    }

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(7L, 8L)]
    public void AnUnstampedOrReplacedRunIsRefusedQuietly(long current, long planned)
    {
        var guard = Guard();

        Assert.False(guard.TryAdmitEpoch(() => current, planned, "slot 1", out var refusal));

        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, refusal.Code);
        Assert.Equal(ServiceActionDisposition.Rejected, refusal.Disposition);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void AnUnreadableLeaseIsALeaseTheSuiteDoesNotHold()
    {
        Assert.False(AutomationCycleGuard.Owns(static () => throw new InvalidOperationException("lease read failed")));
    }

    private AutomationCycleGuard<string> Guard() =>
        new("Test", static action => "Test failed on " + action, _warnings.Add);
}
