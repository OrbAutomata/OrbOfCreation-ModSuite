#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
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
        Func<SuiteRuntimeConfiguration, bool> isOn,
        Func<SuiteRuntimeConfiguration, string> policy)
    {
        Name = name;
        DisplayName = displayName;
        Section = section;
        Key = key;
        Summary = summary;
        IsOn = isOn;
        Policy = policy;
    }

    internal string Name { get; }
    internal string DisplayName { get; }
    internal string Section { get; }
    internal string Key { get; }
    internal string Summary { get; }
    internal Func<SuiteRuntimeConfiguration, bool> IsOn { get; }

    /// <summary>
    /// What this feature does under the settings in force, as opposed to <see cref="Summary"/>,
    /// which is what it is for.
    /// </summary>
    internal Func<SuiteRuntimeConfiguration, string> Policy { get; }

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
            config => config.AutoBuy.Mode == AutoBuyOperationMode.Active,
            GameMcpAutomationPolicy.AutoBuy),
        new(
            "auto_cast",
            "Auto Cast",
            "AutoCast",
            "Mode",
            "Fires equipped spells when their resources and cooldowns allow it.",
            config => config.AutoCast.Mode == AutoCastOperationMode.Active,
            GameMcpAutomationPolicy.AutoCast),
        new(
            "auto_concept",
            "Auto Concept",
            "AutoConcept",
            "Mode",
            "Trains the lowest-mastery discovered Scholar concepts in the Active Concepts list.",
            config => config.AutoConcept.Mode == AutoConceptOperationMode.Active,
            GameMcpAutomationPolicy.AutoConcept),
        new(
            "auto_harvest",
            "Auto Harvest",
            "AutoHarvest",
            "Mode",
            "Collects ready fruit trees and treasure trees on the Agromancy plots.",
            config => config.AutoHarvest.Mode == AutoHarvestOperationMode.Active,
            GameMcpAutomationPolicy.AutoHarvest),
        new(
            "auto_items",
            "Auto Items",
            "AutoItems",
            "Mode",
            "Uses eligible Scrolls, Relics, and approved temporary items.",
            config => config.AutoItems.Mode == AutoItemsOperationMode.Active,
            GameMcpAutomationPolicy.AutoItems),
        new(
            "auto_scribe",
            "Auto Scribe",
            "AutoScribe",
            "Mode",
            "Writes Scrolls at the Scribe for the roles that are configured for it.",
            config => config.AutoScribe.Mode == AutoScribeOperationMode.Active,
            GameMcpAutomationPolicy.AutoScribe),
        new(
            "mentor",
            "Orb Mentor",

            // The wire address, as every other feature's is: GameMcpConfigurationAddress maps it
            // onto the [General] Mode line the config file keeps holding.
            "Mentor",
            "Mode",
            "Shares mastery experience from advanced spells, artifacts, and recipes with lagging ones.",
            config => config.Mentor.Mode == MentorOperationMode.Active,
            GameMcpAutomationPolicy.Mentor),
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

/// <summary>
/// What a service does under the settings in force, in the words the settings themselves use.
/// </summary>
/// <remarks>
/// A breaker that answers only <c>on: yes</c> leaves the operator to open the settings pen and read
/// six lines to find out whether anything will happen at all — and a feature with every kind of work
/// switched off reads exactly like a working one. Every sentence here is written from
/// <see cref="SuiteRuntimeConfiguration"/> and nothing else, so arming a breaker never reads the
/// game to describe itself.
/// </remarks>
internal static class GameMcpAutomationPolicy
{
    internal static string AutoBuy(SuiteRuntimeConfiguration configuration)
    {
        var settings = configuration.AutoBuy;
        var kinds = new List<string>(2);
        if (settings.IncludeStructures)
            kinds.Add("structures " + Affordability(settings.StructureAffordability));
        if (settings.IncludeUpgrades)
            kinds.Add("upgrades " + Affordability(settings.UpgradeAffordability));

        if (kinds.Count == 0 && !settings.AutoLevelSpells)
        {
            return "Structures, upgrades, and spell levelling are all switched off, so Auto Buy " +
                "has nothing to buy until one of them is turned back on.";
        }

        var work = kinds.Count == 0
            ? "Buys nothing — structures and upgrades are both off — but levels ready spells"
            : "Buys " + Join(kinds) + (settings.AutoLevelSpells ? ", and levels ready spells" : "");
        return work + ", leaving " + Slots(settings.LeaveQueueSlots) +
            " free in the action queue for you.";
    }

    internal static string AutoCast(SuiteRuntimeConfiguration configuration)
    {
        var settings = configuration.AutoCast;
        var start = settings.StartResourcePercent <= 0f
            ? "Fires equipped spells as soon as their cost is covered"
            : "Fires equipped spells once every capped resource they draw on is at least " +
                Number(settings.StartResourcePercent) + "% full";
        var charge = settings.FullCharge
            ? ", holding chargeable ones to full charge"
            : ", releasing chargeable ones at once without charging";
        var pause = settings.ManualPauseSeconds <= 0f
            ? "."
            : ", and it stays quiet for " + Number(settings.ManualPauseSeconds) +
                "s after you cast one by hand.";
        return start + charge + pause;
    }

    internal static string AutoConcept(SuiteRuntimeConfiguration configuration)
    {
        var settings = configuration.AutoConcept;
        var rotation = settings.SlotManagement switch
        {
            AutoConceptSlotManagementMode.RotateAll =>
                "Replaces an active concept as soon as a discovered one sits at lower mastery",
            AutoConceptSlotManagementMode.PreserveManual =>
                "Fills empty Active Concepts slots and rotates only the quantity it added itself",
            _ => "Rotates a concept only after its full " + Count(settings.TrainingPeriodSeconds) +
                "s training period",
        };
        return rotation + "; it adds quantity only while each drained resource is at least " +
            Number(settings.MinimumResourcePercent) + "% full, keeps " +
            Number(settings.RateReservePercent) +
            "% of that resource's rate in reserve, and takes its own quantity back off if the " +
            "drain ratio falls under " + Number(settings.MinimumDrainRatio) + ".";
    }

    internal static string AutoHarvest(SuiteRuntimeConfiguration configuration)
    {
        var settings = configuration.AutoHarvest;
        var trees = new List<string>(2);
        if (settings.CollectFruitTrees) trees.Add("ready fruit trees");
        if (settings.CollectTreasureTrees) trees.Add("ready treasure trees");
        if (trees.Count == 0)
        {
            return "Neither fruit trees nor treasure trees are selected, so Auto Harvest has " +
                "nothing to collect until one of them is turned back on.";
        }

        return "Collects " + Join(trees) + " on the Agromancy plots, one plot action at a time.";
    }

    internal static string AutoItems(SuiteRuntimeConfiguration configuration)
    {
        var settings = configuration.AutoItems;
        var kinds = new List<string>(3);
        if (settings.UseScrolls) kinds.Add("visible Scrolls");
        if (settings.UseRelics) kinds.Add("visible Relics");
        var approved = Entries(settings.TemporaryItemAllowlist).Count;
        if (approved > 0)
        {
            kinds.Add(approved == 1
                ? "the one approved temporary item"
                : "the " + Count(approved) + " approved temporary items");
        }

        if (kinds.Count == 0)
        {
            return "Scrolls and Relics are both off and no temporary item is approved, so Auto " +
                "Items has nothing to use.";
        }

        return "Uses at most one item from each fresh reading of the world. In play: " +
            Join(kinds) + ".";
    }

    internal static string AutoScribe(SuiteRuntimeConfiguration configuration)
    {
        var roles = Entries(configuration.AutoScribe.Roles);
        if (roles.Count == 0)
        {
            return "Writes at most one Scroll from each fresh reading of the world, in every role " +
                "the Scribe can produce.";
        }

        if (roles.Count == 1 && string.Equals(roles[0], "none", StringComparison.OrdinalIgnoreCase))
            return "No Scribe role is selected, so Auto Scribe has nothing to write.";

        return "Writes at most one Scroll from each fresh reading of the world, in " +
            (roles.Count == 1 ? "one role: " : "these roles: ") + Join(roles) + ".";
    }

    internal static string Mentor(SuiteRuntimeConfiguration configuration)
    {
        var settings = configuration.Mentor;
        var shares = new List<string>(3)
        {
            Number(settings.SpellSharePercent) + "% from " +
                (settings.SpellSourcePolicy == MentorSpellSourcePolicy.EquippedSpells
                    ? "your equipped spells"
                    : "your highest discovered spells"),
        };
        if (settings.ArtifactsEnabled)
            shares.Add(Number(settings.ArtifactSharePercent) + "% from artifacts");
        if (settings.AlchemyEnabled)
            shares.Add(Number(settings.AlchemySharePercent) + "% from alchemy recipes");

        return "Shares mastery experience with the entries that lag behind — " + Join(shares) +
            " — out of " + (settings.EconomyMode == MentorEconomyMode.SharedPool
                ? "one pool everything draws on."
                : "a separate pool per recipient.");
    }

    private static string Affordability(AutoBuyAffordabilityMode mode) => mode switch
    {
        AutoBuyAffordabilityMode.Excess10 =>
            "costing at most a tenth of the resources on hand",
        AutoBuyAffordabilityMode.Excess100 =>
            "costing at most a hundredth of the resources on hand",
        AutoBuyAffordabilityMode.Excess1000 =>
            "costing at most a thousandth of the resources on hand",
        _ => "at any price you can afford",
    };

    private static string Slots(int slots) =>
        slots == 1 ? "one slot" : Count(slots) + " slots";

    private static List<string> Entries(string value)
    {
        var entries = new List<string>();
        if (string.IsNullOrWhiteSpace(value)) return entries;
        foreach (var entry in value.Split(','))
        {
            var trimmed = entry.Trim();
            if (trimmed.Length > 0) entries.Add(trimmed);
        }

        return entries;
    }

    private static string Join(List<string> parts)
    {
        if (parts.Count == 1) return parts[0];
        if (parts.Count == 2) return parts[0] + " and " + parts[1];
        return string.Join(", ", parts.GetRange(0, parts.Count - 1)) +
            ", and " + parts[parts.Count - 1];
    }

    // The pen's own spelling, so a policy sentence and the setting it was read from can never
    // disagree about a number.
    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
#endif
