using System;
using System.Runtime.CompilerServices;

namespace OrbModding.TestSupport;

/// <summary>
/// Reports what a repeated path allocates on the calling thread, measured after the runtime has
/// finished compiling both the path and the loop that drives it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GC.GetAllocatedBytesForCurrentThread"/> is exact, but a method that runs a hot loop is
/// still being compiled while it runs: the runtime builds an on-stack-replacement body for the loop
/// on this very thread and charges the bytes here. Bracketing the first run of a loop therefore
/// measures the compiler rather than the code, which is why identical code has measured zero on one
/// run and thousands of bytes on the next.
/// </para>
/// <para>
/// Both passes go through the same <see cref="Drive"/> loop and the same delegate, so the unmeasured
/// pass leaves nothing for the measured one to compile. <paramref name="prepare"/> re-arms state a
/// pass consumes and runs outside the bracket, so what it costs is never attributed to the work.
/// </para>
/// <para>
/// The probe returns bytes and nothing else. It holds no assertion, so it cannot soften one, and it
/// runs the work a fixed number of times, so it cannot quietly measure again until it likes the
/// answer.
/// </para>
/// </remarks>
internal static class AllocationProbe
{
    internal static long MeasureRepeated(int iterations, Action work, Action? prepare = null)
    {
        if (iterations <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(iterations), iterations, "An allocation probe measures at least one iteration.");
        }

        if (work is null) throw new ArgumentNullException(nameof(work));

        prepare?.Invoke();
        Drive(iterations, work);

        prepare?.Invoke();
        var before = GC.GetAllocatedBytesForCurrentThread();
        Drive(iterations, work);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <remarks>
    /// Kept out of line so the warm-up pass and the measured pass share one compiled loop instead of
    /// two inlined copies, each with a patchpoint of its own.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Drive(int iterations, Action work)
    {
        for (var index = 0; index < iterations; index++)
            work();
    }
}
