using System.Collections.Generic;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal sealed class AutoConceptFeatureContribution : AutomataFeatureContribution
{
    internal const string FeatureId = "AutoConcept";

    internal AutoConceptFeatureContribution() : base(FeatureId, "Auto Concept", AutoConceptServicePolicies.ServiceId.Value,
        "AutoConcept", "Auto Concept", 15, 2) { }

    internal override bool IsConfigured(SuiteRuntimeConfiguration config) =>
        config.AutoConcept.Mode == AutoConceptOperationMode.Active;

    internal override IEnumerable<AutomataActionFamilyDemand> OwnershipDemands { get; } =
        new AutomataActionFamilyDemand[]
        {
            new(AutomationActionFamily.ConceptAssignment, FeatureId,
                "Automata Auto Concept", config => config.CanStartAutoConceptActively),
        };

    internal override AutomationFeatureControlRegistration CreateControl(
        AutomataConfigurationStore configuration, AutomataFeatureStatuses statuses,
        SpellLevelCapabilityState capability, MentorConfig mentor)
    {
        var control = new AutoConceptToggleControl(configuration, () => statuses.Get(Id).Current);
        return Control(new AutoConceptTooltip(control), statuses, control.Toggle);
    }

    internal override IAutomataServiceCycleFeature CreateService(AutomataFeatureComposition context)
    {
        return new AutoConceptServiceCycleFeature(new AutoConceptFeatureDependencies(
            context.ReadLifecycleEpoch, () => context.Ownership.OwnsFamily(AutomationActionFamily.ConceptAssignment),
            context.Statuses.Get(Id), context.Warn));
    }
}
