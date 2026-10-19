#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbAutomata.GameMcp;

internal sealed class GameMcpGadgetResults
{
    private readonly Func<long> _lifecycleGeneration;
    private readonly Func<ulong> _configurationGeneration;

    internal GameMcpGadgetResults(
        Func<long> lifecycleGeneration,
        Func<ulong> configurationGeneration)
    {
        _lifecycleGeneration = lifecycleGeneration ??
            throw new ArgumentNullException(nameof(lifecycleGeneration));
        _configurationGeneration = configurationGeneration ??
            throw new ArgumentNullException(nameof(configurationGeneration));
    }

    internal GameMcpCommandResult Committed(
        string code,
        GameMcpObjectBuilder details) =>
        GameMcpCommandResult.Committed(
            code,
            observedLifecycleGeneration: _lifecycleGeneration(),
            observedConfigurationGeneration: _configurationGeneration(),
            details.Freeze());

    /// <summary>
    /// A gadget answer for a call the suite stopped on its own, with the game never asked.
    /// </summary>
    /// <remarks>
    /// The router validates every screen and every probe name before a gadget is reached, so the
    /// guards below it are reachable only by the suite contradicting its own router. They stay —
    /// a wrong assumption must fail closed rather than fall through — but the word for them is not
    /// <c>refused</c>, which reads as the game having said no and sends a caller looking for a game
    /// state to change that does not exist.
    /// </remarks>
    internal GameMcpCommandResult Failed(string code, string reason) =>
        GameMcpCommandResult.Failed(
            code,
            reason,
            observedLifecycleGeneration: _lifecycleGeneration(),
            observedConfigurationGeneration: _configurationGeneration());

    internal GameMcpCommandResult Faulted(string code, string reason) =>
        GameMcpCommandResult.Faulted(
            code,
            reason,
            observedLifecycleGeneration: _lifecycleGeneration(),
            observedConfigurationGeneration: _configurationGeneration());

    internal GameMcpCommandResult Rejected(
        string code,
        string reason,
        GameMcpObjectBuilder? details = null) =>
        GameMcpCommandResult.Rejected(
            code,
            reason,
            observedLifecycleGeneration: _lifecycleGeneration(),
            observedConfigurationGeneration: _configurationGeneration(),
            details?.Freeze());
}
#endif
