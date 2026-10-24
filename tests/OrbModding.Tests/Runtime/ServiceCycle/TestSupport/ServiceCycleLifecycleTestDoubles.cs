using OrbModding.TestSupport;
using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal sealed class CountingThreadStarter : IServiceCycleWorkerStarter
{
    private int _attemptCount;
    internal int AttemptCount => Volatile.Read(ref _attemptCount);

    public void Start(Thread thread)
    {
        Interlocked.Increment(ref _attemptCount);
        thread.Start();
    }
}
internal sealed class HoldWorkerExitObserver : IServiceCycleWorkerExitObserver, IDisposable
{
    private int _enteredCount;
    internal ManualResetEventSlim Release { get; } = new(false);

    public void OnWorkerExitPrepared()
    {
        Interlocked.Increment(ref _enteredCount);
        Release.Wait();
    }

    internal bool WaitForCount(int count) => SpinWait.SpinUntil(
        () => Volatile.Read(ref _enteredCount) >= count,
        ServiceCycleTestDeadline.Value);

    public void Dispose()
    {
        Release.Set();
        Release.Dispose();
    }
}
