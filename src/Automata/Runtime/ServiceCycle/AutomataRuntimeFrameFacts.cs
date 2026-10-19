#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// The ServiceCycle-owned facts already present after a pump frame. Callers pin the current
/// publications by reference and request the service diagnostics only when they will return them.
/// This is a one-call frame view, not an independently published or refreshed state model.
/// </summary>
internal sealed class AutomataRuntimeFrameFacts
{
    private AutomataRuntimeFrameFacts(
        WorldPublication<GameWorldState> world,
        StrategistBulletin? strategistBulletin,
        ConfigurationPublication configuration,
        bool emergencyStopEngaged,
        long currentLifecycle,
        AutomataServiceFrameFacts[] services,
        UnavailableGameAction[] unavailableGameActions)
    {
        World = world;
        StrategistBulletin = strategistBulletin;
        Configuration = configuration;
        EmergencyStopEngaged = emergencyStopEngaged;
        CurrentLifecycle = currentLifecycle;
        Services = services;
        UnavailableGameActions = unavailableGameActions;
    }

    internal WorldPublication<GameWorldState> World { get; }
    internal StrategistBulletin? StrategistBulletin { get; }
    internal ConfigurationPublication Configuration { get; }
    internal bool EmergencyStopEngaged { get; }
    internal long CurrentLifecycle { get; }
    internal AutomataServiceFrameFacts[] Services { get; }
    internal UnavailableGameAction[] UnavailableGameActions { get; }

    internal static AutomataRuntimeFrameFacts Capture(
        AutomataServiceCycleHost host,
        ServiceConfigurationPublisher configuration,
        bool includeServices,
        UnavailableGameAction[] unavailableGameActions)
    {
        if (host is null) throw new ArgumentNullException(nameof(host));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        var pump = host.Pump;
        var registry = pump.DiagnosticsRegistry;
        var services = includeServices
            ? CaptureServices(registry)
            : Array.Empty<AutomataServiceFrameFacts>();
        return new AutomataRuntimeFrameFacts(
            registry.World.ReadLatest(),
            registry.Strategy.ReadStrategistBulletin(),
            configuration.ReadLatest(),
            host.EmergencyStopEngaged,
            checked((long)host.CurrentLifecycle.Value),
            services,
            unavailableGameActions);
    }

    private static AutomataServiceFrameFacts[] CaptureServices(
        OrbModding.Common.Runtime.ServiceCycle.Registration.ServiceCycleRegistry registry)
    {
        var services = new AutomataServiceFrameFacts[registry.OrdinalCount];
        for (var ordinal = 0; ordinal < services.Length; ordinal++)
        {
            var id = registry.GetServiceId(ordinal);
            var slot = registry.GetSlot(ordinal);
            services[ordinal] = slot.TryGetRunnerSnapshot(out var runner, waitForHandoffGate: false)
                ? new AutomataServiceFrameFacts(
                    id.Value,
                    AutomataServiceCycleTraceRoster.DisplayName(id),
                    true,
                    runner)
                : new AutomataServiceFrameFacts(
                    id.Value,
                    AutomataServiceCycleTraceRoster.DisplayName(id),
                    false,
                    default);
        }
        return services;
    }
}

internal readonly struct AutomataServiceFrameFacts
{
    internal AutomataServiceFrameFacts(
        string serviceId,
        string displayName,
        bool hasRunner,
        ServiceRunnerSnapshot runner)
    {
        ServiceId = serviceId;
        DisplayName = displayName;
        HasRunner = hasRunner;
        Runner = runner;
    }

    internal string ServiceId { get; }
    internal string DisplayName { get; }
    internal bool HasRunner { get; }
    internal ServiceRunnerSnapshot Runner { get; }
}
#endif
