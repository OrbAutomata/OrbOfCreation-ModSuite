using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal sealed class QualityOfLifeFeatureContribution : AutomataFeatureContribution
{
    internal const string FeatureId = "QualityOfLife";
    internal static readonly GameActionFamily ActionFamily =
        new("Quality of Life", AutomationActionFamily.PassiveNotificationMute);

    internal QualityOfLifeFeatureContribution() : base(FeatureId, "Quality of Life",
        QualityOfLifeService.ServiceId.Value, "QualityOfLife", "Quality of Life", 19, 8,
        iconIdentity: NativeFeatureIcon.General) { }

    internal override AutomationFeatureSettings Settings => QualityOfLifeSettings.Definition;

    internal override bool IsConfigured(SuiteRuntimeConfiguration config) =>
        config.QualityOfLife.Mode == QualityOfLifeOperationMode.Active;

    internal override string? ConfiguredConstraint(SuiteRuntimeConfiguration config) =>
        config.QualityOfLife.MutePassiveNotifications ? null : "No Quality of Life convenience is selected.";

    internal override IEnumerable<AutomataActionFamilyDemand> OwnershipDemands { get; } =
        new AutomataActionFamilyDemand[]
        {
            new(AutomationActionFamily.PassiveNotificationMute, FeatureId, "Quality of Life",
                QualityOfLifePolicy.IsOperational),
        };

    internal override IAutomataServiceCycleFeature CreateService(AutomataFeatureComposition context)
    {
        var family = context.Ownership.Family(ActionFamily);
        return new QualityOfLifeServiceCycleFeature(new QualityOfLifeFeatureDependencies(
            context.RegistryResolver, context.ReadLifecycleEpoch,
            () => context.Ownership.OwnsFamily(AutomationActionFamily.PassiveNotificationMute),
            family.TryCapture, family.Failure, context.Statuses.Get(Id), context.Warn));
    }
}
