using System;
using System.Threading;
using System.Diagnostics;

namespace OrbModding.TestSupport;

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
        if (!PollUntil(condition))
            throw new TimeoutException($"The runtime never reached {expectation}.");
    }

    internal static bool PollUntil(Func<bool> condition)
    {
        var budget = new Budget();
        while (!condition())
        {
            if (budget.Expired) return condition();
            Thread.Sleep(1);
        }
        return true;
    }

    internal sealed class Budget
    {
        private readonly long _end = Stopwatch.GetTimestamp()
            + (long)(Value.TotalSeconds * Stopwatch.Frequency);

        internal bool Expired => Stopwatch.GetTimestamp() >= _end;
        internal TimeSpan Remaining
        {
            get
            {
                var ticks = _end - Stopwatch.GetTimestamp();
                if (ticks <= 0) throw new TimeoutException("The test's shared wait budget expired.");
                return TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
            }
        }

        internal void For(Func<bool> condition, string expectation)
        {
            while (!condition())
            {
                if (Expired) throw new TimeoutException($"The runtime never reached {expectation}.");
                Thread.Sleep(1);
            }
        }
    }
}
