using System.Collections.Generic;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal sealed class AutoScribeFeatureContribution : AutomataFeatureContribution
{
    internal static readonly GameActionFamily ActionFamily =
        new("Auto Scribe", AutomationActionFamily.CraftingQueueSubmission);

    internal const string FeatureId = "AutoScribe";

    internal AutoScribeFeatureContribution() : base(FeatureId, "Auto Scribe", AutoScribeServicePolicies.ServiceId.Value,
        "AutoScribe", "Auto Scribe", 19, 6, iconIdentity: NativeFeatureIcon.AutoScribe) { }

    internal override bool IsConfigured(SuiteRuntimeConfiguration config) =>
        config.AutoScribe.Mode == AutoScribeOperationMode.Active;

    internal override string? BlockedBy(SuiteRuntimeConfiguration config) =>
        config.AutoItems.Mode != AutoItemsOperationMode.Active || !config.AutoItems.UseScrolls
            ? "Auto Scribe needs Auto Items running with Scrolls on." : null;

    internal override string? ConfiguredConstraint(SuiteRuntimeConfiguration config) =>
        AutoScribeRoleSelection.ConfigurationProblem(config.AutoScribe.Roles);

    internal override IEnumerable<AutomataActionFamilyDemand> OwnershipDemands { get; } =
        new AutomataActionFamilyDemand[]
        {
            new(AutomationActionFamily.CraftingQueueSubmission, FeatureId,
                "Automata Auto Scribe", config => AutoScribeConfigurationPolicy.IsOperational(config),
                "Committed configuration no longer enables Auto Scribe production."),
        };

    internal override AutomationFeatureControlRegistration CreateControl(
        AutomataConfigurationStore configuration, AutomataFeatureStatuses statuses,
        SpellLevelCapabilityState capability, MentorConfig mentor)
    {
        var control = new AutoScribeToggleControl(configuration, () => statuses.Get(Id).Current);
        return Control(new AutoScribeTooltip(control), statuses, control.Toggle);
    }

    internal override IAutomataServiceCycleFeature CreateService(AutomataFeatureComposition context)
    {
        var catalog = new AutoScribeIdentityCatalog();
        if (!catalog.TryGetProfile(context.AuditedBaselineId, out var profile))
            return new AutoScribeUnavailableServiceCycleFeature(context.Statuses.Get(Id));
        var family = context.Ownership.Family(AutoScribeFeatureContribution.ActionFamily);
        return new AutoScribeServiceCycleFeature(new AutoScribeFeatureDependencies(
            context.RegistryResolver, profile, context.ReadLifecycleEpoch,
            () => context.Ownership.OwnsPermanently(AutomationActionFamily.CraftingQueueSubmission), family.TryCapture, family.Failure,
            context.Statuses.Get(Id), context.Warn));
    }
}
