using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed class SuiteFramePumpJournalSession
{
    private readonly ServiceCycleRegistry _registry;
    private readonly int _serviceCapacity;
    private readonly ServiceCycleDecisionJournalObserver? _outcomeObserver;
    private readonly ServiceCycleDecisionJournalObserverFanout? _fanout;
    private readonly ServiceActionTimelineRegistration? _outcomeRegistration;
    private IServiceCycleDecisionJournalObserver? _observer;
    private object? _runtimeOwner;

    internal SuiteFramePumpJournalSession(
        ServiceCycleRegistry registry,
        int serviceCapacity,
        ServiceActionTimelineRegistry? actionTimeline)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _serviceCapacity = serviceCapacity;
        if (actionTimeline is null) return;

        var projection = ServiceActionTimelineProjection.Create(registry);
        ServiceActionTimelineRegistration? registration = null;
        try
        {
            registration = actionTimeline.Register(projection);
            var observer = new ServiceCycleDecisionJournalObserver(projection, serviceCapacity);
            var baselines = new DecisionJournalServiceBaseline[serviceCapacity];
            if (!ServiceCycleDecisionJournalBinder.TryBind(
                    registry,
                    serviceCapacity,
                    observer,
                    baselines))
            {
                throw new InvalidOperationException(
                    "The always-on action timeline could not bind the settled service roster.");
            }
            _outcomeObserver = observer;
            _fanout = new ServiceCycleDecisionJournalObserverFanout(observer);
            _outcomeRegistration = registration;
        }
        catch
        {
            registration?.Dispose();
            throw;
        }
    }

    internal IServiceCycleDecisionJournalObserver? Observer => _fanout ?? _observer;

    internal bool TryAttach(
        IServiceCycleDecisionJournalObserver observer,
        DecisionJournalServiceBaseline[] baselines,
        bool emergencyEngaged)
    {
        if (_observer is not null)
            throw new InvalidOperationException("A decision journal is already attached.");
        if (emergencyEngaged) return false;
        if (!ServiceCycleDecisionJournalBinder.TryBind(
                _registry,
                _serviceCapacity,
                observer,
                baselines)) return false;
        _observer = observer;
        _fanout?.Attach(observer);
        return true;
    }

    internal void Detach(IServiceCycleDecisionJournalObserver observer)
    {
        if (!ReferenceEquals(_observer, observer))
            throw new InvalidOperationException("The requested decision journal is not attached.");
        _fanout?.Detach(observer);
        _observer = null;
    }

    internal void Dispose(MonotonicTimestamp observedAt)
    {
        if (_observer is not null)
            throw new InvalidOperationException(
                "An attached decision journal must stop before the action timeline.");
        try { _outcomeObserver?.Stop(observedAt); }
        finally { _outcomeRegistration?.Dispose(); }
    }

    internal void ClaimRuntime(object owner)
    {
        if (owner is null) throw new ArgumentNullException(nameof(owner));
        if (_runtimeOwner is not null)
            throw new InvalidOperationException("A decision-journal runtime already owns this pump.");
        _runtimeOwner = owner;
    }

    internal void ReleaseRuntime(object owner)
    {
        EnsureOwner(owner);
        if (_observer is not null)
            throw new InvalidOperationException("An attached decision journal cannot release runtime ownership.");
        _runtimeOwner = null;
    }

    internal void ValidateOwnedPumpDisposal(object owner)
    {
        EnsureOwner(owner);
        if (_observer is not null)
            throw new InvalidOperationException("An attached decision journal cannot dispose the frame pump.");
    }

    internal void CompleteOwnedPumpDisposal(object owner)
    {
        EnsureOwner(owner);
        _runtimeOwner = null;
    }

    internal void EnsureUnowned()
    {
        if (_runtimeOwner is not null)
            throw new InvalidOperationException(
                "The decision-journal runtime must reach terminal state before the frame pump is disposed.");
    }

    private void EnsureOwner(object owner)
    {
        if (!ReferenceEquals(_runtimeOwner, owner))
            throw new InvalidOperationException("The requested runtime does not own this pump.");
    }
}
