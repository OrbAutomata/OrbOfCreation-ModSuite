namespace OrbModding.Common;

internal readonly record struct UiInstallationRetryObservation(
    int Attempt,
    bool ShouldLogRetry,
    bool IsTerminal);

/// <summary>
/// Shared retry-reporting discipline for the Mods rail and quick-controls surface.
/// The bounded zero-count startup gate precedes this state; only genuine or post-window failures
/// enter the surface-owned five-second cadence and count toward terminal status.
/// </summary>
internal sealed class UiInstallationRetryState
{
    internal const int TerminalAttempt = 3;
    internal const float RetryIntervalSeconds = 5f;

    private int _attempts;
    private bool _retryLogged;

    internal UiInstallationRetryObservation ObserveFailure()
    {
        _attempts++;
        var shouldLogRetry = !_retryLogged;
        _retryLogged = true;
        return new UiInstallationRetryObservation(
            _attempts,
            shouldLogRetry,
            _attempts >= TerminalAttempt);
    }

    internal void Reset()
    {
        _attempts = 0;
        _retryLogged = false;
    }
}
