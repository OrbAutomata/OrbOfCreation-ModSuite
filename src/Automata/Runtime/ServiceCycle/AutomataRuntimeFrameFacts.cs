#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
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
        ConfigurationPublication configuration,
        bool emergencyStopEngaged,
        long acceptedFrameCount,
        long currentLifecycle,
        AutomataServiceFrameFacts[] services,
        bool playerCraftingAvailable,
        string playerCraftingUnavailableReason,
        bool craftingInstancesAvailable,
        string craftingInstancesUnavailableReason)
    {
        World = world;
        Configuration = configuration;
        EmergencyStopEngaged = emergencyStopEngaged;
        AcceptedFrameCount = acceptedFrameCount;
        CurrentLifecycle = currentLifecycle;
        Services = services;
        PlayerCraftingAvailable = playerCraftingAvailable;
        PlayerCraftingUnavailableReason = playerCraftingUnavailableReason ?? string.Empty;
        CraftingInstancesAvailable = craftingInstancesAvailable;
        CraftingInstancesUnavailableReason = craftingInstancesUnavailableReason ?? string.Empty;
    }

    internal WorldPublication<GameWorldState> World { get; }
    internal ConfigurationPublication Configuration { get; }
    internal bool EmergencyStopEngaged { get; }
    internal long AcceptedFrameCount { get; }
    internal long CurrentLifecycle { get; }
    internal AutomataServiceFrameFacts[] Services { get; }
    internal bool PlayerCraftingAvailable { get; }
    internal string PlayerCraftingUnavailableReason { get; }
    internal bool CraftingInstancesAvailable { get; }
    internal string CraftingInstancesUnavailableReason { get; }

    internal static AutomataRuntimeFrameFacts Capture(
        AutomataServiceCycleHost host,
        ServiceConfigurationPublisher configuration,
        bool includeServices,
        bool playerCraftingAvailable,
        string playerCraftingUnavailableReason,
        bool craftingInstancesAvailable,
        string craftingInstancesUnavailableReason)
    {
        if (host is null) throw new ArgumentNullException(nameof(host));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        var pump = host.Pump;
        var registry = pump.DiagnosticsRegistry;
        var services = includeServices
            ? CaptureServices(registry)
            : Array.Empty<AutomataServiceFrameFacts>();
        var diagnostics = pump.DiagnosticsSnapshot;
        return new AutomataRuntimeFrameFacts(
            registry.World.ReadLatest(),
            configuration.ReadLatest(),
            host.EmergencyStopEngaged,
            diagnostics.AcceptedFrameCount,
            checked((long)host.CurrentLifecycle.Value),
            services,
            playerCraftingAvailable,
            playerCraftingUnavailableReason,
            craftingInstancesAvailable,
            craftingInstancesUnavailableReason);
    }

    private static AutomataServiceFrameFacts[] CaptureServices(
        OrbModding.Common.Runtime.ServiceCycle.Registration.ServiceCycleRegistry registry)
    {
        var services = new AutomataServiceFrameFacts[registry.OrdinalCount];
        for (var ordinal = 0; ordinal < services.Length; ordinal++)
        {
            var id = registry.GetServiceId(ordinal);
            var slot = registry.GetSlot(ordinal);
            services[ordinal] = slot.TryGetRunnerSnapshot(out var runner)
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
