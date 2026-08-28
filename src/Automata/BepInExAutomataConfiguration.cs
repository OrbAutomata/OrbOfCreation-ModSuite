using System;
using System.Collections.Generic;
using System.Threading;
using BepInEx.Configuration;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbMentor;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.GameMcp;
#endif

namespace OrbAutomata;

internal sealed class BepInExAutomataConfiguration
{
    private SuiteRuntimeConfiguration _current = null!;
    private MentorConfig? _mentor;
    private int _unpublishedChange;
    private int _emergencyClearRequested;
    private static readonly IReadOnlyList<ModConfigDependency> AutoBuyActiveDependencies = new[]
    {
        new ModConfigDependency("AutoBuy", "Mode", "Active"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> AutoBuyStructuresActiveDependencies = new[]
    {
        new ModConfigDependency("AutoBuy", "Mode", "Active"),
        new ModConfigDependency("AutoBuy", "IncludeStructures"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> AutoBuyUpgradesActiveDependencies = new[]
    {
        new ModConfigDependency("AutoBuy", "Mode", "Active"),
        new ModConfigDependency("AutoBuy", "IncludeUpgrades"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> AutoCastActiveDependencies = new[]
    {
        new ModConfigDependency("AutoCast", "Mode", "Active"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> AutoConceptActiveDependencies = new[]
    {
        new ModConfigDependency("AutoConcept", "Mode", "Active"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> AutoHarvestActiveDependencies = new[]
    {
        new ModConfigDependency("AutoHarvest", "Mode", "Active"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> AutoItemsActiveDependencies = new[]
    {
        new ModConfigDependency("AutoItems", "Mode", "Active"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> AutoScribeActiveDependencies = new[]
    {
        new ModConfigDependency("AutoScribe", "Mode", "Active"),
    };

    private BepInExAutomataConfiguration(
        ConfigFile config,
        ConfigEntry<bool> enabled,
        ConfigEntry<AutoBuyOperationMode> autoBuyMode,
        ConfigEntry<AutoBuyAffordabilityMode> autoBuyAffordabilityMode,
        ConfigEntry<AutoBuyAffordabilityMode> upgradeAffordabilityMode,
        ConfigEntry<bool> autoBuyStructures,
        ConfigEntry<bool> autoBuyUpgrades,
        ConfigEntry<bool> autoLevelSpells,
        ConfigEntry<int> leaveQueueSlots,
        ConfigEntry<AutoCastOperationMode> autoCastMode,
        ConfigEntry<KeyboardShortcut> autoCastToggleShortcut,
        ConfigEntry<bool> autoCastShowToggleButton,
        ConfigEntry<float> autoCastStartResourcePercent,
        ConfigEntry<float> autoCastManualPauseSeconds,
        ConfigEntry<bool> autoCastFullCharge,
        ConfigEntry<AutoConceptOperationMode> autoConceptMode,
        ConfigEntry<AutoConceptSlotManagementMode> autoConceptSlotManagementMode,
        ConfigEntry<bool> autoConceptShowToggleButton,
        ConfigEntry<int> autoConceptTrainingPeriodSeconds,
        ConfigEntry<float> autoConceptRateReservePercent,
        ConfigEntry<float> autoConceptMinimumResourcePercent,
        ConfigEntry<float> autoConceptMinimumDrainRatio,
        ConfigEntry<AutoHarvestOperationMode> autoHarvestMode,
        ConfigEntry<bool> autoHarvestFruitTrees,
        ConfigEntry<bool> autoHarvestTreasureTrees,
        ConfigEntry<AutoItemsOperationMode> autoItemsMode,
        ConfigEntry<bool> autoItemsUseScrolls,
        ConfigEntry<bool> autoItemsUseRelics,
        ConfigEntry<string> autoItemsTemporaryItemAllowlist,
        ConfigEntry<AutoScribeOperationMode> autoScribeMode,
        ConfigEntry<string> autoScribeRoles,
        ConfigEntry<bool> allowUnverifiedGameBuild,
        ConfigEntry<string> acceptedUnverifiedBuildFingerprint,
        ConfigEntry<bool> emergencyDisable,
        ConfigEntry<string> absoluteReserve,
        ConfigEntry<float> relativeReserveMultiplier)
    {
        Enabled = enabled;
        AutoBuyMode = autoBuyMode;
        AutoBuyAffordability = autoBuyAffordabilityMode;
        UpgradeAffordability = upgradeAffordabilityMode;
        AutoBuyStructures = autoBuyStructures;
        AutoBuyUpgrades = autoBuyUpgrades;
        AutoLevelSpells = autoLevelSpells;
        LeaveQueueSlots = leaveQueueSlots;
        AutoCastMode = autoCastMode;
        AutoCastToggleShortcut = autoCastToggleShortcut;
        AutoCastShowToggleButton = autoCastShowToggleButton;
        AutoCastStartResourcePercent = autoCastStartResourcePercent;
        AutoCastManualPauseSeconds = autoCastManualPauseSeconds;
        AutoCastFullCharge = autoCastFullCharge;
        AutoConceptMode = autoConceptMode;
        AutoConceptSlotManagement = autoConceptSlotManagementMode;
        AutoConceptShowToggleButton = autoConceptShowToggleButton;
        AutoConceptTrainingPeriodSeconds = autoConceptTrainingPeriodSeconds;
        AutoConceptRateReservePercent = autoConceptRateReservePercent;
        AutoConceptMinimumResourcePercent = autoConceptMinimumResourcePercent;
        AutoConceptMinimumDrainRatio = autoConceptMinimumDrainRatio;
        AutoHarvestMode = autoHarvestMode;
        AutoHarvestFruitTrees = autoHarvestFruitTrees;
        AutoHarvestTreasureTrees = autoHarvestTreasureTrees;
        AutoItemsMode = autoItemsMode;
        AutoItemsUseScrolls = autoItemsUseScrolls;
        AutoItemsUseRelics = autoItemsUseRelics;
        AutoItemsTemporaryItemAllowlist = autoItemsTemporaryItemAllowlist;
        AutoScribeMode = autoScribeMode;
        AutoScribeRoles = autoScribeRoles;
        AllowUnverifiedGameBuild = allowUnverifiedGameBuild;
        AcceptedUnverifiedBuildFingerprint = acceptedUnverifiedBuildFingerprint;
        EmergencyDisable = emergencyDisable;
        AbsoluteReserve = absoluteReserve;
        RelativeReserveMultiplier = relativeReserveMultiplier;
        RefreshCurrent();
        config.SettingChanged += (_, _) =>
        {
            RefreshCurrent();
            Volatile.Write(ref _unpublishedChange, 1);
        };
        emergencyDisable.SettingChanged += (_, _) =>
        {
            if (!emergencyDisable.Value)
                Volatile.Write(ref _emergencyClearRequested, 1);
        };
    }

    public ConfigEntry<bool> Enabled { get; }

    public ConfigEntry<AutoBuyOperationMode> AutoBuyMode { get; }

    public ConfigEntry<AutoBuyAffordabilityMode> AutoBuyAffordability { get; }

    public ConfigEntry<AutoBuyAffordabilityMode> UpgradeAffordability { get; }

    public ConfigEntry<bool> AutoBuyStructures { get; }

    public ConfigEntry<bool> AutoBuyUpgrades { get; }

    public ConfigEntry<int> LeaveQueueSlots { get; }

    public ConfigEntry<AutoCastOperationMode> AutoCastMode { get; }

    public ConfigEntry<KeyboardShortcut> AutoCastToggleShortcut { get; }

    public ConfigEntry<bool> AutoCastShowToggleButton { get; }

    public ConfigEntry<float> AutoCastStartResourcePercent { get; }

    public ConfigEntry<float> AutoCastManualPauseSeconds { get; }

    public ConfigEntry<bool> AutoCastFullCharge { get; }

    public ConfigEntry<AutoConceptOperationMode> AutoConceptMode { get; }
    public ConfigEntry<AutoConceptSlotManagementMode> AutoConceptSlotManagement { get; }
    public ConfigEntry<bool> AutoConceptShowToggleButton { get; }
    public ConfigEntry<bool> AutoLevelSpells { get; }
    public ConfigEntry<int> AutoConceptTrainingPeriodSeconds { get; }
    public ConfigEntry<float> AutoConceptRateReservePercent { get; }
    public ConfigEntry<float> AutoConceptMinimumResourcePercent { get; }
    public ConfigEntry<float> AutoConceptMinimumDrainRatio { get; }

    public ConfigEntry<AutoHarvestOperationMode> AutoHarvestMode { get; }
    public ConfigEntry<bool> AutoHarvestFruitTrees { get; }
    public ConfigEntry<bool> AutoHarvestTreasureTrees { get; }
    public ConfigEntry<AutoItemsOperationMode> AutoItemsMode { get; }
    public ConfigEntry<bool> AutoItemsUseScrolls { get; }
    public ConfigEntry<bool> AutoItemsUseRelics { get; }
    public ConfigEntry<string> AutoItemsTemporaryItemAllowlist { get; }
    public ConfigEntry<AutoScribeOperationMode> AutoScribeMode { get; }
    public ConfigEntry<string> AutoScribeRoles { get; }
    public ConfigEntry<bool> AllowUnverifiedGameBuild { get; }

    internal ConfigEntry<string> AcceptedUnverifiedBuildFingerprint { get; }

    public ConfigEntry<bool> EmergencyDisable { get; }

    public ConfigEntry<string> AbsoluteReserve { get; }

    public ConfigEntry<float> RelativeReserveMultiplier { get; }

    public SuiteRuntimeConfiguration Current => Volatile.Read(ref _current);

    internal void SetAutoBuyMode(AutoBuyOperationMode mode) => AutoBuyMode.Value = mode;

    internal void SetAutoCastMode(AutoCastOperationMode mode) => AutoCastMode.Value = mode;

    internal void SetAutoConceptMode(AutoConceptOperationMode mode) => AutoConceptMode.Value = mode;

    internal void SetAutoHarvestMode(AutoHarvestOperationMode mode) => AutoHarvestMode.Value = mode;
    internal void SetAutoItemsMode(AutoItemsOperationMode mode) => AutoItemsMode.Value = mode;
    internal void SetAutoScribeMode(AutoScribeOperationMode mode) => AutoScribeMode.Value = mode;

    internal void SetMentorMode(MentorOperationMode mode)
    {
        if (_mentor is null) throw new InvalidOperationException("Mentor configuration is not attached.");
        _mentor.Mode.Value = mode;
    }

    internal void SetEmergencyStop(bool stopped) => EmergencyDisable.Value = stopped;

#if SERVICE_CYCLE_PROFILE
    /// <summary>
    /// Writes one allowlisted suite setting through its real BepInEx entry. The entry's ordinary
    /// SettingChanged event refreshes <see cref="Current"/> and marks the configuration store's
    /// single publication path pending.
    /// </summary>
    internal bool TrySetGameMcpSetting(
        string section,
        string key,
        string serializedValue,
        out string reason,
        out GameMcpConfigurationBound bound)
    {
        bound = GameMcpConfigurationBound.None;
        var entries = GameMcpWritableEntries();
        ConfigEntryBase? selected = null;
        for (var index = 0; index < entries.Length; index++)
        {
            var definition = entries[index].Definition;
            if (!string.Equals(definition.Section, section, StringComparison.Ordinal) ||
                !string.Equals(definition.Key, key, StringComparison.Ordinal))
                continue;
            selected = entries[index];
            break;
        }
        if (selected is null)
        {
            reason =
                "setting " + section + "/" + key +
                " is not in the perf-debug MCP allowlist; compatibility acknowledgements, " +
                "emergency state, and key bindings use dedicated authorities";
            return false;
        }
        if (!GameMcpConfigurationValuePolicy.TryValidate(
                selected,
                serializedValue,
                out reason,
                out bound))
            return false;

        try
        {
            selected.SetSerializedValue(
                GameMcpConfigurationValuePolicy.NativeSerializedValue(
                    selected.SettingType, serializedValue ?? string.Empty));
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException or InvalidOperationException or
                OverflowException)
        {
            reason =
                "BepInEx rejected " + section + "/" + key + ": " +
                exception.GetBaseException().Message;
            return false;
        }
    }

    internal GameMcpWritableSettingDescriptor[] CreateGameMcpWritableSchema()
    {
        var entries = GameMcpWritableEntries();
        var result = new GameMcpWritableSettingDescriptor[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            result[index] = new GameMcpWritableSettingDescriptor(
                entry.Definition.Section,
                entry.Definition.Key,
                GameMcpConfigurationValuePolicy.SettingTypeWord(entry.SettingType),
                entry.Description.Description ?? string.Empty,
                GameMcpConfigurationValuePolicy.Describe(entry));
        }
        return result;
    }

    private ConfigEntryBase[] GameMcpWritableEntries()
    {
        var entries = new System.Collections.Generic.List<ConfigEntryBase>(SuiteWritableEntries());
        // Orb Mentor is one of the same seven on/off buttons and belongs on the same writable
        // surface; it lives on the attached mentor binding rather than on this one.
        if (_mentor is not null) entries.Add(_mentor.Mode);
        return entries.ToArray();
    }

    private ConfigEntryBase[] SuiteWritableEntries() =>
        new ConfigEntryBase[]
        {
            Enabled,
            AutoBuyMode,
            AutoBuyAffordability,
            UpgradeAffordability,
            AutoBuyStructures,
            AutoBuyUpgrades,
            AutoLevelSpells,
            LeaveQueueSlots,
            AutoCastMode,
            AutoCastStartResourcePercent,
            AutoCastManualPauseSeconds,
            AutoCastFullCharge,
            AutoConceptMode,
            AutoConceptSlotManagement,
            AutoConceptTrainingPeriodSeconds,
            AutoConceptRateReservePercent,
            AutoConceptMinimumResourcePercent,
            AutoConceptMinimumDrainRatio,
            AutoHarvestMode,
            AutoHarvestFruitTrees,
            AutoHarvestTreasureTrees,
            AutoItemsMode,
            AutoItemsUseScrolls,
            AutoItemsUseRelics,
            AutoItemsTemporaryItemAllowlist,
            AutoScribeMode,
            AutoScribeRoles,
            AbsoluteReserve,
            RelativeReserveMultiplier,
        };
#endif

    internal void SetAllowUnverifiedGameBuild(bool allowed) =>
        AllowUnverifiedGameBuild.Value = allowed;

    internal void AcceptUnverifiedBuild(string fingerprint) =>
        AcceptedUnverifiedBuildFingerprint.Value = fingerprint ?? string.Empty;

    internal bool IsAutoCastTogglePressed() => AutoCastToggleShortcut.Value.IsDown();

    internal void RefreshCurrent() =>
        Volatile.Write(ref _current, BepInExAutomataConfigurationReader.Read(this));

    internal void AttachMentor(MentorConfig mentor)
    {
        _mentor = mentor ?? throw new ArgumentNullException(nameof(mentor));
        RefreshCurrent();
    }

    internal MentorConfig? Mentor => _mentor;

    /// <summary>
    /// The reading to publish, if the settings have changed since this was last asked.
    /// </summary>
    /// <remarks>
    /// Every source of change lands here — the suite's own panel, BepInEx's configuration manager, an
    /// edited file reloaded from disk — because BepInEx raises one event for all of them. The caller
    /// takes the change on the main thread rather than being handed it from the event, since the file
    /// watcher raises that event on a thread of its own and the suite's publications are replaced by
    /// the main thread only.
    /// </remarks>
    internal bool TryTakeUnpublishedChange(out SuiteRuntimeConfiguration configuration)
    {
        if (Interlocked.Exchange(ref _unpublishedChange, 0) == 0)
        {
            configuration = null!;
            return false;
        }

        configuration = Current;
        return true;
    }

    /// <summary>
    /// Consumes an explicit post-bind request to clear the saved emergency stop.
    /// </summary>
    /// <remarks>
    /// Initial config binding does not raise this signal. On an unverified build, that distinction
    /// lets an immediate emergency-control clear act as explicit consent without treating a persisted
    /// false default as consent after the game assemblies change.
    /// </remarks>
    internal bool TryTakeEmergencyClearRequest() =>
        Interlocked.Exchange(ref _emergencyClearRequested, 0) != 0;

    public static BepInExAutomataConfiguration Bind(ConfigFile config)
    {
        var result = TryBind(config);
        if (!result.Success)
            throw new InvalidOperationException(result.Status.Reason);
        return result.Config!;
    }

    public static ConfigurationSchemaBindResult<BepInExAutomataConfiguration> TryBind(
        ConfigFile config,
        IConfigurationFileOperations? fileOperations = null,
        ConfigurationSchemaStatusRegistry? statuses = null) =>
        ConfigurationSchemaTransaction.Bind(
            PluginIds.SuiteGuid,
            config,
            SuiteConfigurationSchema.Plan,
            BindCurrent,
            fileOperations,
            statuses);

    internal static BepInExAutomataConfiguration BindCurrent(ConfigFile config)
    {
            var autoBuyMode = Bind(
                config,
                "AutoBuy",
                "Mode",
                AutoBuyOperationMode.Active,
                "Disabled stops Auto Buy. Active purchases through the native queue after every action is revalidated.",
                10,
                0);

            var autoCastMode = Bind(
                config,
                "AutoCast",
                "Mode",
                AutoCastOperationMode.Disabled,
                "Disabled stops Auto Cast. Active casts through the native spell manager.",
                15,
                0);

            var autoConceptMode = Bind(
                config,
                "AutoConcept",
                "Mode",
                AutoConceptOperationMode.Disabled,
                "Disabled performs no concept work. Active trains the lowest-mastery discovered Scholar concepts through the native Active Concepts list.",
                17,
                0);

            var result = new BepInExAutomataConfiguration(
                config,
                Bind(config, "General", "Enabled", true, "Master switch for automation and mastery catch-up. The in-game configuration and safety controls remain available while this is off.", 0, 0),
                autoBuyMode,
                Bind(config, "AutoBuy", "AffordabilityMode", AutoBuyAffordabilityMode.Excess100, "Affordability policy for structures. BuyAll accepts any affordable purchase; excess modes require current resources to be at least 10x, 100x, or 1000x the true cost.", 10, 30, dependencies: AutoBuyStructuresActiveDependencies),
                Bind(config, "AutoBuy", "UpgradeAffordabilityMode", AutoBuyAffordabilityMode.Excess100, "Independent affordability policy for upgrades. BuyAll accepts any affordable upgrade; excess modes require current resources to be at least 10x, 100x, or 1000x the true cost.", 10, 31, dependencies: AutoBuyUpgradesActiveDependencies),
                Bind(config, "AutoBuy", "IncludeStructures", true, "Include native StructureSO attributes and levels.", 10, 10, dependencies: AutoBuyActiveDependencies),
                Bind(config, "AutoBuy", "IncludeUpgrades", true, "Include native UpgradeSO purchases.", 10, 20, dependencies: AutoBuyActiveDependencies),
                Bind(config, "AutoBuy", "AutoLevelSpells", true, "Automatically level ready spells while Auto Buy is active. Capability follows native progression automatically: locked, one spell per action, then the native level-all action after its upgrade completes.", 10, 25, dependencies: AutoBuyActiveDependencies),
                Bind(config, "AutoBuy", "LeaveQueueSlots", 1, "Minimum native action-queue slots Automata leaves free for manual actions.", 10, 70, dependencies: AutoBuyActiveDependencies),
                autoCastMode,
                Bind(config, "AutoCast", "ToggleShortcut", new KeyboardShortcut(UnityEngine.KeyCode.F8), "Toggle Auto Cast between Disabled and Active. Default: F8.", 15, 5),
                Bind(
                    config,
                    "AutoCast",
                    "ShowToggleButton",
                    true,
                    "Legacy setting retained for configuration-file compatibility; ignored because every registered automation feature now has one quick control.",
                    15,
                    6,
                    hidden: true),
                Bind(config, "AutoCast", "StartResourcePercent", 0.0f, "Minimum fullness for every finite-cap resource used by a spell's immediate or drain cost. Fresh installs default to 0%.", 15, 20, new AcceptableValueRange<float>(0.0f, 100.0f), AutoCastActiveDependencies),
                Bind(config, "AutoCast", "ManualPauseSeconds", 2.0f, "Unscaled pause after a manual spell fire before Auto Cast resumes.", 15, 30, new AcceptableValueRange<float>(0.0f, 60.0f), AutoCastActiveDependencies),
                Bind(config, "AutoCast", "FullCharge", true, "When enabled, Auto Cast holds charge-capable spells until the native full-charge point. When disabled, it fires them immediately without charging.", 15, 1, dependencies: AutoCastActiveDependencies),
                autoConceptMode,
                Bind(config, "AutoConcept", "SlotManagementMode", AutoConceptSlotManagementMode.TimedCycle, "RotateAll replaces active concepts when a compatible discovered concept has strictly lower mastery. PreserveManual fills empty slots and rotates only quantities added by Automata. TimedCycle rotates compatible concepts only after their full settled training period, even if they already caught up.", 17, 5, dependencies: AutoConceptActiveDependencies),
                Bind(
                    config,
                    "AutoConcept",
                    "ShowToggleButton",
                    true,
                    "Legacy setting retained for configuration-file compatibility; ignored because every registered automation feature now has one quick control.",
                    17,
                    6,
                    hidden: true),
                Bind(config, "AutoConcept", "TrainingPeriodSeconds", 30, "RotateAll and PreserveManual protect a newly assigned concept until it catches the captured highest mastery or this settled time elapses. TimedCycle always uses the full settled period.", 17, 7, new AcceptableValueRange<int>(10, 3600), AutoConceptActiveDependencies),
                Bind(config, "AutoConcept", "RateReservePercent", 10.0f, "Minimum percentage of each drained resource's current gross positive rate to preserve after an automated quantity change.", 17, 30, new AcceptableValueRange<float>(0.0f, 100.0f), AutoConceptActiveDependencies),
                Bind(config, "AutoConcept", "MinimumResourcePercent", 10.0f, "Finite-cap drained resources must be at least this full before Auto Concept adds quantity.", 17, 40, new AcceptableValueRange<float>(0.0f, 100.0f), AutoConceptActiveDependencies),
                Bind(config, "AutoConcept", "MinimumDrainRatio", 0.95f, "Native post-settlement drain ratio floor. Falling below it rolls back only Automata-owned quantity.", 17, 50, new AcceptableValueRange<float>(0.0f, 1.0f), AutoConceptActiveDependencies),
                Bind(config, "AutoHarvest", "Mode", AutoHarvestOperationMode.Disabled, "Disabled performs no harvest work. Active queues one audited native fruit-tree or treasure-tree collect action at a time.", 18, 0),
                Bind(config, "AutoHarvest", "CollectFruitTrees", true, "Collect ready fruit trees through their native plot action.", 18, 10, dependencies: AutoHarvestActiveDependencies),
                Bind(config, "AutoHarvest", "CollectTreasureTrees", true, "Collect ready treasure trees through their native plot action.", 18, 20, dependencies: AutoHarvestActiveDependencies),
                Bind(config, "AutoItems", "Mode", AutoItemsOperationMode.Disabled, "Disabled performs no item work. Active uses one eligible Scroll, Relic, or exact allowlisted temporary item from each fresh world publication.", 19, 0),
                Bind(config, "AutoItems", "UseScrolls", true, "Use visible Scrolls with native randomized targeting after exact live target revalidation.", 19, 10, dependencies: AutoItemsActiveDependencies),
                Bind(config, "AutoItems", "UseRelics", true, "Use visible Relics when live native preparation and firing checks permit.", 19, 20, dependencies: AutoItemsActiveDependencies),
                Bind(config, "AutoItems", "TemporaryItemAllowlist", string.Empty, "Approve discovered Fruits, Potions, and Threads from the item picker. Empty approves none; the stored value remains a comma-separated set of exact UUIDs.", 19, 30, dependencies: AutoItemsActiveDependencies),
                Bind(config, "AutoScribe", "Mode", AutoScribeOperationMode.Disabled, "Disabled performs no Scribe work. Active produces at most one audited Scroll from each fresh world publication.", 20, 0),
                Bind(config, "AutoScribe", "Roles", string.Empty, "Comma-separated semantic Scribe role keys. Empty selects every audited producible role; use none to select no roles.", 20, 10, dependencies: AutoScribeActiveDependencies),
                Bind(config, "Compatibility", "AllowUnverifiedGameBuild", false, "Advanced risk acknowledgement. Allows gameplay patches and services on the exact unaudited assembly pair observed when this is enabled. A later game update automatically returns the suite to quarantine.", 50, 0),
                Bind(config, "Compatibility", "AcceptedUnverifiedBuildFingerprint", string.Empty, "Exact assembly-pair fingerprint accepted by the player. Managed by the suite.", 50, 10, hidden: true),
                Bind(config, "Safety", "EmergencyDisable", false, "Suite-wide emergency stop: halts new purchases, casts, concepts, spell levels, harvest submissions, consumable uses, and mastery sharing immediately.", 40, 0),
                Bind(config, "Reserves", "AbsoluteReserve", "0", "Absolute amount of every resource to leave after each automated purchase or cast.", 20, 0),
                Bind(config, "Reserves", "RelativeReserveMultiplier", 0.0f, "Additional amount to leave after each action, expressed as a multiple of that action's cost. Affordability modes remain separate.", 20, 10));

            return result;
    }

    private static ConfigEntry<T> Bind<T>(
        ConfigFile config,
        string section,
        string key,
        T defaultValue,
        string description,
        int sectionOrder,
        int settingOrder,
        AcceptableValueBase? acceptableValues = null,
        IReadOnlyList<ModConfigDependency>? dependencies = null,
        bool restartRequired = false,
        bool hidden = false)
    {
        var displaySection = section switch
        {
            "General" when key == "Enabled" => "General",
            "Safety" when key == "EmergencyDisable" => "General",
            "AutoBuy" => "Auto Buy",
            "AutoCast" => "Auto Cast",
            "AutoConcept" => "Auto Concept",
            "AutoHarvest" => "Auto Harvest",
            "AutoItems" => "Auto Items",
            "AutoScribe" => "Auto Scribe",
            _ => "Advanced",
        };
        var displayName = key switch
        {
            "Enabled" when section == "General" => "Automation enabled",
            "Mode" when section == "AutoBuy" => "Auto Buy",
            "Mode" when section == "AutoCast" => "Auto Cast",
            "Mode" when section == "AutoConcept" => "Auto Concept",
            "Mode" when section == "AutoHarvest" => "Auto Harvest",
            "Mode" when section == "AutoItems" => "Auto Items",
            "Mode" when section == "AutoScribe" => "Auto Scribe",
            "Roles" => "Roles",
            "UseScrolls" => "Use Scrolls",
            "UseRelics" => "Use Relics",
            "TemporaryItemAllowlist" => "Temporary item UUID allowlist",
            "CollectFruitTrees" => "Collect fruit trees",
            "CollectTreasureTrees" => "Collect treasure trees",
            "SlotManagementMode" => "Slot management",
            "TrainingPeriodSeconds" => "Training period (seconds)",
            "AutoLevelSpells" => "Auto-level spells",
            "AllowUnverifiedGameBuild" => "Allow this unverified game build",
            "AffordabilityMode" => "Structure affordability",
            "UpgradeAffordabilityMode" => "Upgrade affordability",
            "IncludeStructures" => "Buy structures",
            "IncludeUpgrades" => "Buy upgrades",
            "StartResourcePercent" => "Minimum resource percent",
            _ => null,
        };
        var presentationOrder = displaySection == "General" ? -10 : displaySection == "Auto Buy" ? 0 : displaySection == "Auto Cast" ? 10 : displaySection == "Auto Concept" ? 15 : displaySection == "Auto Harvest" ? 17 : displaySection == "Auto Items" ? 18 : displaySection == "Auto Scribe" ? 19 : 20;
        var metadata = dependencies is null
            ? new ModConfigMetadata(presentationOrder, settingOrder, hidden, displaySection, displayName, restartRequired)
            : new ModConfigMetadata(presentationOrder, settingOrder, dependencies, hidden, displaySection, displayName, restartRequired);
        return config.Bind(
            section,
            key,
            defaultValue,
            new ConfigDescription(
                description,
                acceptableValues,
                metadata));
    }

}

internal enum AutoBuyOperationMode
{
    Disabled,
    Active
}

internal enum AutoCastOperationMode
{
    Disabled,
    Active,
}

internal enum AutoHarvestOperationMode
{
    Disabled,
    Active,
}

internal enum LegacyResearchAutomationMode
{
    Disabled,
    DryRun,
    Active,
}

internal enum AutoBuyAffordabilityMode
{
    BuyAll,
    Excess10,
    Excess100,
    Excess1000
}
