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
/// and it costs one window. Any other number has to be measured twice before it is reported: the
/// probe opens further windows, up to <see cref="MaximumWindows"/>, and reports the first count two
/// of them agree on. A disturbance lands on one window and not the next, so a second window is
/// usually the whole story and a third settles the case where the disturbance landed on the first
/// pair; when no two agree the probe throws
/// <see cref="AllocationProbeDisturbedWindowException"/> and reports no number at all. Every window
/// can only turn a byte count into a probe failure or confirm it, never turn a failure into a pass,
/// which is what separates a confirmation from measuring again until the answer is liked.
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
    /// <summary>
    /// How many windows one measurement may open before the probe gives up on the number.
    /// </summary>
    internal const int MaximumWindows = 4;

    internal static long MeasureRepeated(int iterations, Action work, Action? prepare = null)
    {
        if (iterations <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(iterations), iterations, "An allocation probe measures at least one iteration.");
        }

        if (work is null) throw new ArgumentNullException(nameof(work));

        var windows = new long[MaximumWindows];

        prepare?.Invoke();
        Drive(iterations, work);

        windows[0] = MeasureWindow(iterations, work, prepare);
        if (windows[0] == 0) return 0;

        for (var index = 1; index < windows.Length; index++)
        {
            windows[index] = MeasureWindow(iterations, work, prepare);
            for (var earlier = 0; earlier < index; earlier++)
                if (windows[earlier] == windows[index]) return windows[index];
        }

        throw new AllocationProbeDisturbedWindowException(windows);
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
