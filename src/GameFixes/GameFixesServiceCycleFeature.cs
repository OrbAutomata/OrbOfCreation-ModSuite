using System.Collections.Generic;
using System.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbAutomata;

/// <summary>Registers no service; its runtime only switches the installed fixes.</summary>
internal sealed class GameFixesServiceCycleFeature : IAutomataServiceCycleFeature
{
    private readonly IReadOnlyList<GameFix> _fixes;
    private readonly IReadOnlyDictionary<GameFix, string> _failures;
    private readonly AutomataFeatureStatusReporter _status;

    internal GameFixesServiceCycleFeature(IReadOnlyList<GameFix> fixes,
        IReadOnlyDictionary<GameFix, string> failures, AutomataFeatureStatusReporter status)
    {
        _fixes = fixes;
        _failures = failures;
        _status = status;
    }

    public IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context) =>
        new GameFixesRuntime(_fixes, _failures, _status, context.Registry.Configuration,
            context.LifecycleValue, context.ConfigurationGeneration);
}

/// <summary>
/// Turns each installed fix on exactly while the published configuration selects it, Game Fixes is
/// Active, automation is enabled and the emergency stop is clear, and reports what the fixes did.
/// </summary>
internal sealed class GameFixesRuntime : IAutomataServiceCycleFeatureRuntime
{
    /// <summary>How many frames pass between two readings of how often the fixes fired.</summary>
    internal const int ActivityPollFrames = 60;

    private readonly IReadOnlyList<GameFix> _fixes;
    private readonly IReadOnlyDictionary<GameFix, string> _failures;
    private readonly AutomataFeatureStatusReporter _status;
    private readonly ServiceConfigurationPublisher _configuration;
    private readonly long[] _reportedFired;
    private long _lifecycle;
    private ConfigGeneration _configurationGeneration;
    private bool _emergency;
    private bool _diagnosticsActive;
    private int _framesSincePoll;

    internal GameFixesRuntime(IReadOnlyList<GameFix> fixes, IReadOnlyDictionary<GameFix, string> failures,
        AutomataFeatureStatusReporter status, ServiceConfigurationPublisher configuration,
        long lifecycle, ConfigGeneration configurationGeneration)
    {
        _fixes = fixes;
        _failures = failures;
        _status = status;
        _configuration = configuration;
        _reportedFired = new long[fixes.Count];
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        Apply();
    }

    public void ActivateDiagnostics()
    {
        _diagnosticsActive = true;
        Publish();
    }

    public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report)
    {
        if (_emergency != pump.IsEmergencyStopEngaged)
        {
            _emergency = pump.IsEmergencyStopEngaged;
            Apply();
            Publish();
            return;
        }
        if (++_framesSincePoll < ActivityPollFrames) return;
        _framesSincePoll = 0;
        for (var index = 0; index < _fixes.Count; index++)
        {
            if (_fixes[index].Fired == _reportedFired[index]) continue;
            Publish();
            return;
        }
    }

    public void ObserveConfiguration(ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _configurationGeneration = configurationGeneration;
        Apply();
        Publish();
    }

    public void ObserveLifecycle(long nativeLifecycle, ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _lifecycle = nativeLifecycle;
        _configurationGeneration = configurationGeneration;
        Publish();
    }

    public void DisposeDiagnostics() => _diagnosticsActive = false;

    public void DisposeRegistration()
    {
        foreach (var fix in _fixes) fix.Apply(false);
    }

    private void Apply()
    {
        var configuration = _configuration.ReadLatest().Snapshot;
        var enabled = configuration.General.Enabled && !configuration.Safety.EmergencyDisable && !_emergency &&
            configuration.GameFixes.Mode == GameFixesOperationMode.Active;
        foreach (var fix in _fixes)
            fix.Apply(enabled && !_failures.ContainsKey(fix) && fix.IsSelected(configuration));
    }

    private void Publish()
    {
        if (!_diagnosticsActive) return;
        var configuration = _configuration.ReadLatest().Snapshot;
        var unavailable = _fixes.Where(fix => fix.IsSelected(configuration) && _failures.ContainsKey(fix))
            .Select(fix => fix.Name + " is not installed: " + _failures[fix]).ToArray();
        var fired = new List<string>();
        for (var index = 0; index < _fixes.Count; index++)
        {
            _reportedFired[index] = _fixes[index].Fired;
            if (_reportedFired[index] != 0) fired.Add(_fixes[index].DescribeFired(_reportedFired[index]));
        }
        var detail = string.Join(" ", fired);
        if (unavailable.Length != 0)
            _status.ObserveRuntimeLifecycle(FeatureStatusState.ContractUnavailable,
                FeatureStatusReasonCode.ContractUnavailable, string.Join(" ", unavailable), _lifecycle,
                _configurationGeneration, detail);
        else
            _status.ObserveRuntimeLifecycle(FeatureStatusState.Operational, FeatureStatusReasonCode.None,
                string.Empty, _lifecycle, _configurationGeneration, detail);
    }
}
