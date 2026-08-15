#if SERVICE_CYCLE_PROFILE
using System;
using OrbMentor;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata.GameMcp;

/// <summary>
/// The seven breakers the player sees, and the exact BepInEx setting behind each.
/// </summary>
/// <remarks>
/// Every one of these is an <c>{Disabled, Active}</c> enum with no third state, which is why the
/// MCP surface for them is a boolean rather than a serialized-value write: the player concept is a
/// breaker that is green or gray. Everything else a feature can be configured with — thresholds,
/// roles, allowlists — stays on <c>suite_config_set</c>; these seven entries are the breakers' own
/// and that verb refuses them, so a feature has one door and not two.
/// </remarks>
internal sealed class GameMcpAutomationFeature
{
    internal GameMcpAutomationFeature(
        string name,
        string displayName,
        string section,
        string key,
        string summary,
        Func<SuiteRuntimeConfiguration, bool> isOn)
    {
        Name = name;
        DisplayName = displayName;
        Section = section;
        Key = key;
        Summary = summary;
        IsOn = isOn;
    }

    internal string Name { get; }
    internal string DisplayName { get; }
    internal string Section { get; }
    internal string Key { get; }
    internal string Summary { get; }
    internal Func<SuiteRuntimeConfiguration, bool> IsOn { get; }

    internal string SerializedValue(bool on) => on ? "Active" : "Disabled";
}

internal static class GameMcpAutomationFeatures
{
    internal static readonly GameMcpAutomationFeature[] All =
    {
        new(
            "auto_buy",
            "Auto Buy",
            "AutoBuy",
            "Mode",
            "Buys affordable structures and upgrades through the game's own purchase queue.",
            config => config.AutoBuy.Mode == AutoBuyOperationMode.Active),
        new(
            "auto_cast",
            "Auto Cast",
            "AutoCast",
            "Mode",
            "Fires equipped spells when their resources and cooldowns allow it.",
            config => config.AutoCast.Mode == AutoCastOperationMode.Active),
        new(
            "auto_concept",
            "Auto Concept",
            "AutoConcept",
            "Mode",
            "Trains the lowest-mastery discovered Scholar concepts in the Active Concepts list.",
            config => config.AutoConcept.Mode == AutoConceptOperationMode.Active),
        new(
            "auto_harvest",
            "Auto Harvest",
            "AutoHarvest",
            "Mode",
            "Collects ready fruit trees and treasure trees on the Agromancy plots.",
            config => config.AutoHarvest.Mode == AutoHarvestOperationMode.Active),
        new(
            "auto_items",
            "Auto Items",
            "AutoItems",
            "Mode",
            "Uses eligible Scrolls, Relics, and approved temporary items.",
            config => config.AutoItems.Mode == AutoItemsOperationMode.Active),
        new(
            "auto_scribe",
            "Auto Scribe",
            "AutoScribe",
            "Mode",
            "Writes Scrolls at the Scribe for the roles that are configured for it.",
            config => config.AutoScribe.Mode == AutoScribeOperationMode.Active),
        new(
            "mentor",
            "Orb Mentor",
            "General",
            "Mode",
            "Shares mastery experience from advanced spells, artifacts, and recipes with lagging ones.",
            config => config.Mentor.Mode == MentorOperationMode.Active),
    };

    internal static string[] Names()
    {
        var names = new string[All.Length];
        for (var index = 0; index < All.Length; index++) names[index] = All[index].Name;
        return names;
    }

    /// <summary>
    /// The two suite-wide switches that silence every feature at once. Every answer about what is
    /// on carries them under the same condition, whether it lists the buttons or commits a flip.
    /// </summary>
    internal static void AddSuiteOverrides(
        GameMcpObjectBuilder target,
        SuiteRuntimeConfiguration configuration)
    {
        if (configuration.Safety.EmergencyDisable) target["emergencyStop"] = true;
        if (!configuration.General.Enabled) target["automationEnabled"] = false;
    }

    /// <summary>
    /// Whether one configuration entry is the setting behind a breaker. A feature had two doors —
    /// the breaker and its <c>Mode</c> line in the settings pen — and a live round called both in
    /// one breath to be sure they were the same switch. The breaker is the door; this is what lets
    /// the pen refuse the second one.
    /// </summary>
    internal static bool IsBreakerSetting(string section, string key)
    {
        for (var index = 0; index < All.Length; index++)
        {
            if (string.Equals(All[index].Section, section, StringComparison.Ordinal) &&
                string.Equals(All[index].Key, key, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    internal static bool TryGet(string name, out GameMcpAutomationFeature feature)
    {
        for (var index = 0; index < All.Length; index++)
        {
            if (!string.Equals(All[index].Name, name, StringComparison.Ordinal)) continue;
            feature = All[index];
            return true;
        }

        feature = null!;
        return false;
    }
}
#endif
