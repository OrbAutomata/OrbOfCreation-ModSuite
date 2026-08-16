using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using OrbAutomata;
using OrbModConfig;
using OrbModding.Common;
using OrbMentor;
using TMPro;
using UnityEngine;
using Xunit;

namespace OrbModding.Tests;

public sealed class ModConfigTests
{
    private enum SampleMode
    {
        Disabled,
        Active,
    }

    [Fact]
    public void NativeShellUsesTopFrameAndLeavesOnlyDeliberateGutters()
    {
        Assert.Equal(0.975f, ModConfigShellLayout.TitleTop);
        Assert.Equal(0.885f, ModConfigShellLayout.TitleBottom);
        Assert.Equal(0.875f, ModConfigShellLayout.BodyTop);
        Assert.Equal(0.115f, ModConfigShellLayout.BodyBottom);
        Assert.Equal(0.105f, ModConfigShellLayout.FooterTop);
        Assert.Equal(0.025f, ModConfigShellLayout.FooterBottom);
        Assert.Equal(0.01f, ModConfigShellLayout.TitleBottom - ModConfigShellLayout.BodyTop, 3);
        Assert.Equal(0.01f, ModConfigShellLayout.BodyBottom - ModConfigShellLayout.FooterTop, 3);
        Assert.True(ModConfigShellLayout.TitleTop > ModConfigShellLayout.BodyTop);
        Assert.True(ModConfigShellLayout.BodyTop - ModConfigShellLayout.BodyBottom > 0.75f);
    }

    [Fact]
    public void TopNavigationPreservesTheRegisteredPluginTitle()
    {
        var catalog = new ConfigCatalogSnapshot(new[]
        {
            new ModConfigDescriptor(
                "suite",
                "Orb Of Creation ModSuite",
                "test",
                new[] { new ConfigSectionDescriptor("Auto Buy", Array.Empty<ConfigSettingDescriptor>()) }),
        });

        var pages = ModConfigTopNavigation.Build(catalog, attentionCount: 0);

        Assert.Equal("Orb Of Creation ModSuite · Auto Buy", ModConfigTopNavigation.DetailTitle(catalog, pages[1]));
    }

    [Fact]
    public void TopNavigationKeepsSchemaStatusOnlyPluginsSelectable()
    {
        var catalog = new ConfigCatalogSnapshot(new[]
        {
            new ModConfigDescriptor(
                "suite",
                "Orb Of Creation ModSuite",
                "test",
                Array.Empty<ConfigSectionDescriptor>()),
        });

        var page = ModConfigTopNavigation.Build(catalog, attentionCount: 0)[1];

        Assert.Equal(-1, page.SectionIndex);
        Assert.Equal("Orb Of Creation ModSuite", page.Label);
        Assert.Equal("Orb Of Creation ModSuite", ModConfigTopNavigation.DetailTitle(catalog, page));
    }

    [Fact]
    public void Catalog_GroupsAndSortsModsSectionsAndSettingsDeterministically()
    {
        var laterConfig = new ConfigFile();
        laterConfig.Bind("Zeta", "Second", true, "Second setting");
        laterConfig.Bind("Alpha", "Zulu", "value", "Last key");
        laterConfig.Bind("Alpha", "Alpha", 3, "First key");

        var earlierConfig = new ConfigFile();
        earlierConfig.Bind("General", "Enabled", true, "Enable it");

        var snapshot = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("plugin.z", "Same Name", "2.0.0", laterConfig),
            new ConfigPluginSource("plugin.a", "Same Name", "1.0.0", earlierConfig),
        });

        Assert.Equal(new[] { "plugin.a", "plugin.z" }, snapshot.Mods.Select(mod => mod.Guid));
        Assert.Equal(new[] { "Alpha", "Zeta" }, snapshot.Mods[1].Sections.Select(section => section.Name));
        Assert.Equal(new[] { "Alpha", "Zulu" }, snapshot.Mods[1].Sections[0].Settings.Select(setting => setting.Key));
        Assert.Equal(4, snapshot.SettingCount);
    }

    [Fact]
    public void Catalog_ClassifiesSupportedEditorShapes()
    {
        var config = new ConfigFile();
        config.Bind("Types", "Boolean", true, "bool");
        config.Bind("Types", "Enum", SampleMode.Active, "enum");
        config.Bind(
            "Types",
            "Bounded",
            5,
            new ConfigDescription("bounded", new AcceptableValueRange<int>(1, 10)));
        config.Bind("Types", "Numeric", 1.5d, "numeric");
        config.Bind("Types", "String", "text", "string");
        config.Bind("Types", "Shortcut", new KeyboardShortcut(KeyCode.Equals), "shortcut");
        config.Bind("Types", "Unsupported", new object(), "unsupported");

        var settings = ConfigCatalog.Build(new[]
            {
                new ConfigPluginSource("plugin", "Plugin", "1.0.0", config),
            })
            .Mods.Single()
            .Sections.Single()
            .Settings.ToDictionary(setting => setting.Key, setting => setting.Kind);

        Assert.Equal(ConfigEditorKind.Boolean, settings["Boolean"]);
        Assert.Equal(ConfigEditorKind.Enum, settings["Enum"]);
        Assert.Equal(ConfigEditorKind.BoundedNumeric, settings["Bounded"]);
        Assert.Equal(ConfigEditorKind.Numeric, settings["Numeric"]);
        Assert.Equal(ConfigEditorKind.String, settings["String"]);
        Assert.Equal(ConfigEditorKind.KeyboardShortcut, settings["Shortcut"]);
        Assert.Equal(ConfigEditorKind.Unsupported, settings["Unsupported"]);
    }

    [Fact]
    public void Catalog_UsesPresentationMetadataAndOmitsHiddenSettings()
    {
        var config = new ConfigFile();
        config.Bind(
            "Later",
            "First",
            true,
            new ConfigDescription("first", null, new ModConfigMetadata(20, 0)));
        config.Bind(
            "Earlier",
            "Second",
            true,
            new ConfigDescription("second", null, new ModConfigMetadata(10, 20)));
        config.Bind(
            "Earlier",
            "First",
            true,
            new ConfigDescription("first", null, new ModConfigMetadata(10, 10, displaySection: "Basics", displayName: "Friendly first")));
        config.Bind(
            "Earlier",
            "Hidden",
            true,
            new ConfigDescription("hidden", null, new ModConfigMetadata(10, 0, hidden: true)));

        var mod = ConfigCatalog.Build(new[]
            {
                new ConfigPluginSource("plugin", "Plugin", "1.0.0", config),
            })
            .Mods.Single();

        Assert.Equal(new[] { "Basics", "Earlier", "Later" }, mod.Sections.Select(section => section.Name));
        Assert.Equal("Friendly first", mod.Sections[0].Settings.Single().DisplayName);
        Assert.Equal(3, mod.Sections.Sum(section => section.Settings.Count));
    }

    [Fact]
    public void AutomataCatalog_ShowsOnlyOrderedPublicConfiguration()
    {
        var config = new ConfigFile();
        BepInExAutomataConfiguration.Bind(config);

        var mod = ConfigCatalog.Build(new[]
            {
                new ConfigPluginSource("automata", "Automata", "test", config),
            })
            .Mods.Single();

        Assert.Equal(
            new[] { "General", "Auto Buy", "Auto Cast", "Auto Concept", "Auto Harvest", "Auto Items", "Auto Scribe", "Advanced" },
            mod.Sections.Select(section => section.Name));
        Assert.DoesNotContain(mod.Sections, section => section.Name == "Research" || section.Name == "ActiveMode");
        Assert.Equal(
            new[] { "Mode", "IncludeStructures", "IncludeUpgrades", "AutoLevelSpells", "AffordabilityMode", "UpgradeAffordabilityMode", "LeaveQueueSlots" },
            mod.Sections.Single(section => section.Name == "Auto Buy").Settings.Take(7).Select(setting => setting.Key));
        Assert.Equal(
            new[] { "Mode", "FullCharge", "ToggleShortcut", "StartResourcePercent", "ManualPauseSeconds" },
            mod.Sections.Single(section => section.Name == "Auto Cast").Settings.Select(setting => setting.Key));
        Assert.Equal(
            new[] { "Mode", "SlotManagementMode", "TrainingPeriodSeconds", "RateReservePercent", "MinimumResourcePercent", "MinimumDrainRatio" },
            mod.Sections.Single(section => section.Name == "Auto Concept").Settings.Select(setting => setting.Key));
        Assert.Equal(
            new[] { "Mode", "CollectFruitTrees", "CollectTreasureTrees" },
            mod.Sections.Single(section => section.Name == "Auto Harvest").Settings.Select(setting => setting.Key));
        Assert.Equal(
            new[] { "Mode", "UseScrolls", "UseRelics", "TemporaryItemAllowlist" },
            mod.Sections.Single(section => section.Name == "Auto Items").Settings.Select(setting => setting.Key));
        Assert.Equal(
            new[] { "Mode", "Roles" },
            mod.Sections.Single(section => section.Name == "Auto Scribe").Settings.Select(setting => setting.Key));
        Assert.DoesNotContain(
            mod.Sections.SelectMany(section => section.Settings),
            setting => setting.Key.Contains("RuntimeProbe", StringComparison.Ordinal) ||
                       setting.Key.Contains("PurchaseLimitPerSession", StringComparison.Ordinal));
        Assert.DoesNotContain(
            mod.Sections.Single(section => section.Name == "Auto Buy").Settings,
            setting => setting.Key is "PurchaseGrouping" or "FixedGroupSize" or
                "BatchSizingMode" or "MaxPurchasesPerBatch" or
                "PrioritizeCostAndQualityStructures" or "AllowedUuids" or "BlockedUuids");

        Assert.Contains(mod.Sections.SelectMany(section => section.Settings), setting => setting.SourceSection == "General" && setting.Key == "Enabled");
        Assert.Contains(mod.Sections.Single(section => section.Name == "General").Settings, setting => setting.Key == "EmergencyDisable");
        Assert.DoesNotContain(
            mod.Sections.SelectMany(section => section.Settings),
            setting => setting.Key is "EvaluationIntervalSeconds" or
                "FallbackEvaluationIntervalSeconds" or "PerConceptQuantityCap");
        Assert.Contains(mod.Sections.Single(section => section.Name == "Advanced").Settings, setting => setting.Key == "AllowUnverifiedGameBuild");
        Assert.DoesNotContain(mod.Sections.Single(section => section.Name == "Advanced").Settings, setting => setting.Key == "EmergencyDisable");
        Assert.DoesNotContain(mod.Sections.SelectMany(section => section.Settings), setting => setting.Key == "AcceptedUnverifiedBuildFingerprint");

        var autoBuyMode = mod.Sections.Single(section => section.Name == "Auto Buy").Settings.Single(setting => setting.Key == "Mode");
        Assert.True(ModSettingsPage.IsImmediateCommandSetting(autoBuyMode));
        var emergencyDisable = mod.Sections.Single(section => section.Name == "General")
            .Settings.Single(setting => setting.Key == "EmergencyDisable");
        Assert.True(ModSettingsPage.IsImmediateCommandSetting(emergencyDisable));
        Assert.Equal(new[] { "Disabled", "Active" }, Enum.GetNames(autoBuyMode.SettingType));
        var autoConceptMode = mod.Sections.Single(section => section.Name == "Auto Concept").Settings.Single(setting => setting.Key == "Mode");
        Assert.Equal(new[] { "Disabled", "Active" }, Enum.GetNames(autoConceptMode.SettingType));
        var autoConceptSlotManagement = mod.Sections.Single(section => section.Name == "Auto Concept").Settings.Single(setting => setting.Key == "SlotManagementMode");
        Assert.Equal(new[] { "RotateAll", "PreserveManual", "TimedCycle" }, Enum.GetNames(autoConceptSlotManagement.SettingType));

        var session = new ConfigEditSession(new ConfigCatalogSnapshot(new[] { mod }));
        var settings = mod.Sections.SelectMany(section => section.Settings).ToDictionary(
            setting => $"{setting.SourceSection}.{setting.Key}");
        Assert.All(
            settings.Values.Where(setting => setting.SourceSection == "AutoBuy" && setting.Key != "Mode"),
            setting => Assert.Contains(setting.Dependencies, dependency =>
                dependency.Section == "AutoBuy" && dependency.Key == "Mode" && dependency.ExpectedValue == "Active"));
        Assert.All(
            settings.Values.Where(setting => setting.SourceSection == "AutoCast" &&
                setting.Key is not ("Mode" or "ToggleShortcut")),
            setting => Assert.Contains(setting.Dependencies, dependency =>
                dependency.Section == "AutoCast" && dependency.Key == "Mode" && dependency.ExpectedValue == "Active"));
        Assert.All(
            settings.Values.Where(setting => setting.SourceSection == "AutoConcept" &&
                setting.Key != "Mode"),
            setting => Assert.Contains(setting.Dependencies, dependency =>
                dependency.Section == "AutoConcept" && dependency.Key == "Mode" && dependency.ExpectedValue == "Active"));
        Assert.All(
            settings.Values.Where(setting => setting.SourceSection == "AutoHarvest" &&
                setting.Key != "Mode"),
            setting => Assert.Contains(setting.Dependencies, dependency =>
                dependency.Section == "AutoHarvest" && dependency.Key == "Mode" && dependency.ExpectedValue == "Active"));
        Assert.All(
            settings.Values.Where(setting => setting.SourceSection == "AutoItems" &&
                setting.Key != "Mode"),
            setting => Assert.Contains(setting.Dependencies, dependency =>
                dependency.Section == "AutoItems" && dependency.Key == "Mode" && dependency.ExpectedValue == "Active"));

        Assert.True(session.DependencySatisfied(settings["AutoCast.Mode"]));
        Assert.True(session.DependencySatisfied(settings["AutoCast.ToggleShortcut"]));
        Assert.True(session.DependencySatisfied(settings["AutoBuy.AutoLevelSpells"]));
        session.Get(settings["AutoBuy.Mode"]).Stage("Disabled");
        Assert.False(session.DependencySatisfied(settings["AutoBuy.AutoLevelSpells"]));
        session.Get(settings["AutoBuy.Mode"]).Stage("Active");
        Assert.False(session.DependencySatisfied(settings["AutoCast.FullCharge"]));
        Assert.False(session.DependencySatisfied(settings["AutoConcept.SlotManagementMode"]));

        session.Get(settings["AutoCast.Mode"]).Stage("Active");
        session.Get(settings["AutoConcept.Mode"]).Stage("Active");
        Assert.True(session.DependencySatisfied(settings["AutoCast.FullCharge"]));
        Assert.True(session.DependencySatisfied(settings["AutoConcept.SlotManagementMode"]));
    }

    [Fact]
    public void SuiteCatalogConsolidatesLegacyTabsIntoNineFeaturePages()
    {
        var config = new ConfigFile();
        BepInExAutomataConfiguration.Bind(config);
        MentorConfig.Bind(config);

        var mod = ConfigCatalog.Build(new[]
            {
                new ConfigPluginSource("suite", "Orb Of Creation ModSuite", "test", config),
            })
            .Mods.Single();

        Assert.Equal(
            new[] { "General", "Auto Buy", "Auto Cast", "Auto Concept", "Auto Harvest", "Auto Items", "Auto Scribe", "Mentor", "Advanced" },
            mod.Sections.Select(section => section.Name));
        Assert.Equal(10, ModConfigTopNavigation.Build(new ConfigCatalogSnapshot(new[] { mod }), 0).Count);
        Assert.All(
            mod.Sections.Where(section => section.Name is "Auto Buy" or "Auto Cast" or "Auto Concept" or "Auto Harvest" or "Auto Items" or "Auto Scribe" or "Mentor"),
            section => Assert.True(ModSettingsPage.IsImmediateCommandSetting(
                section.Settings.Single(setting => setting.Key == "Mode"))));
    }

    [Fact]
    public void GeneralEmergencyCommandUsesTheSameImmediateToggleInBothDirections()
    {
        var file = new ConfigFile();
        var config = BepInExAutomataConfiguration.Bind(file);
        var mentor = MentorConfig.Bind(file);
        config.AttachMentor(mentor);
        var store = new AutomataConfigurationStore(config, (_, _) => { });
        using var statuses = new AutomataFeatureStatuses(
            store.Current,
            lifecycleGeneration: 1,
            registry: new FeatureStatusRegistry(),
            configurationGeneration: store.CurrentGeneration);
        var registry = AutomationFeatureControlRegistry.Create(
            store,
            statuses,
            new SpellLevelCapabilityState(),
            mentor);
        var emergency = new EmergencyStopControl(store, _ => { });
        var commands = new ModConfigFeatureCommands(registry, emergency);

        Assert.True(commands.TryGet(PluginIds.SuiteGuid, "General", out var command));
        Assert.Equal("Stop all", command.Presentation.ButtonLabel);

        command.Toggle();

        Assert.True(store.Current.Safety.EmergencyDisable);
        Assert.Equal("Resume all", command.Presentation.ButtonLabel);

        command.Toggle();

        Assert.False(store.Current.Safety.EmergencyDisable);
        Assert.Equal("Stop all", command.Presentation.ButtonLabel);
    }

    [Fact]
    public void ModsRailBuildsEveryRegisteredConsolidatedPageWithDistinctAuditedIconsWithoutAnEquippedSpell()
    {
        var config = new ConfigFile();
        BepInExAutomataConfiguration.Bind(config);
        MentorConfig.Bind(config);
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("suite", "Orb Of Creation ModSuite", "test", config),
        });
        var pages = ModConfigTopNavigation.Build(catalog, attentionCount: 0);
        var prototypeObject = new GameObject(
            "NativeRailPrototype",
            typeof(RectTransform));
        var prototype = prototypeObject.AddComponent<Behaviour>();
        var inactive = new Sprite();
        var active = new Sprite();
        var primitives = new NativeFeatureRailVisualPrimitives(
            prototype,
            inactive,
            active,
            new Sprite(),
            new Sprite(),
            new Sprite(),
            new Sprite(),
            new Sprite(),
            new Sprite());
        var parent = (RectTransform)new GameObject(
            "RailParent",
            typeof(RectTransform)).transform;
        var template = new GameObject("LabelTemplate").AddComponent<TextMeshProUGUI>();
        var owned = new List<GameObject>();
        var previousSpellManager = global::SpellManager.instance;
        global::SpellManager.instance = new global::SpellManager();
        try
        {
            Assert.Empty(global::SpellManager.instance.activeSpells.value);
            Assert.True(
                ModConfigNativeRailFactory.TryBuild(
                    parent,
                    pages.Select(page => page.Label).ToArray(),
                    selected: 0,
                    owned,
                    template,
                    primitives,
                    _ => { },
                    out var reason),
                reason);
            Assert.Equal(pages.Count, owned.Count);
        }
        finally
        {
            global::SpellManager.instance = previousSpellManager;
            foreach (var item in owned) UnityEngine.Object.Destroy(item);
            UnityEngine.Object.Destroy(template.gameObject);
            UnityEngine.Object.Destroy(prototypeObject);
            UnityEngine.Object.Destroy(parent.gameObject);
        }
    }

    [Fact]
    public void ModsRailRejectsAnyDuplicateRegisteredPageGlyph()
    {
        var prototypeObject = new GameObject(
            "NativeRailPrototype",
            typeof(RectTransform));
        var prototype = prototypeObject.AddComponent<Behaviour>();
        var duplicate = new Sprite();
        var primitives = new NativeFeatureRailVisualPrimitives(
            prototype,
            new Sprite(),
            new Sprite(),
            duplicate,
            duplicate,
            new Sprite(),
            new Sprite(),
            new Sprite(),
            new Sprite());
        var parent = (RectTransform)new GameObject(
            "RailParent",
            typeof(RectTransform)).transform;
        var template = new GameObject("LabelTemplate").AddComponent<TextMeshProUGUI>();
        var owned = new List<GameObject>();

        Assert.False(
            ModConfigNativeRailFactory.TryBuild(
                parent,
                new[] { "Runtime", "General" },
                selected: 0,
                owned,
                template,
                primitives,
                _ => { },
                out var reason));
        Assert.Empty(owned);
        Assert.Contains("resolve the same sprite", reason);
    }

    [Fact]
    public void MentorCatalog_UsesFeatureTabsAndDependencies()
    {
        var config = new ConfigFile();
        MentorConfig.Bind(config);
        var mod = ConfigCatalog.Build(new[] { new ConfigPluginSource("mentor", "Mentor", "test", config) }).Mods.Single();

        Assert.Equal(new[] { "General", "Mentor" }, mod.Sections.Select(section => section.Name));
        Assert.DoesNotContain(mod.Sections.SelectMany(section => section.Settings), setting => setting.SourceSection == "General" && setting.Key == "Enabled");
        Assert.Contains(mod.Sections.Single(section => section.Name == "General").Settings, setting => setting.Key == "EmergencyDisable");
        var mentorSection = mod.Sections.Single(section => section.Name == "Mentor");
        var artifactShare = mentorSection.Settings.Single(setting =>
            setting.SourceSection == "Artifacts" && setting.Key == "SharePercent");
        Assert.Equal(2, artifactShare.Dependencies.Count);
        Assert.Contains(artifactShare.Dependencies, dependency => dependency.Section == "General" && dependency.Key == "Mode" && dependency.ExpectedValue == "Active");
        Assert.Contains(artifactShare.Dependencies, dependency => dependency.Section == "Artifacts" && dependency.Key == "Enabled" && dependency.ExpectedValue == "true");
        Assert.DoesNotContain(
            mod.Sections.SelectMany(section => section.Settings),
            setting => setting.SourceSection == "Performance");

        var session = new ConfigEditSession(new ConfigCatalogSnapshot(new[] { mod }));
        var mentorMode = mentorSection.Settings.Single(setting =>
            setting.SourceSection == "General" && setting.Key == "Mode");
        var toggleShortcut = mentorSection.Settings.Single(setting => setting.Key == "ToggleShortcut");
        var artifactEnabled = mentorSection.Settings.Single(setting =>
            setting.SourceSection == "Artifacts" && setting.Key == "Enabled");
        Assert.All(
            mod.Sections.SelectMany(section => section.Settings).Where(setting =>
                setting.SourceSection is "Sharing" or "Artifacts" or "Alchemy"),
            setting => Assert.Contains(setting.Dependencies, dependency =>
                dependency.Section == "General" && dependency.Key == "Mode" && dependency.ExpectedValue == "Active"));
        Assert.False(session.DependencySatisfied(artifactShare));
        Assert.False(session.DependencySatisfied(artifactEnabled));
        Assert.True(session.DependencySatisfied(toggleShortcut));
        var dependencyMessage = session.DescribeUnsatisfiedDependencies(artifactShare);
        Assert.Contains("Mentor = Active", dependencyMessage);
        Assert.Contains("Artifact sharing = true", dependencyMessage);
        session.Get(mentorMode).Stage("Active");
        Assert.True(session.DependencySatisfied(artifactEnabled));
        Assert.False(session.DependencySatisfied(artifactShare));
        session.Get(artifactEnabled).Stage("true");
        Assert.True(session.DependencySatisfied(artifactShare));
    }

    [Fact]
    public void LegacySingleDependencyMetadataRemainsSupported()
    {
        var config = new ConfigFile();
        var enabled = config.Bind("Feature", "Enabled", false, "feature");
        config.Bind(
            "Feature",
            "Amount",
            5,
            new ConfigDescription(
                "amount",
                null,
                new ModConfigMetadata(
                    0,
                    10,
                    dependencySection: "Feature",
                    dependencyKey: "Enabled")));
        var mod = ConfigCatalog.Build(new[] { new ConfigPluginSource("legacy", "Legacy", "1", config) }).Mods.Single();
        var amount = mod.Sections.Single().Settings.Single(setting => setting.Key == "Amount");
        var session = new ConfigEditSession(new ConfigCatalogSnapshot(new[] { mod }));

        Assert.Single(amount.Dependencies);
        Assert.False(session.DependencySatisfied(amount));
        enabled.Value = true;
        session.RevertAll();
        Assert.True(session.DependencySatisfied(amount));
    }

    [Fact]
    public void AutomataBinding_RemovesDeprecatedReleaseSettings()
    {
        var config = new ConfigFile();
        config.SeedSerialized("AutoBuy", "RuntimeProbeConfirmed", "true");
        config.SeedSerialized("AutoBuy", "ActivePurchaseLimitPerSession", "1");
        config.SeedSerialized("AutoCast", "RuntimeProbeConfirmed", "true");
        config.SeedSerialized("Safety", "AllowUnvalidatedActiveMode", "false");
        config.SeedSerialized("Research", "Mode", LegacyResearchAutomationMode.DryRun.ToString());
        config.SeedSerialized("AutoConcept", "AutoLevelSpells", "false");

        BepInExAutomataConfiguration.Bind(config);

        Assert.DoesNotContain(config, pair => pair.Key.Key.Contains("RuntimeProbe", StringComparison.Ordinal));
        Assert.DoesNotContain(config, pair => pair.Key.Key == "ActivePurchaseLimitPerSession");
        Assert.DoesNotContain(config, pair => pair.Key.Key == "AllowUnvalidatedActiveMode");
        Assert.DoesNotContain(config, pair => pair.Key.Section == "Research");
        Assert.DoesNotContain(config, pair => pair.Key.Section == "AutoConcept" && pair.Key.Key == "AutoLevelSpells");
        Assert.Contains(config, pair => pair.Key.Section == "AutoBuy" && pair.Key.Key == "AutoLevelSpells");
    }

    [Fact]
    public void Catalog_PreservesPresentationAndSerializedValues()
    {
        var config = new ConfigFile();
        var entry = config.Bind(
            "Performance",
            "BatchSize",
            8,
            new ConfigDescription("Purchases per queue fill.", new AcceptableValueRange<int>(1, 64)));
        entry.Value = 12;

        var setting = ConfigCatalog.Build(new[]
            {
                new ConfigPluginSource("plugin", "Plugin", "1.2.3", config),
            })
            .Mods.Single()
            .Sections.Single()
            .Settings.Single();

        Assert.Equal("Performance", setting.Section);
        Assert.Equal("BatchSize", setting.Key);
        Assert.Equal("Purchases per queue fill.", setting.Description);
        Assert.Equal("12", setting.CurrentSerializedValue);
        Assert.Equal("8", setting.DefaultSerializedValue);
        Assert.Contains("1", setting.AcceptableValuesDescription);
        Assert.Contains("64", setting.AcceptableValuesDescription);
    }

    [Fact]
    public void EditSession_StagesAppliesAndRevertsWithoutEarlyMutation()
    {
        var config = new ConfigFile();
        var enabled = config.Bind("General", "Enabled", true, "Enabled");
        var mode = config.Bind("General", "Mode", SampleMode.Disabled, "Mode");
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("plugin", "Plugin", "1.0.0", config),
        });
        var session = new ConfigEditSession(catalog);
        var settings = catalog.Mods.Single().Sections.Single().Settings.ToDictionary(setting => setting.Key);

        session.Get(settings["Enabled"]).Stage("false");
        session.Get(settings["Mode"]).Stage("Active");

        Assert.True(enabled.Value);
        Assert.Equal(SampleMode.Disabled, mode.Value);
        Assert.True(session.IsDirty);
        Assert.True(session.Apply(out var error), error);
        Assert.False(enabled.Value);
        Assert.Equal(SampleMode.Active, mode.Value);
        Assert.False(session.IsDirty);

        session.Get(settings["Enabled"]).Stage("true");
        session.RevertAll();
        Assert.Equal("false", session.Get(settings["Enabled"]).StagedSerialized);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void EditSession_ExternalChangesConflictUntilExplicitlyResolved()
    {
        var config = new ConfigFile();
        var count = config.Bind("General", "Count", 1, "Count");
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("plugin", "Plugin", "1.0.0", config),
        });
        var session = new ConfigEditSession(catalog);
        var setting = catalog.Mods.Single().Sections.Single().Settings.Single();

        session.Get(setting).Stage("2");
        count.Value = 3;

        Assert.True(session.RefreshExternalValues());
        Assert.Equal("2", session.Get(setting).StagedSerialized);
        Assert.Equal("3", session.Get(setting).ExternalSerialized);
        Assert.True(session.Get(setting).HasExternalConflict);
        Assert.False(session.Apply(out var conflict));
        Assert.Contains("Keep mine or Take live", conflict);
        Assert.Equal(3, count.Value);

        session.Get(setting).KeepStagedValue();
        Assert.True(session.Apply(out var error), error);
        Assert.Equal(2, count.Value);

        session.Get(setting).Stage("4");
        count.Value = 5;
        Assert.True(session.RefreshExternalValues());
        session.Get(setting).TakeExternalValue();
        Assert.False(session.Get(setting).IsDirty);
        Assert.Equal("5", session.Get(setting).StagedSerialized);
    }

    [Fact]
    public void EditSession_ApplyAndRevertAreScopedToTheSelectedMod()
    {
        var firstConfig = new ConfigFile();
        var secondConfig = new ConfigFile();
        var first = firstConfig.Bind("General", "Enabled", true, "Enabled");
        var second = secondConfig.Bind("General", "Enabled", true, "Enabled");
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("first", "First", "1.0.0", firstConfig),
            new ConfigPluginSource("second", "Second", "1.0.0", secondConfig),
        });
        var session = new ConfigEditSession(catalog);
        var firstMod = catalog.Mods.Single(mod => mod.Guid == "first");
        var secondMod = catalog.Mods.Single(mod => mod.Guid == "second");
        var firstEdit = session.Values.Single(value => value.Setting.PluginGuid == "first");
        var secondEdit = session.Values.Single(value => value.Setting.PluginGuid == "second");
        firstEdit.Stage("false");
        secondEdit.Stage("not-a-boolean");

        Assert.True(session.IsModDirty(firstMod));
        Assert.True(session.IsModValid(firstMod));
        Assert.False(session.IsModValid(secondMod));
        Assert.True(session.Apply(firstMod, out var error, out var applied), error);
        Assert.False(first.Value);
        Assert.True(second.Value);
        Assert.Single(applied);
        Assert.False(session.IsModDirty(firstMod));
        Assert.True(session.IsModDirty(secondMod));

        firstEdit.Stage("true");
        session.Revert(secondMod);
        Assert.True(firstEdit.IsDirty);
        Assert.False(secondEdit.IsDirty);
        Assert.False(first.Value);
        Assert.True(second.Value);
    }

    [Fact]
    public void EditSession_RejectsInvalidAndOutOfRangeValues()
    {
        var config = new ConfigFile();
        config.Bind("General", "Enabled", true, "Enabled");
        config.Bind(
            "General",
            "Count",
            4,
            new ConfigDescription("Count", new AcceptableValueRange<int>(1, 8)));
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("plugin", "Plugin", "1.0.0", config),
        });
        var session = new ConfigEditSession(catalog);
        var settings = catalog.Mods.Single().Sections.Single().Settings.ToDictionary(setting => setting.Key);

        session.Get(settings["Enabled"]).Stage("sometimes");
        Assert.False(session.IsValid);
        Assert.False(session.Apply(out _));

        session.Get(settings["Enabled"]).Stage("true");
        session.Get(settings["Count"]).Stage("12");
        Assert.False(session.IsValid);
        Assert.Contains("Range", session.Get(settings["Count"]).Error);
    }

    [Fact]
    public void EditSession_ValidatesKeyboardShortcutsBeforeApply()
    {
        var config = new ConfigFile();
        var shortcut = config.Bind(
            "General",
            "Shortcut",
            new KeyboardShortcut(KeyCode.Equals, KeyCode.LeftAlt),
            "Shortcut");
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("plugin", "Plugin", "1.0.0", config),
        });
        var session = new ConfigEditSession(catalog);
        var setting = catalog.Mods.Single().Sections.Single().Settings.Single();

        session.Get(setting).Stage("DefinitelyNotAKey + LeftAlt");

        Assert.False(session.Get(setting).IsValid);
        Assert.False(session.Apply(out _));
        Assert.Equal(KeyCode.Equals, shortcut.Value.MainKey);

        session.Get(setting).Stage(ConfigCatalog.Serialize(
            new KeyboardShortcut(KeyCode.Y, KeyCode.LeftControl)));

        Assert.True(session.Get(setting).IsValid, session.Get(setting).Error);
        Assert.True(session.Apply(out var error), error);
        Assert.Equal(KeyCode.Y, shortcut.Value.MainKey);
        Assert.Contains(KeyCode.LeftControl, shortcut.Value.Modifiers);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void EditSession_ApplyFailureRollsBackEveryWrittenEntryAndResavesOwner()
    {
        var config = new ConfigFile { ThrowOnSaveCall = 1 };
        var enabled = config.Bind("General", "Enabled", true, "Enabled");
        var count = config.Bind("General", "Count", 4, "Count");
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("plugin", "Plugin", "1.0.0", config),
        });
        var session = new ConfigEditSession(catalog);
        var settings = catalog.Mods.Single().Sections.Single().Settings.ToDictionary(setting => setting.Key);
        session.Get(settings["Enabled"]).Stage("false");
        session.Get(settings["Count"]).Stage("8");

        var applied = session.Apply(out var error);

        Assert.False(applied);
        Assert.Contains("simulated config save failure", error);
        Assert.True(enabled.Value);
        Assert.Equal(4, count.Value);
        Assert.Equal(2, config.SaveCalls);
        Assert.True(session.IsDirty);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void EditSession_ThrowingSettingChangedSubscriberLeavesNoPartialCommit()
    {
        var config = new ConfigFile();
        var first = config.Bind("General", "First", 1, "First");
        var second = config.Bind("General", "Second", 2, "Second");
        second.SettingChanged += (_, _) => throw new InvalidOperationException("simulated subscriber failure");
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("plugin", "Plugin", "1.0.0", config),
        });
        var session = new ConfigEditSession(catalog);
        var settings = catalog.Mods.Single().Sections.Single().Settings.ToDictionary(setting => setting.Key);
        session.Get(settings["First"]).Stage("10");
        session.Get(settings["Second"]).Stage("20");

        var applied = session.Apply(out var error);

        Assert.False(applied);
        Assert.Equal("simulated subscriber failure", error);
        Assert.Equal(1, first.Value);
        Assert.Equal(2, second.Value);
        Assert.Equal(1, config.SaveCalls);
        Assert.True(session.IsDirty);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void EditSession_MultipleConfigFilesRollbackAndResaveEveryOwner()
    {
        var firstConfig = new ConfigFile();
        var secondConfig = new ConfigFile { ThrowOnSaveCall = 1 };
        var first = firstConfig.Bind("General", "Value", 1, "Value");
        var second = secondConfig.Bind("General", "Value", 2, "Value");
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("first", "First", "1.0.0", firstConfig),
            new ConfigPluginSource("second", "Second", "1.0.0", secondConfig),
        });
        var session = new ConfigEditSession(catalog);
        session.Values.Single(value => ReferenceEquals(value.Setting.Source.ConfigFile, firstConfig)).Stage("10");
        session.Values.Single(value => ReferenceEquals(value.Setting.Source.ConfigFile, secondConfig)).Stage("20");

        var applied = session.Apply(out var error);

        Assert.False(applied);
        Assert.Contains("simulated config save failure", error);
        Assert.Equal(1, first.Value);
        Assert.Equal(2, second.Value);
        Assert.Equal(2, firstConfig.SaveCalls);
        Assert.Equal(2, secondConfig.SaveCalls);
        Assert.True(session.IsDirty);
    }
}
