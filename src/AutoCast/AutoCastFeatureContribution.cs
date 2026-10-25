using System.Collections.Generic;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal sealed class AutoCastFeatureContribution : AutomataFeatureContribution
{
    internal const string FeatureId = "AutoCast";

    internal AutoCastFeatureContribution() : base(FeatureId, "Auto Cast", AutoCastServicePolicies.ServiceId.Value,
        "AutoCast", "Auto Cast", 10, 1) { }

    internal override bool IsConfigured(SuiteRuntimeConfiguration config) =>
        config.AutoCast.Mode == AutoCastOperationMode.Active;

    internal override IEnumerable<AutomataActionFamilyDemand> OwnershipDemands { get; } =
        new AutomataActionFamilyDemand[]
        {
            new(AutomationActionFamily.SpellCast, FeatureId,
                "Automata Auto Cast", config => config.CanStartAutoCastActively),
        };

    internal override AutomationFeatureControlRegistration CreateControl(
        AutomataConfigurationStore configuration, AutomataFeatureStatuses statuses,
        SpellLevelCapabilityState capability, MentorConfig mentor)
    {
        var control = new AutoCastToggleControl(configuration, () => statuses.Get(Id).Current);
        return Control(new AutoCastTooltip(control), statuses, control.Toggle);
    }

    internal override IAutomataServiceCycleFeature CreateService(AutomataFeatureComposition context)
    {
        return new AutoCastServiceCycleFeature(new AutoCastFeatureDependencies(
            context.ReadLifecycleEpoch, () => context.Ownership.OwnsFamily(AutomationActionFamily.SpellCast),
            context.ManualCastPause, context.Statuses.Get(Id), warn: context.Warn));
    }
}
