using System.Collections.Generic;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal sealed class SpellLevelFeatureContribution : AutomataFeatureContribution
{
    internal const string FeatureId = "SpellLevel";

    internal SpellLevelFeatureContribution() : base(FeatureId, "Spell Leveling", SpellLevelServicePolicies.ServiceId.Value,
        null, null, 0, 0) { }

    internal override bool IsConfigured(SuiteRuntimeConfiguration config) =>
        config.AutoBuy.AutoLevelSpells;

    internal override string? BlockedBy(SuiteRuntimeConfiguration config) =>
        config.AutoBuy.Mode != AutoBuyOperationMode.Active
            ? "Auto Buy is disabled by configuration." : null;

    internal override IEnumerable<AutomataActionFamilyDemand> OwnershipDemands { get; } =
        new AutomataActionFamilyDemand[]
        {
            new(AutomationActionFamily.SpellLevelPurchase, FeatureId,
                "Automata Spell Leveling", config => config.CanStartAutoBuyActively && config.AutoBuy.AutoLevelSpells),
        };

    internal override IAutomataServiceCycleFeature CreateService(AutomataFeatureComposition context)
    {
        return new SpellLevelServiceCycleFeature(new SpellLevelFeatureDependencies(
            context.ReadLifecycleEpoch, () => context.Ownership.OwnsFamily(AutomationActionFamily.SpellLevelPurchase),
            context.SpellLevelCapability, context.Statuses.Get(Id), context.Warn));
    }
}
