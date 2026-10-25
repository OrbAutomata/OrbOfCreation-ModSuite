using OrbModding.TestSupport;
using System;
using System.Reflection;
using System.Threading;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal sealed class HandoffGateContention : IDisposable
{
    private readonly object _gate;
    private readonly ManualResetEventSlim _request = new(false);
    private readonly ManualResetEventSlim _entered = new(false);
    private readonly ManualResetEventSlim _release = new(false);
    private readonly Thread _thread;

    internal HandoffGateContention(
        ServiceRunner<ExecutionState, ExecutionAction> runner)
        : this((object)runner) { }

    internal HandoffGateContention(object runner)
    {
        if (runner is null) throw new ArgumentNullException(nameof(runner));
        var handoff = runner.GetType()
            .GetField("_handoff", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(runner)!;
        _gate = handoff.GetType()
            .GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(handoff)!;
        _thread = new Thread(HoldGate)
        {
            IsBackground = true,
            Name = "ServiceCycle.HandoffContention",
        };
        _thread.Start();
    }

    internal void Acquire()
    {
        _request.Set();
        if (!_entered.Wait(ServiceCycleTestDeadline.Value))
            throw new TimeoutException("The contention thread did not acquire the handoff gate.");
    }

    internal void Release()
    {
        _release.Set();
        if (!_thread.Join(ServiceCycleTestDeadline.Value))
            throw new TimeoutException("The contention thread did not release the handoff gate.");
    }

    public void Dispose()
    {
        _release.Set();
        _request.Set();
        _thread.Join(ServiceCycleTestDeadline.Value);
        _request.Dispose();
        _entered.Dispose();
        _release.Dispose();
    }

    private void HoldGate()
    {
        _request.Wait();
        lock (_gate)
        {
            _entered.Set();
            _release.Wait();
        }
    }
}
