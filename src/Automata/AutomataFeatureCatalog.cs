using System;
using System.Collections.Generic;
using System.Linq;
using OrbMentor;

namespace OrbAutomata;

/// <summary>The one explicit feature order. Infrastructure is composed around these features.</summary>
internal static class AutomataFeatureCatalog
{
    internal static IReadOnlyList<AutomataFeatureContribution> All { get; } =
        Array.AsReadOnly(new AutomataFeatureContribution[]
        {
            new AutoLevelsFeatureContribution(),
            new QualityOfLifeFeatureContribution(),
            new AutoItemsFeatureContribution(),
            new AutoScribeFeatureContribution(),
            new AutoHarvestFeatureContribution(),
            new AutoBuyFeatureContribution(),
            new SpellLevelFeatureContribution(),
            new AutoCastFeatureContribution(),
            new AutoConceptFeatureContribution(),
            new MentorFeatureContribution(),
        });

    internal static AutomataFeatureContribution? ForConfigurationSection(string section) =>
        All.FirstOrDefault(feature => feature.ConfigurationSection == section);

    internal static IAutomataServiceCycleFeature[] CreateServices(
        AutomataFeatureComposition context,
        IAutomataServiceCycleFeature worldCollection,
        IAutomataServiceCycleFeature strategist) =>
        new[] { worldCollection }.Concat(All.Select(feature => feature.CreateService(context)))
            .Concat(new[] { strategist }).ToArray();
}
