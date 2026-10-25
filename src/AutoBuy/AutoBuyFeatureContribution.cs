using System.Collections.Generic;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal sealed class AutoBuyFeatureContribution : AutomataFeatureContribution
{
    internal const string FeatureId = "AutoBuy";

    internal AutoBuyFeatureContribution() : base(FeatureId, "Auto Buy", AutoBuyServicePolicies.ServiceId.Value,
        "AutoBuy", "Auto Buy", 0, 0, iconIdentity: NativeFeatureIcon.AutoBuy) { }

    internal override bool IsConfigured(SuiteRuntimeConfiguration config) =>
        config.AutoBuy.Mode == AutoBuyOperationMode.Active;

    internal override string? ConfiguredConstraint(SuiteRuntimeConfiguration config) =>
        !config.AutoBuy.IncludeStructures && !config.AutoBuy.IncludeUpgrades
            ? "Auto Buy has neither structures nor upgrades selected to buy." : null;

    internal override IEnumerable<AutomataActionFamilyDemand> OwnershipDemands { get; } =
        new AutomataActionFamilyDemand[]
        {
            new(AutomationActionFamily.StructurePurchase, "AutoBuy.Structures",
                "Automata Auto Buy Structures", config => config.CanStartAutoBuyActively && config.AutoBuy.IncludeStructures),
            new(AutomationActionFamily.UpgradePurchase, "AutoBuy.Upgrades",
                "Automata Auto Buy Upgrades", config => config.CanStartAutoBuyActively && config.AutoBuy.IncludeUpgrades),
            AutomataActionFamilyDemand.MultiBuy(config => config.CanStartAutoBuyActively && config.AutoBuy.IncludeUpgrades),
        };

    internal override AutomationFeatureControlRegistration CreateControl(
        AutomataConfigurationStore configuration, AutomataFeatureStatuses statuses,
        SpellLevelCapabilityState capability, MentorConfig mentor)
    {
        var control = new AutoBuyToggleControl(configuration, () => capability.Current,
            () => statuses.Get(Id).Current, () => statuses.Get(SpellLevelFeatureContribution.FeatureId).Current);
        return Control(new AutoBuyTooltip(control), statuses, control.Toggle);
    }

    internal override IAutomataServiceCycleFeature CreateService(AutomataFeatureComposition context)
    {
        return new AutoBuyServiceCycleFeature(new AutoBuyFeatureDependencies(
            context.ReadLifecycleEpoch,
            () => context.Ownership.EffectiveAutoBuyOwnership(context.Configuration.Current.AutoBuy),
            context.RuntimeDiagnostics, context.Statuses.Get(Id), context.RefusalBundles,
            context.ReportInvariant, context.Warn
#if SERVICE_CYCLE_PROFILE
            , gameMcpOwnership: context.Ownership.OwnsAutoBuy
#endif
            , info: context.Info));
    }
}

/// <summary>Buy-specific projections over the generic family leases.</summary>
internal static class AutoBuyOwnership
{
    internal static bool OwnsAutoBuy(this AutomataActionFamilyOwnership ownership, AutoBuyCandidateKind kind) => kind switch
    {
        AutoBuyCandidateKind.Structure => ownership.OwnsFamily(AutomationActionFamily.StructurePurchase),
        AutoBuyCandidateKind.Upgrade =>
            (ownership.OwnsPermanently(AutomationActionFamily.UpgradePurchase) &&
             ownership.OwnsPermanently(AutomationActionFamily.NativeMultiBuyOverride)) ||
            (ownership.OwnsForCurrentOperation(AutomationActionFamily.UpgradePurchase) &&
             ownership.OwnsForCurrentOperation(AutomationActionFamily.NativeMultiBuyOverride)),
        _ => false,
    };

    internal static AutoBuyCandidateKinds EffectiveAutoBuyOwnership(
        this AutomataActionFamilyOwnership ownership, AutoBuyConfiguration config)
    {
        var owned = AutoBuyCandidateKinds.None;
        if (!config.IncludeStructures || ownership.OwnsAutoBuy(AutoBuyCandidateKind.Structure))
            owned |= AutoBuyCandidateKinds.Structures;
        if (!config.IncludeUpgrades || ownership.OwnsAutoBuy(AutoBuyCandidateKind.Upgrade))
            owned |= AutoBuyCandidateKinds.Upgrades;
        return owned;
    }

}
