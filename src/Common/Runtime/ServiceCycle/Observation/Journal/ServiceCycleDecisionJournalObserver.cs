using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal sealed partial class ServiceCycleDecisionJournalObserver : IServiceCycleDecisionJournalObserver
{
    private const string CoalescerSite = "DecisionJournalCoalescer";

    private readonly IDecisionJournalObservationSink _journal;
    private readonly DecisionJournalServiceCursor[] _services;
    private readonly DecisionJournalLifecycleObservation _lifecycle;
    private ConfigGeneration _configuration;
    private StrategyGeneration _strategy;
    private Exception? _faultException;
    private string? _faultSite;
    private bool _faulted;

    internal ServiceCycleDecisionJournalObserver(
        IDecisionJournalObservationSink journal,
        int serviceCapacity)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        if (serviceCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(serviceCapacity));
        _services = new DecisionJournalServiceCursor[serviceCapacity];
        _lifecycle = new DecisionJournalLifecycleObservation(journal);
    }

    public bool IsFaulted => _faulted || _journal.IsFaulted;

    public Exception? FaultException => _faultException;

    /// <summary>
    /// Where the first contained failure was caught, so a stopped journal can say what killed it.
    /// </summary>
    /// <remarks>
    /// The coalescer refuses records without throwing once its sink is gone, so a journal can fault
    /// with no exception to report. Naming the sink there keeps "stopped after ProducerFailed" from
    /// being the whole account of a dead journal.
    /// </remarks>
    public string? FaultSite => _faultSite ?? (_journal.IsFaulted ? CoalescerSite : null);

    /// <summary>
    /// Establishes the suite-wide publication baseline. Attaching mid-session must not fabricate
    /// change records for generations that were already in force.
    /// </summary>
    public void BindPublications(ConfigGeneration configuration, StrategyGeneration strategy)
    {
        if (IsFaulted) return;
        _configuration = configuration;
        _strategy = strategy;
    }

    public void Bind(
        int ordinal,
        LifecycleGeneration lifecycle,
        ServiceFault fault,
        long lifecycleSemanticVersion,
        long lifecycleTerminalSequence,
        long constructionDeferralSequence,
        long worldGateDeferralSequence)
    {
        if (IsFaulted) return;
        try
        {
            ref var service = ref Service(ordinal);
            var id = new ServiceCycleTraceServiceId(checked((ulong)ordinal + 1));
            service.Bind(
                id,
                lifecycle,
                fault,
                lifecycleSemanticVersion,
                lifecycleTerminalSequence,
                constructionDeferralSequence,
                worldGateDeferralSequence);
        }
        catch (Exception exception) when (CanContain(exception)) { Fault(exception, nameof(Bind)); }
    }

    public void ObservePublications(
        ConfigGeneration configuration,
        StrategyGeneration strategy,
        MonotonicTimestamp observedAt)
    {
        if (IsFaulted) return;
        try
        {
            EnsureConfiguration(configuration, observedAt);
            EnsureStrategy(strategy, observedAt);
        }
        catch (Exception exception) when (CanContain(exception)) { Fault(exception, nameof(ObservePublications)); }
    }

    private void EnsureConfiguration(ConfigGeneration generation, MonotonicTimestamp observedAt)
    {
        if (!generation.IsValid || generation.Value <= _configuration.Value) return;
        _configuration = generation;
        Transition(
            DecisionJournalRecordKind.ConfigurationChanged,
            default,
            generation.Value,
            observedAt);
    }

    private void ObserveFaultTransition(
        ref DecisionJournalServiceCursor service,
        ServiceFaultRecoveryFact recovery,
        ServiceFault fault,
        MonotonicTimestamp observedAt)
    {
        if (recovery.IsPresent)
            _journal.BreakServiceSpan(service.Service, observedAt);
        service.ObserveFaultTransition(recovery, fault);
    }

    private void EnsureStrategy(StrategyGeneration generation, MonotonicTimestamp observedAt)
    {
        if (generation.Value == 0 || generation.Value <= _strategy.Value) return;
        _strategy = generation;
        Transition(
            DecisionJournalRecordKind.StrategyChanged,
            default,
            generation.Value,
            observedAt);
    }

    private void Transition(
        DecisionJournalRecordKind kind,
        ServiceCycleTraceServiceId service,
        ulong generation,
        MonotonicTimestamp observedAt,
        int code = 0)
    {
        var transition = DecisionJournalRecord.Transition(kind, service, generation, observedAt, code);
        _journal.ObserveTransition(in transition);
    }

    private ref DecisionJournalServiceCursor Service(int ordinal)
    {
        if ((uint)ordinal >= (uint)_services.Length) throw new ArgumentOutOfRangeException(nameof(ordinal));
        return ref _services[ordinal];
    }

    private ref DecisionJournalServiceCursor BoundService(int ordinal)
    {
        ref var service = ref Service(ordinal);
        if (!service.IsBound) throw new InvalidOperationException("The journal service is not bound.");
        return ref service;
    }

    /// <summary>
    /// Records a contained failure and the observation that hit it.
    /// </summary>
    /// <remarks>
    /// Containment keeps gameplay alive, but discarding the exception left the runtime able to say
    /// only that the producer failed. The first one is kept because later ones are consequences of
    /// a journal that already stopped tracking the suite.
    /// </remarks>
    private void Fault(Exception exception, string site)
    {
        if (_faultException is null)
        {
            _faultException = exception;
            _faultSite = site;
        }
        _faulted = true;
    }

    private static bool CanContain(Exception exception) =>
        !ExceptionDetail.IsProcessFatal(exception);
}
