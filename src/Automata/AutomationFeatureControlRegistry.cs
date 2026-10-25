using System;
using System.Collections.Generic;
using System.Linq;
using OrbMentor;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutomationFeatureControlRegistration
{
    private readonly Func<FeatureStatusSnapshot> _readStatus;
    private readonly Action _toggle;

    internal AutomationFeatureControlRegistration(
        string featureId,
        string pageLabel,
        string displayName,
        ITooltipable tooltip,
        Func<FeatureStatusSnapshot> readStatus,
        Action toggle)
    {
        FeatureId = RequireText(featureId, nameof(featureId));
        PageLabel = RequireText(pageLabel, nameof(pageLabel));
        DisplayName = RequireText(displayName, nameof(displayName));
        Tooltip = tooltip ?? throw new ArgumentNullException(nameof(tooltip));
        _readStatus = readStatus ?? throw new ArgumentNullException(nameof(readStatus));
        _toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
    }

    internal string FeatureId { get; }
    internal string PageLabel { get; }
    internal string DisplayName { get; }
    internal ITooltipable Tooltip { get; }
    internal FeatureStatusSnapshot Status => _readStatus();
    internal void Toggle() => _toggle();

    private static string RequireText(string value, string parameterName)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0
            ? throw new ArgumentException("A non-empty value is required.", parameterName)
            : normalized;
    }
}

/// <summary>
/// The one registered automation-feature control roster shared by the Mods headers and the
/// gameplay feature drawer. Construction consumers enumerate this roster; they do not
/// maintain their own feature lists.
/// </summary>
internal sealed class AutomationFeatureControlRegistry
{
    private readonly IReadOnlyList<AutomationFeatureControlRegistration> _features;
    private readonly IReadOnlyDictionary<string, AutomationFeatureControlRegistration> _byPage;

    internal AutomationFeatureControlRegistry(
        IEnumerable<AutomationFeatureControlRegistration> features)
    {
        if (features is null) throw new ArgumentNullException(nameof(features));
        var materialized = features.ToArray();
        if (materialized.Length == 0)
            throw new ArgumentException("At least one automation feature is required.", nameof(features));
        if (materialized.Select(feature => feature.FeatureId).Distinct(StringComparer.Ordinal).Count() !=
            materialized.Length)
            throw new ArgumentException("Automation feature IDs must be unique.", nameof(features));
        if (materialized.Select(feature => feature.PageLabel).Distinct(StringComparer.Ordinal).Count() !=
            materialized.Length)
            throw new ArgumentException("Automation feature page labels must be unique.", nameof(features));
        _features = materialized;
        _byPage = materialized.ToDictionary(feature => feature.PageLabel, StringComparer.Ordinal);
    }

    internal IReadOnlyList<AutomationFeatureControlRegistration> Features => _features;

    internal bool TryGet(
        string pageLabel,
        out AutomationFeatureControlRegistration registration) =>
        _byPage.TryGetValue(pageLabel, out registration!);

    internal static AutomationFeatureControlRegistry Create(
        AutomataConfigurationStore configuration,
        AutomataFeatureStatuses statuses,
        SpellLevelCapabilityState spellLevelCapability,
        MentorConfig mentor)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (statuses is null) throw new ArgumentNullException(nameof(statuses));
        if (spellLevelCapability is null) throw new ArgumentNullException(nameof(spellLevelCapability));
        if (mentor is null) throw new ArgumentNullException(nameof(mentor));

        return new AutomationFeatureControlRegistry(AutomataFeatureCatalog.All
            .OrderBy(feature => feature.ControlOrder)
            .Select(feature => feature.CreateControl(configuration, statuses, spellLevelCapability, mentor))
            .OfType<AutomationFeatureControlRegistration>());
    }
}
