using System.Collections.Generic;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal sealed class AutoItemsFeatureContribution : AutomataFeatureContribution
{
    internal static readonly GameActionFamily ActionFamily =
        new("Auto Items", AutomationActionFamily.ConsumableUse,
            AutomationActionFamily.NativeMultiBuyOverride);

    internal const string FeatureId = "AutoItems";

    internal AutoItemsFeatureContribution() : base(FeatureId, "Auto Items", AutoItemsServicePolicies.ServiceId.Value,
        "AutoItems", "Auto Items", 18, 5) { }

    internal override bool IsConfigured(SuiteRuntimeConfiguration config) =>
        config.AutoItems.Mode == AutoItemsOperationMode.Active;

    internal override string? ConfiguredConstraint(SuiteRuntimeConfiguration config) =>
        !AutoItemsConfigurationPolicy.HasEnabledFamily(config.AutoItems)
            ? "Auto Items has no Scrolls, Relics or approved temporary items selected." : null;

    internal override IEnumerable<AutomataActionFamilyDemand> OwnershipDemands { get; } =
        new AutomataActionFamilyDemand[]
        {
            new(AutomationActionFamily.ConsumableUse, FeatureId,
                "Automata Auto Items", config => AutoItemsConfigurationPolicy.IsOperational(config),
                "Committed configuration no longer enables Auto Items consumable use."),
            AutomataActionFamilyDemand.MultiBuy(config => AutoItemsConfigurationPolicy.IsOperational(config)),
        };

    internal override AutomationFeatureControlRegistration CreateControl(
        AutomataConfigurationStore configuration, AutomataFeatureStatuses statuses,
        SpellLevelCapabilityState capability, MentorConfig mentor)
    {
        var control = new AutoItemsToggleControl(configuration, () => statuses.Get(Id).Current);
        return Control(new AutoItemsTooltip(control), statuses, control.Toggle);
    }

    internal override IAutomataServiceCycleFeature CreateService(AutomataFeatureComposition context)
    {
        var family = context.Ownership.Family(AutoItemsFeatureContribution.ActionFamily);
        return new AutoItemsServiceCycleFeature(new AutoItemsFeatureDependencies(
            context.RegistryResolver, context.ReadLifecycleEpoch, () => context.Ownership.Owns(AutoItemsFeatureContribution.ActionFamily),
            family.TryCapture, family.Failure, context.Statuses.Get(Id), context.Warn));
    }
}
