using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.World;

namespace OrbMentor;

internal sealed class MentorFeatureContribution : AutomataFeatureContribution
{
    internal const string FeatureId = "Mentor";
    internal const string PageName = "Mentor";
    internal const int ConfigurationPageOrder = 19;

    internal MentorFeatureContribution() : base(FeatureId, "Orb Mentor", MentorServicePolicies.ServiceId.Value,
        null, PageName, ConfigurationPageOrder, 4, traceName: PageName,
        iconIdentity: NativeFeatureIcon.Mentor, modeConfigurationSection: "General") { }

    internal override bool IsConfigured(SuiteRuntimeConfiguration config) =>
        config.Mentor.Mode == MentorOperationMode.Active;

    internal override IEnumerable<AutomataActionFamilyDemand> OwnershipDemands { get; } =
        new AutomataActionFamilyDemand[]
        {
            new(AutomationActionFamily.SpellMasteryExperienceGrant, FeatureId, "Orb Mentor",
                config => MentorConfigurationPolicy.IsOperational(config)),
            new(AutomationActionFamily.ArtifactMasteryExperienceGrant, FeatureId, "Orb Mentor",
                config => MentorConfigurationPolicy.IsOperational(config) && config.Mentor.ArtifactsEnabled),
            new(AutomationActionFamily.AlchemyMasteryExperienceGrant, FeatureId, "Orb Mentor",
                config => MentorConfigurationPolicy.IsOperational(config) && config.Mentor.AlchemyEnabled),
        };

    internal override AutomationFeatureControlRegistration CreateControl(
        AutomataConfigurationStore configuration, AutomataFeatureStatuses statuses,
        SpellLevelCapabilityState capability, MentorConfig mentor)
    {
        return Control(new MentorTooltip(mentor, () => statuses.Get(Id).Current),
            statuses, configuration.ToggleMentor);
    }

    internal override IAutomataServiceCycleFeature CreateService(AutomataFeatureComposition context)
    {
        return new MentorServiceCycleFeature(new MentorFeatureDependencies(
            context.ReadLifecycleEpoch,
            domain => context.Ownership.TryCaptureFamily(domain switch
            {
                MasteryExperienceDomain.Spell => AutomationActionFamily.SpellMasteryExperienceGrant,
                MasteryExperienceDomain.Artifact => AutomationActionFamily.ArtifactMasteryExperienceGrant,
                _ => AutomationActionFamily.AlchemyMasteryExperienceGrant,
            }), context.Statuses.Get(Id), warn: context.Warn));
    }
}
