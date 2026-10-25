using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Owns the scene-bound quick-controls column, installation retries and diagnostic outputs.
/// </summary>
internal sealed class QuickControlsHost : IDisposable
{
    private readonly AutomationFeatureControlRegistry _features;
    private readonly EmergencyStopControl _emergencyStop;
    private readonly Action<string> _log;
    private readonly Action _reportSuccess;
    private readonly Action<string> _reportWaiting;
    private readonly Action<string> _reportFailure;
    private readonly UiInstallationRetryState _retry = new();
    private QuickControlColumn? _column;
    private float _retrySeconds;

    internal QuickControlsHost(
        AutomationFeatureControlRegistry features,
        EmergencyStopControl emergencyStop,
        Action<string> log,
        Action reportSuccess,
        Action<string> reportWaiting,
        Action<string> reportFailure)
    {
        _features = features ?? throw new ArgumentNullException(nameof(features));
        _emergencyStop = emergencyStop ?? throw new ArgumentNullException(nameof(emergencyStop));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _reportSuccess = reportSuccess ?? throw new ArgumentNullException(nameof(reportSuccess));
        _reportWaiting = reportWaiting ?? throw new ArgumentNullException(nameof(reportWaiting));
        _reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
    }

    internal void RetryNow() => _retrySeconds = 0f;

    internal void ResetLifecycle()
    {
        _column?.Dispose();
        _column = null;
        _retrySeconds = 0f;
        ResetQuickControlsFailure();
    }

    public void Dispose() => ResetLifecycle();

    internal void Update(float unscaledDeltaTime, bool gameplayScene, bool startupReady, bool allowFeatureControls)
    {
        if (!gameplayScene)
        {
            ResetLifecycle();
            return;
        }
        if (_column is not null && !_column.IsAlive)
        {
            _column.Dispose();
            _column = null;
        }
        if (_column is not null &&
            _column.AllowsFeatureControls != allowFeatureControls)
        {
            _column.Dispose();
            _column = null;
            ResetQuickControlsFailure();
        }
        _column?.Render();
        if (_column is not null && _column.Failures.Count == 0)
        {
            ResetQuickControlsFailure();
            _reportSuccess();
            return;
        }

        if (!startupReady) return;

        _retrySeconds -= Math.Max(0f, unscaledDeltaTime);
        if (_retrySeconds > 0f) return;
        _retrySeconds = UiInstallationRetryState.RetryIntervalSeconds;
        if (!QuickControlNativeAdapter.TryCapture(out var native, out var captureReason))
        {
            ReportQuickControlsRetry(captureReason);
            return;
        }
        if (!QuickControlColumn.TryCreate(
                _features,
                _emergencyStop,
                native,
                allowFeatureControls: allowFeatureControls,
                out var candidate,
                out var constructionReason))
        {
            ReportQuickControlsRetry(constructionReason);
            return;
        }

        if (_column is null ||
            candidate!.Failures.Count < _column.Failures.Count)
        {
            _column?.Dispose();
            _column = candidate;
        }
        else
        {
            candidate!.Dispose();
        }
        if (_column is not null && _column.Failures.Count == 0)
        {
            ResetQuickControlsFailure();
            _reportSuccess();
            return;
        }
        ReportQuickControlsRetry(constructionReason);
    }

    private void ReportQuickControlsRetry(string reason)
    {
        var failureReason = string.IsNullOrWhiteSpace(reason)
            ? "audited native objects are not ready"
            : reason;
        var observation = _retry.ObserveFailure();
        if (observation.ShouldLogRetry)
        {
            _log(
                "Quick controls are not ready; installation will retry: " +
                failureReason);
        }
        if (observation.IsTerminal)
            _reportFailure(failureReason);
        else
            _reportWaiting(failureReason);
    }

    private void ResetQuickControlsFailure()
    {
        _retry.Reset();
    }

}
