using System;
using System.Runtime.CompilerServices;

namespace OrbModding.TestSupport;

/// <summary>
/// Reports what a repeated path allocates on the calling thread, and refuses to report a byte count
/// it could not measure twice.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GC.GetAllocatedBytesForCurrentThread"/> is exact for what this thread allocates, but a
/// window held open under load can gain bytes the measured code never allocated: while other threads
/// allocate and the heap is still growing, the runtime charges this thread the unused remainder of
/// its own allocation quantum. Every such charge observed on this runtime was positive, under 8,192
/// bytes and a multiple of eight. The bytes were never the measured code's, so no warm-up removes
/// them, and the number is not evidence about the code at all.
/// </para>
/// <para>
/// On-stack-replacement compilation is the obvious suspect for a loop-carrying <see cref="Drive"/>
/// and it is not the cause: an OSR compile forced inside the bracket, confirmed by the JIT's own
/// compilation summary, measured exactly zero, as did the first-call compile of a loop-free method.
/// The JIT allocates from native arenas and never reaches this counter.
/// </para>
/// <para>
/// The disturbance only ever adds, so a window measuring zero is proof the code allocated nothing,
/// and it costs one window. Any other number is measured a second time and the two windows have to
/// agree before either is reported; when they disagree the probe throws
/// <see cref="AllocationProbeDisturbedWindowException"/>. The second window can only turn a byte
/// count into a probe failure, never a failure into a pass, which is what separates a confirmation
/// from measuring again until the answer is liked.
/// </para>
/// <para>
/// Every pass goes through the same <see cref="Drive"/> loop and the same delegate, so the unmeasured
/// pass leaves nothing for the measured one to compile. The preparation delegate re-arms state a pass
/// consumes and runs outside the bracket, so what it costs is never attributed to the work.
/// </para>
/// <para>
/// The probe returns bytes and nothing else. It holds no assertion, so it cannot soften one, and it
/// runs the work a fixed number of times per pass.
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

        var measured = MeasureWindow(iterations, work, prepare);
        if (measured == 0) return 0;

        var confirmation = MeasureWindow(iterations, work, prepare);
        if (confirmation != measured)
        {
            throw new AllocationProbeDisturbedWindowException(measured, confirmation);
        }

        return measured;
    }

    private static long MeasureWindow(int iterations, Action work, Action? prepare)
    {
        prepare?.Invoke();
        var before = GC.GetAllocatedBytesForCurrentThread();
        Drive(iterations, work);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <remarks>
    /// Kept out of line so every pass shares one compiled loop instead of inlined copies, each with a
    /// patchpoint of its own.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Drive(int iterations, Action work)
    {
        for (var index = 0; index < iterations; index++)
            work();
    }
}
