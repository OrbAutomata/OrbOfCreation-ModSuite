#if SERVICE_CYCLE_PROFILE
using System;
using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal sealed class ServiceCycleProfileProbe
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private IServiceCycleProfileMeasurementPort? _measurement;
    private int _fault;
    private Exception? _faultException;

    internal bool IsAttached => _measurement is not null;
    internal ServiceCycleProfileProbeFault Fault =>
        (ServiceCycleProfileProbeFault)Volatile.Read(ref _fault);

    /// <summary>What the measurement port threw, when a throw is what faulted the probe.</summary>
    internal Exception? FaultException => Volatile.Read(ref _faultException);

    internal void Attach(IServiceCycleProfileMeasurementPort measurement)
    {
        AssertOwnerThread();
        if (measurement is null) throw new ArgumentNullException(nameof(measurement));
        if (_measurement is not null)
            throw new InvalidOperationException("A profile recorder is already attached.");
        if (Fault != ServiceCycleProfileProbeFault.None)
            throw new InvalidOperationException("A faulted profile probe cannot be reused.");
        _measurement = measurement;
    }

    internal IServiceCycleProfileMeasurementPort Detach()
    {
        AssertOwnerThread();
        var measurement = _measurement ??
            throw new InvalidOperationException("No profile recorder is attached.");
        _measurement = null;
        return measurement;
    }

    internal ServiceCycleProfileStageScope Begin(in ServiceCycleProfileContext context)
    {
        var measurement = _measurement;
        if (measurement is null || Fault != ServiceCycleProfileProbeFault.None) return default;
        try
        {
            if (measurement.TryBegin(in context, out var token))
                return new ServiceCycleProfileStageScope(this, measurement, in token);
            Fail(ServiceCycleProfileProbeFault.MeasurementPortRejected);
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Fail(ServiceCycleProfileProbeFault.MeasurementPortFailed, exception);
        }
        return default;
    }

    internal void Fail(ServiceCycleProfileProbeFault fault, Exception? exception = null)
    {
        if (fault == ServiceCycleProfileProbeFault.None)
            throw new ArgumentOutOfRangeException(nameof(fault));
        if (Interlocked.CompareExchange(
                ref _fault,
                (int)fault,
                (int)ServiceCycleProfileProbeFault.None) == (int)ServiceCycleProfileProbeFault.None &&
            exception is not null)
            Volatile.Write(ref _faultException, exception);
    }

    private void AssertOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The service-cycle profile probe is owner-thread affine.");
    }

    private static bool IsProcessFatal(Exception exception) =>
        exception is StackOverflowException or OutOfMemoryException or AccessViolationException;
}
#endif
