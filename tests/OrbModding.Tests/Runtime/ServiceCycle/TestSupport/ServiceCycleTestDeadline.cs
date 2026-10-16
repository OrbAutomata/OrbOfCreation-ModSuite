using System;
using System.Threading;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

/// <summary>
/// How long a test waits on another thread before calling the runtime wedged.
/// </summary>
/// <remarks>
/// <para>
/// Every wait built on this is a wait for a condition some other thread satisfies, so the deadline is
/// not part of what the test asserts — it is only how a wedged runtime fails loudly instead of hanging
/// the gate forever. A tight one asserts something no test meant to assert: that this machine was not
/// busy at that moment. Several were two seconds, and under a loaded gate they expired while the
/// worker was merely waiting for a core.
/// </para>
/// <para>
/// Generous, but still inside the gate's own deadline, so a genuinely stuck runtime
/// reports the named expectation it never reached rather than an anonymous gate timeout.
/// </para>
/// </remarks>
internal static class ServiceCycleTestDeadline
{
    internal static readonly TimeSpan Value = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Waits for a signal another thread sets, and names what was expected if it never comes.
    /// </summary>
    internal static void ForSignal(ManualResetEventSlim signal, string expectation)
    {
        if (!signal.Wait(Value))
            throw new TimeoutException($"The runtime never reached {expectation}.");
    }

    /// <summary>
    /// Waits for a condition another thread satisfies, and names what was expected if it never holds.
    /// </summary>
    internal static void For(Func<bool> condition, string expectation)
    {
        if (!SpinWait.SpinUntil(condition, Value))
            throw new TimeoutException($"The runtime never reached {expectation}.");
    }
}
