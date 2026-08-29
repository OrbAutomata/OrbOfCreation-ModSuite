using System;
using System.Collections.Generic;
using BepInEx.Logging;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbAutomata;

/// <summary>
/// Builds the Automata application. The ServiceCycle owns its registry, configuration,
/// observability, and host; each feature supplies only its typed service and native boundary.
/// </summary>
internal static class AutomataServiceCycleComposition
{
    internal static AutomataServiceCycleRuntime? TryCreate(
        SuiteRuntimeConfiguration configuration,
        ConfigGeneration configurationGeneration,
        AutomataServiceCycleHostDependencies hostDependencies,
        IReadOnlyList<IAutomataServiceCycleFeature> features,
        ManualLogSource log,
        Func<DiscoveryTreeOfferGameAction>? createDiscoveryTreeOffers = null,
        Func<SpellWorkbenchGameAction>? createSpellWorkbench = null,
        Func<SpellCompositionGameAction>? createSpellComposition = null,
        Func<SpellLoadoutGameAction>? createSpellLoadout = null,
        Func<TargetingGameAction>? createTargeting = null,
        Func<GenericDiscoveryGameAction>? createGenericDiscovery = null,
        Func<EquipmentLoadoutGameAction>? createEquipmentLoadout = null,
        Func<AlchemyLoadoutGameAction>? createAlchemyLoadout = null,
        Func<RitualLifecycleGameAction>? createRitualLifecycle = null,
        Func<GenericLevelGameAction>? createGenericLevel = null,
        Func<CraftingStationGameAction>? createCraftingStations = null,
        Func<CraftingInstanceLifecycleGameAction>? createCraftingInstances = null,
        Func<EquipmentLoadoutGameAction,
            AlchemyLoadoutGameAction, LoadoutGameAction>? createLoadouts = null,
        Func<HarvestLifecycleGameAction>? createHarvestLifecycle = null,
        Func<PlotLifecycleGameAction>? createPlotLifecycle = null,
        Func<StructureLifecycleGameAction>? createStructureLifecycle = null,
        Func<ReturnToMenuGameAction>? createReturnToMenu = null,
        Func<ChallengeGameAction>? createChallenges = null,
        Func<PrestigeGameAction>? createPrestige = null,
        Func<ResearchGameAction>? createResearch = null)
    {
        try
        {
            var runtime = Create(
                configuration,
                configurationGeneration,
                hostDependencies,
                features,
                log,
                createDiscoveryTreeOffers,
                createSpellWorkbench,
                createSpellComposition,
                createSpellLoadout,
                createTargeting,
                createGenericDiscovery,
                createEquipmentLoadout,
                createAlchemyLoadout,
                createRitualLifecycle,
                createGenericLevel,
                createCraftingStations,
                createCraftingInstances,
                createLoadouts,
                createHarvestLifecycle,
                createPlotLifecycle,
                createStructureLifecycle,
                createReturnToMenu,
                createChallenges,
                createPrestige,
                createResearch);
            log.LogAutomataInfo("Automata ServiceCycle runtime registered.");
            return runtime;
        }
        catch (Exception exception) when (IsContainedStartupFailure(exception))
        {
            log.LogAutomataError(
                "Automata ServiceCycle host initialization failed and its features are disabled: " +
                exception.GetBaseException().Message);
            return null;
        }
    }

    internal static bool IsContainedStartupFailure(Exception exception) =>
        exception is not StackOverflowException and
        not OutOfMemoryException and
        not AccessViolationException;

    public static AutomataServiceCycleRuntime Create(
        SuiteRuntimeConfiguration configuration,
        ConfigGeneration configurationGeneration,
        AutomataServiceCycleHostDependencies hostDependencies,
        IReadOnlyList<IAutomataServiceCycleFeature> features,
        ManualLogSource log,
        Func<DiscoveryTreeOfferGameAction>? createDiscoveryTreeOffers = null,
        Func<SpellWorkbenchGameAction>? createSpellWorkbench = null,
        Func<SpellCompositionGameAction>? createSpellComposition = null,
        Func<SpellLoadoutGameAction>? createSpellLoadout = null,
        Func<TargetingGameAction>? createTargeting = null,
        Func<GenericDiscoveryGameAction>? createGenericDiscovery = null,
        Func<EquipmentLoadoutGameAction>? createEquipmentLoadout = null,
        Func<AlchemyLoadoutGameAction>? createAlchemyLoadout = null,
        Func<RitualLifecycleGameAction>? createRitualLifecycle = null,
        Func<GenericLevelGameAction>? createGenericLevel = null,
        Func<CraftingStationGameAction>? createCraftingStations = null,
        Func<CraftingInstanceLifecycleGameAction>? createCraftingInstances = null,
        Func<EquipmentLoadoutGameAction,
            AlchemyLoadoutGameAction, LoadoutGameAction>? createLoadouts = null,
        Func<HarvestLifecycleGameAction>? createHarvestLifecycle = null,
        Func<PlotLifecycleGameAction>? createPlotLifecycle = null,
        Func<StructureLifecycleGameAction>? createStructureLifecycle = null,
        Func<ReturnToMenuGameAction>? createReturnToMenu = null,
        Func<ChallengeGameAction>? createChallenges = null,
        Func<PrestigeGameAction>? createPrestige = null,
        Func<ResearchGameAction>? createResearch = null)
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
        DiscoveryTreeOfferGameAction? discoveryTreeOffers = null;
        SpellWorkbenchGameAction? spellWorkbench = null;
        SpellCompositionGameAction? spellComposition = null;
        SpellLoadoutGameAction? spellLoadout = null;
        TargetingGameAction? targeting = null;
        GenericDiscoveryGameAction? genericDiscovery = null;
        EquipmentLoadoutGameAction? equipmentLoadout = null;
        AlchemyLoadoutGameAction? alchemyLoadout = null;
        RitualLifecycleGameAction? ritualLifecycle = null;
        GenericLevelGameAction? genericLevel = null;
        CraftingStationGameAction? craftingStations = null;
        CraftingInstanceLifecycleGameAction? craftingInstances = null;
        LoadoutGameAction? loadouts = null;
        HarvestLifecycleGameAction? harvestLifecycle = null;
        PlotLifecycleGameAction? plotLifecycle = null;
        StructureLifecycleGameAction? structureLifecycle = null;
        ReturnToMenuGameAction? returnToMenu = null;
        ChallengeGameAction? challenges = null;
        PrestigeGameAction? prestige = null;
        ResearchGameAction? research = null;
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
                    checked((long)lifecycle.Value)
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
            discoveryTreeOffers = createDiscoveryTreeOffers?.Invoke();
            spellWorkbench = createSpellWorkbench?.Invoke();
            spellComposition = createSpellComposition?.Invoke();
            spellLoadout = createSpellLoadout?.Invoke();
            targeting = createTargeting?.Invoke();
            genericDiscovery = createGenericDiscovery?.Invoke();
            equipmentLoadout = createEquipmentLoadout?.Invoke();
            alchemyLoadout = createAlchemyLoadout?.Invoke();
            ritualLifecycle = createRitualLifecycle?.Invoke();
            genericLevel = createGenericLevel?.Invoke();
            craftingStations = createCraftingStations?.Invoke();
            craftingInstances = createCraftingInstances?.Invoke();
            if (createLoadouts is not null)
            {
                if (equipmentLoadout is null || alchemyLoadout is null)
                    throw new InvalidOperationException(
                        "The player-loadout action requires Equipment and Alchemy actions.");
                loadouts = createLoadouts(equipmentLoadout, alchemyLoadout);
            }
            harvestLifecycle = createHarvestLifecycle?.Invoke();
            plotLifecycle = createPlotLifecycle?.Invoke();
            structureLifecycle = createStructureLifecycle?.Invoke();
            returnToMenu = createReturnToMenu?.Invoke();
            challenges = createChallenges?.Invoke();
            prestige = createPrestige?.Invoke();
            research = createResearch?.Invoke();
            return new AutomataServiceCycleRuntime(
                hostDependencies.ReadLifecycleEpoch,
                configurationPublication,
                host,
                featureRuntimes.ToArray(),
                configurationGeneration,
                discoveryTreeOffers,
                spellWorkbench,
                spellComposition,
                spellLoadout,
                targeting,
                genericDiscovery,
                equipmentLoadout,
                alchemyLoadout,
                ritualLifecycle,
                genericLevel,
                craftingStations,
                craftingInstances,
                loadouts,
                harvestLifecycle,
                plotLifecycle,
                structureLifecycle,
                returnToMenu,
                challenges,
                prestige,
                research);
        }
        catch
        {
            discoveryTreeOffers?.Dispose();
            spellWorkbench?.Dispose();
            spellComposition?.Dispose();
            spellLoadout?.Dispose();
            targeting?.Dispose();
            genericDiscovery?.Dispose();
            equipmentLoadout?.Dispose();
            alchemyLoadout?.Dispose();
            ritualLifecycle?.Dispose();
            genericLevel?.Dispose();
            craftingStations?.Dispose();
            craftingInstances?.Dispose();
            loadouts?.Dispose();
            harvestLifecycle?.Dispose();
            plotLifecycle?.Dispose();
            structureLifecycle?.Dispose();
            returnToMenu?.Dispose();
            challenges?.Dispose();
            prestige?.Dispose();
            research?.Dispose();
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
