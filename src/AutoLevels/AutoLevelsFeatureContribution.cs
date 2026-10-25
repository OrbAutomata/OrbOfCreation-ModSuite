using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
namespace OrbAutomata;
internal sealed class AutoLevelsFeatureContribution : AutomataFeatureContribution
{
    internal const string FeatureId = "AutoLevels";
    internal static readonly GameActionFamily ActionFamily = new("Auto Levels", AutomationActionFamily.SpellComposition);
    internal AutoLevelsFeatureContribution() : base(FeatureId, "Auto Levels", AutoLevelsService.ServiceId.Value,
        "AutoLevels", "Auto Levels", 19, 7, iconIdentity: NativeFeatureIcon.Advanced) { }
    internal override AutomationFeatureSettings Settings => AutoLevelsSettings.Definition;
    internal override bool IsConfigured(SuiteRuntimeConfiguration config) => config.AutoLevels.Mode == AutoLevelsOperationMode.Active;
    internal override IEnumerable<AutomataActionFamilyDemand> OwnershipDemands { get; } =
        new[] { new AutomataActionFamilyDemand(AutomationActionFamily.SpellComposition, FeatureId,
            "Auto Levels", AutoLevelsService.IsOperational) };
    internal override IAutomataServiceCycleFeature CreateService(AutomataFeatureComposition context)
    {
        var family = context.Ownership.Family(ActionFamily);
        return new AutoLevelsServiceCycleFeature(new AutoLevelsFeatureDependencies(context.RegistryResolver,
            context.ReadLifecycleEpoch, () => context.Ownership.Owns(ActionFamily), family.TryCapture,
            family.Failure, context.Statuses.Get(Id), context.Warn));
    }
}
