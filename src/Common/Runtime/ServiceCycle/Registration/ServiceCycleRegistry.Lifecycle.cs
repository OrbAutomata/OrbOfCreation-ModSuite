using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.World;
using RuntimeLifecycleGeneration = OrbModding.Common.Runtime.LifecycleGeneration;

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

public sealed partial class ServiceCycleRegistry
{
    internal bool RequestLifecycle(RuntimeLifecycleGeneration generation)
    {
        AssertOwnerThread();
        ThrowIfDisposed();
        ThrowIfRunnerConstruction();
        if (generation.Value == 0)
            throw new ArgumentException("A valid lifecycle generation is required.", nameof(generation));
        if (!_hasLifecycle)
        {
            _lifecycle = generation;
            _hasLifecycle = true;
        }
        else if (generation.Value <= _lifecycle.Value)
        {
            return false;
        }
        else
        {
            _lifecycle = generation;
        }

        // The world is the one publication that outlives its own subject. Slot states are recreated
        // per lifecycle and so cannot carry a dead run forward; the world snapshot is a plain
        // immutable object and would keep answering for a save that no longer exists. Flushing it
        // here rather than at any caller means every accepted lifecycle replacement — scene change,
        // save load, reset, NG+ — trashes it, and none of them can forget to.
        _world.Flush(GameWorldStateDefaults.Empty);
        for (var ordinal = 0; ordinal < _nextOrdinal; ordinal++)
            _slots[ordinal].RequestLifecycle(generation);
        return true;
    }

    internal int ReconcileLifecycle(MonotonicTimestamp now)
        => ReconcileLifecycle(now, NextLifecycleReconciliationEpoch());

    internal long NextLifecycleReconciliationEpoch()
    {
        AssertOwnerThread();
        ThrowIfDisposed();
        ThrowIfRunnerConstruction();
        return checked(++_nextLifecycleReconciliationEpoch);
    }

    internal int ReconcileLifecycle(MonotonicTimestamp now, long reconciliationEpoch)
    {
        AssertOwnerThread();
        ThrowIfDisposed();
        if (reconciliationEpoch <= 0)
            throw new ArgumentOutOfRangeException(nameof(reconciliationEpoch));
        if (_reconcilingLifecycle || _constructingRunner)
            throw new InvalidOperationException("Lifecycle reconciliation cannot be entered recursively.");
        _reconcilingLifecycle = true;
        try
        {
            var changed = 0;
            for (var ordinal = 0; ordinal < _nextOrdinal; ordinal++)
            {
                if (_slots[ordinal].ReconcileLifecycle(now, reconciliationEpoch)) changed++;
            }
            return changed;
        }
        finally
        {
            _reconcilingLifecycle = false;
        }
    }
}
