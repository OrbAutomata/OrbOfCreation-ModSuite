using System;
using System.Linq;
using HarmonyLib;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// What a feature runs from the suite's composition to its shutdown whether or not automation does:
/// <b>Automation enabled</b> and <b>STOP ALL</b> never reach it, only its own settings do.
/// </summary>
internal interface IAutomataStandingFeature : IDisposable
{
    void ObserveConfiguration(SuiteRuntimeConfiguration configuration, ConfigGeneration configurationGeneration);

    /// <summary>Once per frame, with whether gameplay is ready and the lifecycle it belongs to.</summary>
    void Tick(bool lifecycleReady, long lifecycleGeneration);
}

/// <summary>What a standing feature is composed from, at the same moment the suite installs its patches.</summary>
internal sealed class AutomataStandingComposition
{
    internal Harmony Harmony { get; init; } = null!;
    internal AutomataFeatureStatuses Statuses { get; init; } = null!;
    internal SuiteRuntimeConfiguration Configuration { get; init; } = null!;
    internal ConfigGeneration ConfigurationGeneration { get; init; }
    internal Action<string> Warn { get; init; } = null!;
}

/// <summary>Every catalog feature's standing part, driven by the plugin apart from the automation runtime.</summary>
internal sealed class AutomataStandingFeatures : IDisposable
{
    private readonly IAutomataStandingFeature[] _features;

    private AutomataStandingFeatures(IAutomataStandingFeature[] features) => _features = features;

    internal static AutomataStandingFeatures Compose(AutomataStandingComposition context) =>
        new(AutomataFeatureCatalog.All.Select(feature => feature.CreateStanding(context))
            .OfType<IAutomataStandingFeature>().ToArray());

    internal void ObserveConfiguration(SuiteRuntimeConfiguration configuration, ConfigGeneration configurationGeneration)
    {
        foreach (var feature in _features) feature.ObserveConfiguration(configuration, configurationGeneration);
    }

    internal void Tick(bool lifecycleReady, long lifecycleGeneration)
    {
        foreach (var feature in _features) feature.Tick(lifecycleReady, lifecycleGeneration);
    }

    public void Dispose()
    {
        for (var index = _features.Length - 1; index >= 0; index--) _features[index].Dispose();
    }
}
