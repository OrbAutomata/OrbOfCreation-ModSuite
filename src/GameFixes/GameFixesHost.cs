using System.Collections.Generic;
using System.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// Turns each installed fix on exactly while the published configuration selects it and Game Fixes
/// is Active, and reports what the fixes did. Game fixes are not automation: it runs whether or not
/// the automation runtime does, and <b>Automation enabled</b> and <b>STOP ALL</b> leave it as it is.
/// </summary>
internal sealed class GameFixesHost : IAutomataStandingFeature
{
    /// <summary>How many frames pass between two readings of how often the fixes fired.</summary>
    internal const int ActivityPollFrames = 60;

    private readonly IReadOnlyList<GameFix> _fixes;
    private readonly IReadOnlyDictionary<GameFix, string> _failures;
    private readonly AutomataFeatureStatusReporter _status;
    private readonly long[] _reportedFired;
    private SuiteRuntimeConfiguration _configuration;
    private ConfigGeneration _configurationGeneration;
    private long? _lifecycle;
    private int _framesSincePoll;

    internal GameFixesHost(IReadOnlyList<GameFix> fixes, IReadOnlyDictionary<GameFix, string> failures,
        AutomataFeatureStatusReporter status, SuiteRuntimeConfiguration configuration,
        ConfigGeneration configurationGeneration)
    {
        _fixes = fixes;
        _failures = failures;
        _status = status;
        _reportedFired = new long[fixes.Count];
        _configuration = configuration;
        _configurationGeneration = configurationGeneration;
        Apply();
    }

    public void ObserveConfiguration(SuiteRuntimeConfiguration configuration, ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _configuration = configuration;
        _configurationGeneration = configurationGeneration;
        Apply();
        Publish();
    }

    public void Tick(bool lifecycleReady, long lifecycleGeneration)
    {
        if (!lifecycleReady)
        {
            _lifecycle = null;
            return;
        }
        if (_lifecycle != lifecycleGeneration)
        {
            _lifecycle = lifecycleGeneration;
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

    public void Dispose()
    {
        foreach (var fix in _fixes) fix.Apply(false);
    }

    private void Apply()
    {
        var enabled = _configuration.GameFixes.Mode == GameFixesOperationMode.Active;
        foreach (var fix in _fixes)
            fix.Apply(enabled && !_failures.ContainsKey(fix) && fix.IsSelected(_configuration));
    }

    private void Publish()
    {
        if (_lifecycle is not { } lifecycle) return;
        var unavailable = _fixes.Where(fix => fix.IsSelected(_configuration) && _failures.ContainsKey(fix))
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
                FeatureStatusReasonCode.ContractUnavailable, string.Join(" ", unavailable), lifecycle,
                _configurationGeneration, detail);
        else
            _status.ObserveRuntimeLifecycle(FeatureStatusState.Operational, FeatureStatusReasonCode.None,
                string.Empty, lifecycle, _configurationGeneration, detail);
    }
}
