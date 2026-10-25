using System.Collections.Generic;
using BepInEx.Configuration;
using OrbModding.Common;

namespace OrbMentor;

internal enum MentorOperationMode { Disabled, Active }
internal enum MentorSpellSourcePolicy { EquippedSpells, HighestDiscovered }
public enum MentorEconomyMode { SharedPool, PerRecipient }

internal sealed class MentorConfig
{
    private static readonly IReadOnlyList<ModConfigDependency> ActiveDependencies = new[]
    {
        new ModConfigDependency("General", "Mode", "Active"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> ActiveArtifactDependencies = new[]
    {
        new ModConfigDependency("General", "Mode", "Active"),
        new ModConfigDependency("Artifacts", "Enabled"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> ActiveAlchemyDependencies = new[]
    {
        new ModConfigDependency("General", "Mode", "Active"),
        new ModConfigDependency("Alchemy", "Enabled"),
    };

    private MentorConfig(ConfigEntry<bool> enabled, ConfigEntry<MentorOperationMode> mode,
        ConfigEntry<KeyboardShortcut> shortcut, ConfigEntry<bool> emergencyDisable,
        ConfigEntry<MentorEconomyMode> economyMode, ConfigEntry<MentorSpellSourcePolicy> spellSourcePolicy,
        ConfigEntry<double> sharePercent,
        ConfigEntry<bool> artifactsEnabled, ConfigEntry<double> artifactSharePercent,
        ConfigEntry<bool> alchemyEnabled, ConfigEntry<double> alchemySharePercent)
    {
        Enabled = enabled; Mode = mode; ToggleShortcut = shortcut; EmergencyDisable = emergencyDisable;
        EconomyMode = economyMode; SharePercent = sharePercent;
        SpellSourcePolicy = spellSourcePolicy;
        ArtifactsEnabled = artifactsEnabled; ArtifactSharePercent = artifactSharePercent;
        AlchemyEnabled = alchemyEnabled; AlchemySharePercent = alchemySharePercent;
    }

    public ConfigEntry<bool> Enabled { get; }
    public ConfigEntry<MentorOperationMode> Mode { get; }
    public ConfigEntry<KeyboardShortcut> ToggleShortcut { get; }
    public ConfigEntry<bool> EmergencyDisable { get; }
    public ConfigEntry<MentorEconomyMode> EconomyMode { get; }
    public ConfigEntry<MentorSpellSourcePolicy> SpellSourcePolicy { get; }
    public ConfigEntry<double> SharePercent { get; }
    public ConfigEntry<bool> ArtifactsEnabled { get; }
    public ConfigEntry<double> ArtifactSharePercent { get; }
    public ConfigEntry<bool> AlchemyEnabled { get; }
    public ConfigEntry<double> AlchemySharePercent { get; }
    public bool Active => Enabled.Value && Mode.Value == MentorOperationMode.Active && !EmergencyDisable.Value;

    public static MentorConfig Bind(ConfigFile file)
    {
        var result = TryBind(file);
        if (!result.Success) throw new System.InvalidOperationException(result.Status.Reason);
        return result.Config!;
    }

    public static ConfigurationSchemaBindResult<MentorConfig> TryBind(ConfigFile file) =>
        ConfigurationSchemaTransaction.Bind(
            PluginIds.SuiteGuid,
            file,
            SuiteConfigurationSchema.Plan,
            BindCurrent);

    internal static MentorConfig BindCurrent(ConfigFile file) => new(
        Bind(file, "General", "Enabled", true, "Enable Orb Mentor. Mode still starts Disabled on fresh installations.", 0, 0, hidden: true),
        Bind(file, "General", "Mode", MentorOperationMode.Disabled, "Disabled rejects and clears sharing work. Active grants through native mastery paths.", MentorFeatureContribution.ConfigurationPageOrder, 0, displaySection: MentorFeatureContribution.PageName, displayName: "Mentor"),
        Bind(file, "General", "ToggleShortcut", new KeyboardShortcut(UnityEngine.KeyCode.M, UnityEngine.KeyCode.LeftAlt), "Toggle Disabled/Active. Default: Left Alt + M.", MentorFeatureContribution.ConfigurationPageOrder, 10, displaySection: MentorFeatureContribution.PageName, displayName: "Toggle shortcut"),
        Bind(file, "Safety", "EmergencyDisable", false, "Immediately reject new events and discard pending bonus work.", 0, 20, displaySection: "General", displayName: "Emergency disable"),
        Bind(file, "Sharing", "EconomyMode", MentorEconomyMode.SharedPool, "SharedPool bounds total bonus XP. PerRecipient grants the percentage to every recipient and scales with collection size.", MentorFeatureContribution.ConfigurationPageOrder, 80, displaySection: MentorFeatureContribution.PageName, displayName: "Economy mode", dependencies: ActiveDependencies),
        Bind(file, "Sharing", "SpellSourcePolicy", MentorSpellSourcePolicy.EquippedSpells, "EquippedSpells lets every equipped spell share its native mastery XP with discovered spells below that source's mastery. HighestDiscovered keeps the original highest-mastery-only rule.", MentorFeatureContribution.ConfigurationPageOrder, 20, displaySection: MentorFeatureContribution.PageName, displayName: "Sharing sources", dependencies: ActiveDependencies),
        Bind(file, "Sharing", "SharePercent", 10.0, "Final mentor XP percentage, clamped to 0-100.", MentorFeatureContribution.ConfigurationPageOrder, 30, new AcceptableValueRange<double>(0, 100), displaySection: MentorFeatureContribution.PageName, displayName: "Spell share percent", dependencies: ActiveDependencies),
        Bind(file, "Artifacts", "Enabled", false, "Share mastery XP earned by equipped artifacts with lower-mastery created artifacts.", MentorFeatureContribution.ConfigurationPageOrder, 40, displaySection: MentorFeatureContribution.PageName, displayName: "Artifact sharing", dependencies: ActiveDependencies),
        Bind(file, "Artifacts", "SharePercent", 10.0, "Artifact mastery XP percentage, clamped to 0-100.", MentorFeatureContribution.ConfigurationPageOrder, 50, new AcceptableValueRange<double>(0, 100), displaySection: MentorFeatureContribution.PageName, displayName: "Artifact share percent", dependencies: ActiveArtifactDependencies),
        Bind(file, "Alchemy", "Enabled", false, "Share alchemy mastery XP with lower-mastery discovered recipes.", MentorFeatureContribution.ConfigurationPageOrder, 60, displaySection: MentorFeatureContribution.PageName, displayName: "Alchemy sharing", dependencies: ActiveDependencies),
        Bind(file, "Alchemy", "SharePercent", 10.0, "Alchemy mastery XP percentage, clamped to 0-100.", MentorFeatureContribution.ConfigurationPageOrder, 70, new AcceptableValueRange<double>(0, 100), displaySection: MentorFeatureContribution.PageName, displayName: "Alchemy share percent", dependencies: ActiveAlchemyDependencies));

    private static ConfigEntry<T> Bind<T>(ConfigFile file, string section, string key, T value, string description,
        int sectionOrder, int settingOrder, AcceptableValueBase? range = null, bool hidden = false,
        string? displaySection = null, string? displayName = null,
        IReadOnlyList<ModConfigDependency>? dependencies = null)
    {
        var metadata = dependencies is null
            ? new ModConfigMetadata(sectionOrder, settingOrder, hidden, displaySection, displayName)
            : new ModConfigMetadata(sectionOrder, settingOrder, dependencies, hidden, displaySection, displayName);
        return file.Bind(section, key, value, new ConfigDescription(description, range, metadata));
    }
}
