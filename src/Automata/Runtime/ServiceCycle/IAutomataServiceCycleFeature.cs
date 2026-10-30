using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbAutomata;

/// <summary>
/// Application context handed to a feature during composition. The feature performs
/// its typed registration against the cycle registry and keeps its service generics local, and
/// registers any GameAction it composes into <see cref="GameActions"/>, which owns it from then on.
/// </summary>
internal readonly struct AutomataServiceCycleFeatureContext
{
    internal AutomataServiceCycleFeatureContext(
        ServiceCycleRegistry registry,
        ConfigGeneration configurationGeneration,
        long lifecycleValue,
        LifecycleGameActions gameActions
#if SERVICE_CYCLE_PROFILE
        , ServiceCycleProfileProbe profileProbe
#endif
        )
    {
        Registry = registry;
        ConfigurationGeneration = configurationGeneration;
        LifecycleValue = lifecycleValue;
        GameActions = gameActions;
#if SERVICE_CYCLE_PROFILE
        ProfileProbe = profileProbe;
#endif
    }

    internal ServiceCycleRegistry Registry { get; }
    internal ConfigGeneration ConfigurationGeneration { get; }
    internal long LifecycleValue { get; }
    internal LifecycleGameActions GameActions { get; }
#if SERVICE_CYCLE_PROFILE
    internal ServiceCycleProfileProbe ProfileProbe { get; }
#endif
}

/// <summary>
/// One feature in the Automata application. It supplies its typed service and native boundary.
/// </summary>
internal interface IAutomataServiceCycleFeature
{
    /// <summary>
    /// Registers the feature's typed definition into the shared registry and returns a
    /// non-generic runtime that the application drives per frame, per configuration
    /// publication, and per lifecycle boundary.
    /// </summary>
    IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context);

}

/// <summary>
/// The non-generic per-feature runtime driven by the application. It owns the
/// feature's typed registration handle, native binding cache, and diagnostics
/// projection. Disposal is split so the owner can preserve the exact ordering of
/// diagnostics teardown (before host shutdown) and registration/binding teardown
/// (after host shutdown).
/// </summary>
internal interface IAutomataServiceCycleFeatureRuntime
{
#if SERVICE_CYCLE_PROFILE
    /// <summary>The feature's optional player command; runtimes without one contribute none.</summary>
    OrbAutomata.GameMcp.IGameMcpServiceCommand? GameMcpCommand => null;
#endif

    /// <summary>
    /// Stands up the feature's diagnostics once the shared host and its observability
    /// are live. Kept separate from <see cref="IAutomataServiceCycleFeature.Register"/>
    /// so a host that fails to start its observability never registers feature
    /// diagnostics that would then need unwinding.
    /// </summary>
    void ActivateDiagnostics();

    void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report);

    /// <summary>
    /// Invalidates runtime-health evidence produced against an older configuration. The snapshot
    /// itself lives only in the registry publication and is never relayed through feature runtimes.
    /// </summary>
    void ObserveConfiguration(ConfigGeneration configurationGeneration);

    void ObserveLifecycle(
        long nativeLifecycle,
        ConfigGeneration configurationGeneration);

    /// <summary>Tears down diagnostics before the shared host is shut down.</summary>
    void DisposeDiagnostics();

    /// <summary>Tears down the typed registration and native bindings after host shutdown.</summary>
    void DisposeRegistration();
}
