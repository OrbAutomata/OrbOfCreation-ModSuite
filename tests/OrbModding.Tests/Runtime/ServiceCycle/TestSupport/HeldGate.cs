using OrbModding.TestSupport;
using System;
using System.Threading;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

/// <summary>
/// Proves a call does not wait for something another thread is holding, without timing it: the call
/// must return while the hold is still in place. If it blocks, the hang deadline releases the hold so
/// the test fails on a named assertion instead of wedging the gate.
/// </summary>
internal static class HeldGate
{
    internal static void AssertReturnsWhileHeld(Action call, Action releaseHold) =>
        AssertReturnsWhileHeld(
            () =>
            {
                call();
                return true;
            },
            releaseHold);

    internal static T AssertReturnsWhileHeld<T>(Func<T> call, Action releaseHold)
    {
        var released = 0;
        using var watchdog = new Timer(
            _ =>
            {
                if (Interlocked.Exchange(ref released, 1) == 0)
                    releaseHold();
            },
            null,
            ServiceCycleTestDeadline.Value,
            Timeout.InfiniteTimeSpan);
        var result = call();
        Assert.True(
            Interlocked.Exchange(ref released, 1) == 0,
            "The call waited for the held gate; only the hang deadline releasing it let the call return.");
        return result;
    }
}
