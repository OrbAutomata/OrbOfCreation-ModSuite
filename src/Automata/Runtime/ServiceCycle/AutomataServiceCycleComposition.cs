using System;
using System.Collections.Generic;
using BepInEx.Logging;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbAutomata;

/// <summary>
/// Builds the Automata application. The ServiceCycle owns its registry, configuration,
/// observability, and host; each feature supplies only its typed service and native boundary.
/// Every GameAction, a feature's or the caller's, is registered once into the one
/// <see cref="LifecycleGameActions"/> the runtime owns.
/// </summary>
internal static class AutomataServiceCycleComposition
{
    internal static AutomataServiceCycleRuntime? TryCreate(
        SuiteRuntimeConfiguration configuration,
        ConfigGeneration configurationGeneration,
        AutomataServiceCycleHostDependencies hostDependencies,
        IReadOnlyList<IAutomataServiceCycleFeature> features,
        ManualLogSource log,
        Action<LifecycleGameActions>? composeGameActions = null)
    {
        try
        {
            var runtime = Create(
                configuration,
                configurationGeneration,
                hostDependencies,
                features,
                log,
                composeGameActions);
            log.LogAutomataInfo("Automata ServiceCycle runtime registered.");
            return runtime;
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            log.LogAutomataError(
                "Automata ServiceCycle host initialization failed and its features are disabled: " +
                exception.GetBaseException().Message);
            return null;
        }
    }

    public static AutomataServiceCycleRuntime Create(
        SuiteRuntimeConfiguration configuration,
        ConfigGeneration configurationGeneration,
        AutomataServiceCycleHostDependencies hostDependencies,
        IReadOnlyList<IAutomataServiceCycleFeature> features,
        ManualLogSource log,
        Action<LifecycleGameActions>? composeGameActions = null)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (hostDependencies is null) throw new ArgumentNullException(nameof(hostDependencies));
        if (features is null) throw new ArgumentNullException(nameof(features));
        if (features.Count == 0)
            throw new ArgumentException("The Automata ServiceCycle host requires at least one feature.", nameof(features));
        if (log is null) throw new ArgumentNullException(nameof(log));

        var lifecycle = AutomataServiceCycleHost.ToLifecycle(hostDependencies.ReadLifecycleEpoch());
        ServiceCycleRegistry? registry = null;
        AutomataServiceCycleHost? host = null;
        AutomataServiceCycleObservability? observability = null;
        var gameActions = new LifecycleGameActions();
        var featureRuntimes = new List<IAutomataServiceCycleFeatureRuntime>(features.Count);
        try
        {
            registry = new ServiceCycleRegistry(
                features.Count,
                configuration,
                configurationGeneration,
                lifecycle);
            var configurationPublication = registry.Configuration;
            observability = AutomataServiceCycleObservability.Create(
                registry.Clock,
                traceActive: hostDependencies.Observability.FullTrace.Enabled,
                log);
#if SERVICE_CYCLE_PROFILE
            var profileProbe = observability.ProfileProbe;
#endif

            for (var index = 0; index < features.Count; index++)
            {
                var context = new AutomataServiceCycleFeatureContext(
                    registry,
                    configurationGeneration,
                    checked((long)lifecycle.Value),
                    gameActions
#if SERVICE_CYCLE_PROFILE
                    , profileProbe
#endif
                    );
                featureRuntimes.Add(features[index].Register(in context));
            }

            host = new AutomataServiceCycleHost(
                registry,
                hostDependencies.ReadFrameIdentity,
                hostDependencies.PumpTiming,
                // Always attached, never streaming: the ring holds the recent past in memory so a
                // bug report can snapshot it after something goes wrong, rather than requiring a
                // diagnostics request to have been made before it did.
                HostSemanticTrace.Create(
                    new ServiceCycleTraceSessionId(checked((ulong)DateTime.UtcNow.Ticks)),
                    features.Count),
                hostDependencies.ActionOutcomes
#if SERVICE_CYCLE_PROFILE
                , profileProbe
#endif
                , message => log.LogAutomataError(message)
                );
            var observabilityOptions = hostDependencies.Observability;
            host.AttachObservability(observability, in observabilityOptions);
            observability = null;
            for (var index = 0; index < featureRuntimes.Count; index++)
                featureRuntimes[index].ActivateDiagnostics();
            composeGameActions?.Invoke(gameActions);
            return new AutomataServiceCycleRuntime(
                hostDependencies.ReadLifecycleEpoch,
                configurationPublication,
                host,
                featureRuntimes.ToArray(),
                configurationGeneration,
                gameActions);
        }
        catch
        {
            gameActions.Dispose();
            DisposeFailedConstruction(featureRuntimes, observability, host, registry);
            throw;
        }
    }

    private static void DisposeFailedConstruction(
        List<IAutomataServiceCycleFeatureRuntime> featureRuntimes,
        IDisposable? observability,
        AutomataServiceCycleHost? host,
        ServiceCycleRegistry? registry)
    {
        try
        {
            for (var index = 0; index < featureRuntimes.Count; index++)
                featureRuntimes[index].DisposeDiagnostics();
        }
        finally
        {
            try { observability?.Dispose(); }
            finally
            {
                try
                {
                    if (host is not null)
                    {
                        host.Shutdown();
                    }
                    else
                    {
                        registry?.Dispose();
                    }
                }
                finally
                {
                    for (var index = 0; index < featureRuntimes.Count; index++)
                        featureRuntimes[index].DisposeRegistration();
                }
            }
        }
    }
}
