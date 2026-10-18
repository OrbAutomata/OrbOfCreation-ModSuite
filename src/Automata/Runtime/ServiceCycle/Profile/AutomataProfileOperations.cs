#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

namespace OrbAutomata.Runtime.ServiceCycle.Profile;

internal sealed class AutomataProfileOperations
{
    private readonly ServiceCycleProfileProbe _probe;
    private ulong _stableIdReads;
    private ulong _listEntries;
    private bool _active;
    private bool _failed;

    internal AutomataProfileOperations(ServiceCycleProfileProbe probe) =>
        _probe = probe ?? throw new System.ArgumentNullException(nameof(probe));

    internal AutomataProfileStageScope Begin(
        ServiceCycleProfileSpan span,
        in ServiceActionContext action,
        ServiceCycleProfileTemperature temperature)
    {
        var coordinates = action.ProfileCoordinates;
        return Begin(
            span,
            action.Cycle.Lifecycle.Value,
            action.Cycle.Cycle.Value,
            in coordinates,
            temperature);
    }

    private AutomataProfileStageScope Begin(
        ServiceCycleProfileSpan span,
        ulong lifecycle,
        ulong cycle,
        in ServiceCycleProfileCoordinates coordinates,
        ServiceCycleProfileTemperature temperature)
    {
        if (_active)
        {
            _failed = true;
            _probe.Fail(ServiceCycleProfileProbeFault.StageOverlapRejected);
            return default;
        }
        if (!coordinates.TryCreateContext(
                span,
                lifecycle,
                cycle,
                temperature,
                out var context))
        {
            _probe.Fail(ServiceCycleProfileProbeFault.ContextRejected);
            return default;
        }
        var measurement = _probe.Begin(in context);
        if (!measurement.IsActive)
        {
            measurement.Abandon();
            return default;
        }
        Reset();
        _active = true;
        return new AutomataProfileStageScope(this, in measurement);
    }

    internal void AddStableIdRead() => Add(ref _stableIdReads);
    internal void AddListEntry() => Add(ref _listEntries);

    internal void Complete(ref ServiceCycleProfileStageScope measurement)
    {
        if (!_active)
        {
            measurement.Abandon();
            return;
        }
        _active = false;
        if (_failed)
        {
            measurement.Abandon();
            return;
        }
        measurement.AddStableIdReads(_stableIdReads);
        measurement.AddListEntries(_listEntries);
        measurement.Complete();
    }

    internal void Abandon(ref ServiceCycleProfileStageScope measurement)
    {
        if (_active) _active = false;
        measurement.Abandon();
    }

    private void Reset()
    {
        _stableIdReads = 0;
        _listEntries = 0;
        _failed = false;
    }

    private void Add(ref ulong value)
    {
        if (!_active || _failed) return;
        value++;
    }
}

internal ref struct AutomataProfileStageScope
{
    private AutomataProfileOperations? _operations;
    private ServiceCycleProfileStageScope _measurement;

    internal AutomataProfileStageScope(
        AutomataProfileOperations operations,
        in ServiceCycleProfileStageScope measurement)
    {
        _operations = operations;
        _measurement = measurement;
    }

    internal bool IsActive => _operations is not null;

    internal void Complete()
    {
        var operations = _operations;
        if (operations is null) return;
        _operations = null;
        operations.Complete(ref _measurement);
    }

    internal void Abandon()
    {
        var operations = _operations;
        if (operations is null) return;
        _operations = null;
        operations.Abandon(ref _measurement);
    }
}
#endif
