using System.Collections.Generic;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal sealed class AutoHarvestFeatureContribution : AutomataFeatureContribution
{
    internal static readonly GameActionFamily ActionFamily =
        new(GameActionFamily.McpOperation, AutomationActionFamily.HarvestAction);

    internal const string FeatureId = "AutoHarvest";

    internal AutoHarvestFeatureContribution() : base(FeatureId, "Auto Harvest", AutoHarvestServicePolicies.ServiceId.Value,
        "AutoHarvest", "Auto Harvest", 17, 3, iconIdentity: NativeFeatureIcon.AutoHarvest) { }

    internal override bool IsConfigured(SuiteRuntimeConfiguration config) =>
        config.AutoHarvest.Mode == AutoHarvestOperationMode.Active;

    internal override string? ConfiguredConstraint(SuiteRuntimeConfiguration config) =>
        !config.AutoHarvest.CollectFruitTrees && !config.AutoHarvest.CollectTreasureTrees
            ? "Auto Harvest has neither fruit trees nor treasure trees selected." : null;

    internal override IEnumerable<AutomataActionFamilyDemand> OwnershipDemands { get; } =
        new AutomataActionFamilyDemand[]
        {
            new(AutomationActionFamily.HarvestAction, FeatureId,
                "Automata Auto Harvest", config => AutoHarvestConfigurationPolicy.IsOperational(config)),
        };

    internal override AutomationFeatureControlRegistration CreateControl(
        AutomataConfigurationStore configuration, AutomataFeatureStatuses statuses,
        SpellLevelCapabilityState capability, MentorConfig mentor)
    {
        var control = new AutoHarvestToggleControl(configuration, () => statuses.Get(Id).Current);
        return Control(new AutoHarvestTooltip(control), statuses, control.Toggle);
    }

    internal override IAutomataServiceCycleFeature CreateService(AutomataFeatureComposition context)
    {
        var family = context.Ownership.Family(AutoHarvestFeatureContribution.ActionFamily);
        return new AutoHarvestServiceCycleFeature(new AutoHarvestFeatureDependencies(
            context.RegistryResolver, () => context.Ownership.OwnsFamily(AutomationActionFamily.HarvestAction),
            family.TryCapture, context.RuntimeDiagnostics, context.Statuses.Get(Id), warn: context.Warn));
    }
}
