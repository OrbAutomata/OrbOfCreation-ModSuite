using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.GameMcp;
#endif

namespace OrbAutomata;

/// <summary>
/// The one Automata-owned ServiceCycle runtime. It owns the shared host and the
/// ordered feature runtimes, and drives them per Unity frame, per saved-configuration
/// publication, and per lifecycle boundary. It owns no feature policy, native adapter,
/// or typed service generic.
/// </summary>
internal sealed class AutomataServiceCycleRuntime : IAutomataServiceCycleRuntime
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly AutomataServiceCycleHost _host;
    private readonly IAutomataServiceCycleFeatureRuntime[] _features;
    private readonly ServiceConfigurationPublisher _configurationPublication;
    private ConfigGeneration _configurationGeneration;
    private readonly LifecycleGameActions _gameActions;
    private bool _disposed;

    internal AutomataServiceCycleRuntime(
        Func<long> readLifecycleEpoch,
        ServiceConfigurationPublisher configurationPublication,
        AutomataServiceCycleHost host,
        IAutomataServiceCycleFeatureRuntime[] features,
        ConfigGeneration configurationGeneration,
        LifecycleGameActions gameActions
#if SERVICE_CYCLE_PROFILE
        , GameMcpCommandDispatcher gameMcp
#endif
        )
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _configurationPublication = configurationPublication ??
            throw new ArgumentNullException(nameof(configurationPublication));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _features = features ?? throw new ArgumentNullException(nameof(features));
        _configurationGeneration = configurationGeneration;
        _gameActions = gameActions ?? throw new ArgumentNullException(nameof(gameActions));
#if SERVICE_CYCLE_PROFILE
        GameMcp = gameMcp ?? throw new ArgumentNullException(nameof(gameMcp));
#endif
    }

    internal SuiteRuntimeConfiguration CurrentConfiguration => _configurationPublication.ReadLatest().Snapshot;
    internal ConfigGeneration CurrentConfigurationGeneration =>
        _configurationPublication.ReadLatest().Generation;
    internal LifecycleGeneration CurrentLifecycle => _host.CurrentLifecycle;
    internal bool EmergencyStopEngaged => _host.EmergencyStopEngaged;

    public void Tick(float unscaledDeltaTime)
    {
        if (_disposed) return;
        var report = _host.Tick();
        var pump = _host.Pump;
        for (var index = 0; index < _features.Length; index++)
            _features[index].ObserveFrame(pump, in report);
    }

    public void PublishSavedConfiguration(
        SuiteRuntimeConfiguration configuration,
        ConfigGeneration configurationGeneration)
    {
        if (_disposed) return;
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (configurationGeneration.Value <= _configurationGeneration.Value) return;
        if (configurationGeneration != _configurationGeneration.Next())
            throw new InvalidOperationException(
                "A saved configuration generation was skipped before ServiceCycle publication.");
        var publishedGeneration = _configurationPublication.Publish(configuration);
        if (publishedGeneration != configurationGeneration)
            throw new InvalidOperationException(
                "The configuration store and ServiceCycle publication generations diverged.");
        _configurationGeneration = configurationGeneration;
        for (var index = 0; index < _features.Length; index++)
            _features[index].ObserveConfiguration(configurationGeneration);
    }

    public void CancelPreparedWork()
    {
        if (_disposed) return;
        if (!_host.EmergencyStopEngaged)
            // This cancellation is recoverable: the caller may be releasing ownership, disabling
            // automation, or synchronously engaging the configured emergency stop. Marking it as a
            // shutdown made the configuration pump correctly refuse to clear it, so RESUME could
            // never restore this runtime. The next published false reading may clear a user episode;
            // Dispose still creates the non-clearable shutdown episode below.
            _host.SetEmergencyStop(true, EmergencyStopReason.UserRequested);
    }

    public void InvalidateLifecycle()
    {
        if (_disposed) return;
        var nativeLifecycle = _readLifecycleEpoch();
        if (!_host.TryReplaceLifecycle(nativeLifecycle)) return;
        for (var index = 0; index < _features.Length; index++)
            _features[index].ObserveLifecycle(
                nativeLifecycle,
                _configurationGeneration);
        _gameActions.InvalidateLifecycle();
    }

    public AutomataDiagnosticsRuntimeEvidence CaptureDiagnostics()
    {
        if (_disposed)
            return AutomataDiagnosticsRuntimeEvidence.Unavailable(
                "The automation runtime has shut down.");
        return _host.CaptureDiagnostics();
    }

#if SERVICE_CYCLE_PROFILE
    public GameMcpCommandDispatcher GameMcp { get; }

    public AutomataRuntimeFrameFacts CaptureFrameFacts(bool includeServices)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AutomataServiceCycleRuntime));
        return AutomataRuntimeFrameFacts.Capture(
            _host,
            _configurationPublication,
            includeServices,
            _gameActions.Unavailable());
    }
#endif

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
#if SERVICE_CYCLE_PROFILE
        GameMcp.Dispose();
#endif
        try
        {
            _gameActions.Dispose();
            if (!_host.EmergencyStopEngaged)
                _host.SetEmergencyStop(true, EmergencyStopReason.SuiteShutdown);
        }
        finally
        {
            try
            {
                for (var index = 0; index < _features.Length; index++)
                    _features[index].DisposeDiagnostics();
            }
            finally
            {
                try
                {
                    _host.Shutdown();
                }
                finally
                {
                    for (var index = 0; index < _features.Length; index++)
                        _features[index].DisposeRegistration();
                }
            }
        }
    }
}
