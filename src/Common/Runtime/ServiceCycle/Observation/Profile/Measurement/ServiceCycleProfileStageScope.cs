#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal ref struct ServiceCycleProfileStageScope
{
    private IServiceCycleProfileMeasurementPort? _measurement;
    private ServiceCycleProfileProbe? _probe;
    private ServiceCycleProfileMeasurementToken _token;
    private ServiceCycleProfileOperationCounters _operations;

    internal ServiceCycleProfileStageScope(
        ServiceCycleProfileProbe probe,
        IServiceCycleProfileMeasurementPort measurement,
        in ServiceCycleProfileMeasurementToken token)
    {
        _probe = probe;
        _measurement = measurement;
        _token = token;
        _operations = default;
    }

    internal bool IsActive => _measurement is not null;

    internal void AddStableIdReads(ulong count = 1) =>
        _operations.AddStableIdReads(count);
    internal void AddListEntries(ulong count = 1) =>
        _operations.AddListEntries(count);

    internal ServiceCycleProfileMeasurementResult Complete()
    {
        var measurement = _measurement;
        if (measurement is null) return ServiceCycleProfileMeasurementResult.Accepted;
        var probe = _probe!;
        _measurement = null;
        _probe = null;
        var token = _token;
        _token = default;
        try
        {
            var result = measurement.Complete(in token, in _operations);
            if (result == ServiceCycleProfileMeasurementResult.Faulted)
                probe.Fail(ServiceCycleProfileProbeFault.MeasurementPortRejected);
            return result;
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            probe.Fail(ServiceCycleProfileProbeFault.MeasurementPortFailed, exception);
            return ServiceCycleProfileMeasurementResult.Faulted;
        }
    }

    internal void Abandon()
    {
        var measurement = _measurement;
        if (measurement is null) return;
        var probe = _probe!;
        _measurement = null;
        _probe = null;
        var token = _token;
        _token = default;
        try
        {
            if (measurement.Abandon(in token) == ServiceCycleProfileMeasurementResult.Faulted)
                probe.Fail(ServiceCycleProfileProbeFault.MeasurementPortRejected);
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            probe.Fail(ServiceCycleProfileProbeFault.MeasurementPortFailed, exception);
        }
    }
}
#endif
