using System;
using System.Collections.Generic;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

/// <summary>A feature's metadata and composition, kept beside the feature that owns its policy.</summary>
internal abstract class AutomataFeatureContribution
{
    protected AutomataFeatureContribution(string id, string displayName, string serviceId,
        string? configurationSection, string? pageLabel, int pageOrder, int controlOrder,
        string? traceName = null, NativeFeatureIcon iconIdentity = NativeFeatureIcon.Unknown,
        string? modeConfigurationSection = null)
    {
        Id = id;
        DisplayName = displayName;
        ServiceId = serviceId;
        ConfigurationSection = configurationSection;
        PageLabel = pageLabel;
        PageOrder = pageOrder;
        ControlOrder = controlOrder;
        TraceName = traceName ?? displayName;
        IconIdentity = iconIdentity;
        ModeConfigurationSection = modeConfigurationSection ?? configurationSection;
    }

    internal string Id { get; }
    internal string DisplayName { get; }
    internal string ServiceId { get; }
    internal string? ConfigurationSection { get; }
    internal string? PageLabel { get; }
    internal int PageOrder { get; }
    internal int ControlOrder { get; }
    internal string TraceName { get; }
    internal NativeFeatureIcon IconIdentity { get; }
    internal string? ModeConfigurationSection { get; }
    internal virtual AutomationFeatureSettings? Settings => null;
    internal abstract bool IsConfigured(SuiteRuntimeConfiguration configuration);
    internal virtual string? BlockedBy(SuiteRuntimeConfiguration configuration) => null;
    internal virtual string? ConfiguredConstraint(SuiteRuntimeConfiguration configuration) => null;
    internal virtual IEnumerable<AutomataActionFamilyDemand> OwnershipDemands =>
        Array.Empty<AutomataActionFamilyDemand>();
    internal abstract IAutomataServiceCycleFeature CreateService(AutomataFeatureComposition context);
    internal virtual AutomationFeatureControlRegistration? CreateControl(
        AutomataConfigurationStore configuration, AutomataFeatureStatuses statuses,
        SpellLevelCapabilityState capability, MentorConfig mentor) => Settings is null ? null :
        Control(new AutomationFeatureSettingsTooltip(this, () => configuration.Current,
            () => statuses.Get(Id).Current), statuses, () => configuration.ToggleFeature(Id));

    protected AutomationFeatureControlRegistration Control(ITooltipable tooltip,
        AutomataFeatureStatuses statuses, Action toggle) =>
        new(Id, PageLabel!, DisplayName, tooltip, () => statuses.Get(Id).Current, toggle, IconIdentity);
}

/// <summary>The root's existing application dependencies, passed once to feature construction.</summary>
internal sealed class AutomataFeatureComposition
{
    internal Func<long> ReadLifecycleEpoch { get; init; } = null!;
    internal TypedRegistryResolver RegistryResolver { get; init; } = null!;
    internal AutomataActionFamilyOwnership Ownership { get; init; } = null!;
    internal AutomataConfigurationStore Configuration { get; init; } = null!;
    internal AutomataFeatureStatuses Statuses { get; init; } = null!;
    internal SpellLevelCapabilityState SpellLevelCapability { get; init; } = null!;
    internal AutoCastManualPauseState ManualCastPause { get; init; } = null!;
    internal string AuditedBaselineId { get; init; } = string.Empty;
    internal RuntimeDiagnosticsRegistry RuntimeDiagnostics { get; init; } = null!;
    internal IAutoBuyRefusalBundlePort RefusalBundles { get; init; } = null!;
    internal Action<string> ReportInvariant { get; init; } = null!;
    internal Action<string> Warn { get; init; } = null!;
    internal Action<string> Info { get; init; } = null!;
}

/// <summary>A single-family claim and the feature-local predicate that needs it.</summary>
internal sealed class AutomataActionFamilyDemand
{
    internal AutomataActionFamilyDemand(AutomationActionFamily family, string ownerId,
        string ownerName, Func<SuiteRuntimeConfiguration, bool> isRequired,
        string? disabledReason = null)
    {
        Family = family;
        OwnerId = ownerId;
        OwnerName = ownerName;
        IsRequired = isRequired;
        DisabledReason = disabledReason ?? ownerName + " is not enabled by configuration.";
    }

    internal AutomationActionFamily Family { get; }
    internal string OwnerId { get; }
    internal string OwnerName { get; }
    internal Func<SuiteRuntimeConfiguration, bool> IsRequired { get; }
    internal string DisabledReason { get; }

    // Both consumers request the same scope; the host merges their predicates with OR.
    internal static AutomataActionFamilyDemand MultiBuy(Func<SuiteRuntimeConfiguration, bool> required) =>
        new(AutomationActionFamily.NativeMultiBuyOverride, "NativeMultiBuy",
            "Automata Native Multi-Buy Scope", required,
            "No enabled service currently requires NativeMultiBuyOverride.");
}
