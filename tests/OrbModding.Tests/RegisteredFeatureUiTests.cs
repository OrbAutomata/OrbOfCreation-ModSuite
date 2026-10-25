using System;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbMentor;
using OrbModConfig;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace OrbModding.Tests;

public sealed class RegisteredFeatureUiTests
{
    [Theory]
    [InlineData("AutoLevels", "Advanced")]
    [InlineData("QualityOfLife", "General")]
    public void RegisteredPageAndDrawerUseTheContributionsDeclaredStaticAsset(string featureId, string expectedIcon)
    {
        using var fixture = new Fixture(featureId);
        Assert.Equal(Enum.Parse<NativeFeatureIcon>(expectedIcon), fixture.Contribution.IconIdentity);
        var page = ModConfigTopNavigation.Build(fixture.Catalog, 0).Single(page =>
            page.Kind == ModConfigTopPageKind.PluginSettings && page.Label == fixture.Contribution.PageLabel);
        Assert.Equal(fixture.Contribution.IconIdentity, page.Icon);
        var registration = fixture.Registry.Features.Single(registration => registration.FeatureId == featureId);
        Assert.Equal(fixture.Contribution.IconIdentity, registration.IconIdentity);
        var native = new QuickControlNativePrimitives((RectTransform)fixture.Root.transform,
            new NativeButtonStateVisualPrimitives(new Sprite(), new Sprite()), new Sprite(),
            new QuickControlNativeGeometry(Vector2.zero, Vector2.one, new Vector2(0f, 1f), Vector2.zero, new Vector2(64f, 64f)));
        bool Resolve(AutomationFeatureControlRegistration control, out Sprite? icon, out string reason) =>
            NativeFeatureIconResolver.TryResolve(control.IconIdentity, fixture.Visuals, out icon, out reason);
        Assert.True(QuickControlColumn.TryCreate(fixture.Registry, fixture.Emergency, native, true,
            out var column, out var reason, Resolve), reason);
        using (var controls = Assert.IsType<QuickControlColumn>(column))
        {
            Assert.True(controls.TryGetButton(featureId, out var button));
            var image = Child(button.transform, "Icon").GetComponent<Image>()!;
            Assert.Same(featureId == "AutoLevels" ? fixture.Visuals.AdvancedIcon : fixture.Visuals.GeneralIcon, image.sprite);
            Assert.False(fixture.Contribution.IsConfigured(fixture.Store.Current));
            button.onClick.Invoke();
            Assert.True(fixture.Contribution.IsConfigured(fixture.Store.Current));
            Assert.True(fixture.Statuses.Get(featureId).Current.ConfiguredEnabled);
        }
    }

    [Theory]
    [InlineData("AutoLevels", "SpellCostSeconds", "2")]
    [InlineData("QualityOfLife", "MutePassiveNotifications", "False")]
    public void RegisteredModeUsesImmediateHeaderWhileFeaturePolicyRemainsStaged(string featureId, string policyKey, string proposed)
    {
        using var fixture = new Fixture(featureId);
        var mod = fixture.Catalog.Mods.Single();
        var sectionIndex = Array.FindIndex(mod.Sections.ToArray(), section => section.Name == fixture.Contribution.PageLabel);
        fixture.Page.ShowSection(0, sectionIndex);
        Assert.DoesNotContain(Enumerable.Range(0, fixture.Content.childCount).Select(fixture.Content.GetChild),
            row => row.gameObject.name == "Setting.Mode");
        var mode = mod.Sections[sectionIndex].Settings.Single(setting => setting.Key == "Mode");
        var policy = mod.Sections[sectionIndex].Settings.Single(setting => setting.Key == policyKey);
        Assert.True(ModSettingsPage.IsImmediateCommandSetting(mode));
        Assert.False(ModSettingsPage.IsImmediateCommandSetting(policy));
        var originalPolicy = policy.Source.GetSerializedValue();
        var header = Child(fixture.Content, "Feature." + fixture.Contribution.DisplayName);
        Child(header, "ImmediateMode").GetComponent<Button>()!.onClick.Invoke();
        Assert.True(fixture.Contribution.IsConfigured(fixture.Store.Current));
        Assert.Equal("Active", mode.Source.GetSerializedValue());
        Assert.False(fixture.Apply.interactable);

        var row = Child(fixture.Content, "Setting." + policyKey);
        if (policy.Kind == ConfigEditorKind.Boolean)
            Child(row, "Boolean").GetComponent<Button>()!.onClick.Invoke();
        else
        {
            var input = Child(row, "Input").GetComponent<TMP_InputField>()!;
            input.text = proposed;
            input.onValueChanged.Invoke(proposed);
        }
        Assert.Equal(originalPolicy, policy.Source.GetSerializedValue());
        Assert.False(fixture.Store.TryPublishPending());
        Assert.True(fixture.Apply.interactable);
        fixture.Apply.onClick.Invoke();
        fixture.Store.PublishPending();
        Assert.Equal(proposed, policy.Source.GetSerializedValue(), ignoreCase: true);
        Assert.True(fixture.Contribution.IsConfigured(fixture.Store.Current));
    }

    [Theory]
    [InlineData("AutoLevels", "SpellCostSeconds", "Input")]
    [InlineData("QualityOfLife", "MutePassiveNotifications", "Boolean")]
    public void LiveHeaderReflowPreservesValidPageScrollAndClampsOnlyAfterContentShrinks(
        string featureId, string policyKey, string editorName)
    {
        using var fixture = new Fixture(featureId);
        fixture.Registry.Features.Single(control => control.FeatureId == featureId).Toggle();
        var reporter = fixture.Statuses.Get(featureId);
        reporter.ObserveOperational();
        ((RectTransform)fixture.Root.transform).rect = new Rect(0f, 0f, 1000f, 80f);
        var section = Array.FindIndex(fixture.Catalog.Mods[0].Sections.ToArray(),
            section => section.Name == fixture.Contribution.PageLabel);
        fixture.Page.ShowSection(0, section);
        var row = Child(fixture.Content, "Setting." + policyKey);
        var editor = Child(row, editorName);
        var initialHeight = fixture.Content.sizeDelta.y;
        fixture.Content.anchoredPosition = new Vector2(0f, 41f);
        reporter.Observe(true, FeatureStatusState.TemporarilyBlocked, FeatureStatusReasonCode.NativeBusy,
            "Casting Output: next cost exceeds the Magic income budget.\n" +
            "Casting Reserve: next drain needs 300 seconds of stored resource buffer.\n" +
            "Concept speed: next level is waiting for a larger Knowledge income budget.\n" +
            "Alchemy speed: next drain would exceed the current mana buffer.");

        fixture.Page.RefreshFeaturePresentation();

        Assert.True(fixture.Content.sizeDelta.y > initialHeight);
        Assert.Equal(41f, fixture.Content.anchoredPosition.y);
        Assert.Same(row, Child(fixture.Content, "Setting." + policyKey));
        Assert.Same(editor, Child(row, editorName));
        fixture.Content.anchoredPosition = new Vector2(0f, fixture.Content.sizeDelta.y - 80f);
        reporter.ObserveOperational();
        fixture.Page.RefreshFeaturePresentation();
        Assert.Equal(initialHeight, fixture.Content.sizeDelta.y, 3);
        Assert.Equal(initialHeight - 80f, fixture.Content.anchoredPosition.y, 3);
        Assert.Same(editor, Child(row, editorName));
    }

    [Fact]
    public void OperationalDetailsReachTheLiveHeaderQuickTooltipAndRuntimeCardWithoutReplacingEditors()
    {
        using var fixture = new Fixture("AutoLevels");
        var control = fixture.Registry.Features.Single(control => control.FeatureId == "AutoLevels");
        control.Toggle();
        var reporter = fixture.Statuses.Get("AutoLevels");
        reporter.ObserveOperational(detail: "Waiting for the next evaluation.");
        var section = Array.FindIndex(fixture.Catalog.Mods[0].Sections.ToArray(), section => section.Name == "Auto Levels");
        fixture.Page.ShowSection(0, section);
        var row = Child(fixture.Content, "Setting.SpellCostSeconds");
        var input = Child(row, "Input").GetComponent<TMP_InputField>()!;
        input.text = "2";
        input.onValueChanged.Invoke("2");
        var header = Child(fixture.Content, "Feature.Auto Levels");
        var headerStatus = Child(header, "Status").GetComponent<TextMeshProUGUI>()!;
        var headerButton = Child(header, "ImmediateMode").GetComponent<Button>()!;
        var transitions = 0;
        var snapshots = 0;
        fixture.FeatureStatuses.Transitioned += _ => transitions++;
        fixture.FeatureStatuses.SnapshotChanged += () => snapshots++;
        const string details = "Casting Output: next cost 250; magic-budget-250 is the current resource income budget.\n" +
            "Casting Reserve: next drain needs reserve-buffer-300 seconds of stored resource coverage.\n" +
            "Concept speed: next level waits until the Knowledge income reaches knowledge-budget-800.\n" +
            "Alchemy speed: next drain remains within mana-buffer-300 seconds of current resource coverage.";

        Assert.False(reporter.ObserveOperational(detail: details));
        fixture.Page.RefreshFeaturePresentation();

        Assert.Equal(FeatureStatusState.Operational, reporter.Current.State);
        Assert.True(reporter.Current.Reason.IsEmpty);
        Assert.Equal(0, transitions);
        Assert.Equal(1, snapshots);
        Assert.Contains("Runtime: Operational", headerStatus.text);
        Assert.Contains("Casting Output:", headerStatus.text);
        Assert.Contains("Alchemy speed:", headerStatus.text);
        Assert.Contains("mana-buffer-300", headerStatus.text);
        Assert.DoesNotContain("Reason:", headerStatus.text);
        Assert.Same(input, Child(row, "Input").GetComponent<TMP_InputField>());
        Assert.Same(headerButton, Child(header, "ImmediateMode").GetComponent<Button>());
        Assert.Equal("2", input.text);
        Assert.Equal(ConfiguredIntentIconState.On, ConfiguredIntentIconButtonVisual.FromFeatureStatus(reporter.Current).State);
        var tooltip = string.Join("\n", control.Tooltip.GetTooltipNodes().Select(node => node.Text));
        Assert.Contains("mana-buffer-300", tooltip);
        Assert.Contains("Runtime: Operational", tooltip);

        var cardParent = ModConfigUiFactory.CreateRectObject("RuntimeCardParent", fixture.Root.transform, Vector2.zero, Vector2.one);
        var template = Child(fixture.Root.transform, "Template").GetComponent<TextMeshProUGUI>()!;
        using var cardView = new RuntimeDiagnosticsCardView((RectTransform)cardParent.transform, template);
        RuntimeDiagnosticsCard Card() => new(PluginIds.SuiteGuid, "Suite", "test", "Schema ready.",
            new[] { reporter.Current }, Array.Empty<RuntimeServiceDiagnosticsSnapshot>(), RuntimeDiagnosticsSeverity.Healthy);
        cardView.LayoutCard(Card(), 1000f, 0f, 0, 1);
        var renderedCard = Child(cardParent.transform, "TextCard");
        var body = Child(renderedCard, "Body").GetComponent<TextMeshProUGUI>()!;
        Assert.Contains("Auto Levels: Operational", body.text);
        Assert.Contains("Auto Levels details:", body.text);
        Assert.Contains("mana-buffer-300", body.text);
        Assert.Equal(ModConfigPalette.Row, renderedCard.GetComponent<Image>()!.color);

        reporter.ObserveOperational(detail: "Casting Output: updated-budget-500.");
        fixture.Page.RefreshFeaturePresentation();
        cardView.LayoutCard(Card(), 1000f, 0f, 0, 2);
        Assert.Same(body, Child(renderedCard, "Body").GetComponent<TextMeshProUGUI>());
        Assert.Contains("updated-budget-500", body.text);
        Assert.DoesNotContain("mana-buffer-300", body.text);
        Assert.Contains("updated-budget-500", headerStatus.text);
        Assert.Same(input, Child(row, "Input").GetComponent<TMP_InputField>());
        Assert.Equal("2", input.text);
        Assert.Equal(0, transitions);
        Assert.Equal(2, snapshots);
    }

    [Fact]
    public void UnregisteredFeatureAndExternalModeKeepTheirExactIdentity()
    {
        Assert.False(NativeFeatureIconResolver.TryResolve("Unregistered feature", null, out _, out var reason));
        Assert.Contains("Unregistered feature", reason);
        var file = new ConfigFile();
        var entry = file.Bind("QualityOfLife", "Mode", QualityOfLifeOperationMode.Disabled,
            new ConfigDescription("External setting", null, new ModConfigMetadata(0, 0, displaySection: "Quality of Life")));
        Assert.False(ModSettingsPage.IsImmediateCommandSetting(new ConfigSettingDescriptor("external.plugin", entry)));
        var unexpected = file.Bind("Unexpected", "Mode", QualityOfLifeOperationMode.Disabled,
            new ConfigDescription("Unregistered identity", null, new ModConfigMetadata(0, 0, displaySection: "Quality of Life")));
        Assert.False(ModSettingsPage.IsImmediateCommandSetting(new ConfigSettingDescriptor(PluginIds.SuiteGuid, unexpected)));
    }

    private static Transform Child(Transform parent, string name) =>
        Enumerable.Range(0, parent.childCount).Select(parent.GetChild)
            .Last(child => child.gameObject.name == name);

    private sealed class Fixture : IDisposable
    {
        internal readonly GameObject Root = new("RegisteredFeatureUi", typeof(RectTransform));
        internal readonly AutomataFeatureContribution Contribution;
        internal readonly ConfigCatalogSnapshot Catalog;
        internal readonly AutomataConfigurationStore Store;
        internal readonly AutomataFeatureStatuses Statuses;
        internal readonly FeatureStatusRegistry FeatureStatuses = new();
        internal readonly AutomationFeatureControlRegistry Registry;
        internal readonly EmergencyStopControl Emergency;
        internal readonly NativeFeatureRailVisualPrimitives Visuals;
        internal readonly RectTransform Content;
        internal readonly Button Apply;
        internal readonly ModSettingsPage Page;

        internal Fixture(string featureId)
        {
            Contribution = AutomataFeatureCatalog.All.Single(feature => feature.Id == featureId);
            var file = new ConfigFile();
            var configuration = BepInExAutomataConfiguration.Bind(file);
            var mentor = MentorConfig.Bind(file);
            configuration.AttachMentor(mentor);
            Statuses = new AutomataFeatureStatuses(configuration.Current, 1, FeatureStatuses);
            Store = new AutomataConfigurationStore(configuration, Statuses.ObserveConfiguration);
            Registry = AutomationFeatureControlRegistry.Create(Store, Statuses, new SpellLevelCapabilityState(), mentor);
            Emergency = new EmergencyStopControl(Store, _ => { });
            Catalog = ConfigCatalog.Build(new[] { new ConfigPluginSource(PluginIds.SuiteGuid, "Suite", "test", file) });
            Visuals = new NativeFeatureRailVisualPrimitives(new Sprite(), new Sprite(), new Sprite(), new Sprite(),
                new Sprite(), new Sprite(), new Sprite(), new Sprite());
            ModConfigUiFactory.UseNativeVisuals(Visuals);
            Content = (RectTransform)ModConfigUiFactory.CreateRectObject("Content", Root.transform, Vector2.zero, Vector2.one).transform;
            Content.rect = new Rect(0f, 0f, 1000f, 600f);
            var viewport = (RectTransform)Root.transform;
            viewport.rect = new Rect(0f, 0f, 1000f, 400f);
            var scroll = Root.AddComponent<ScrollRect>();
            scroll.content = Content;
            scroll.viewport = viewport;
            var template = new GameObject("Template", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            template.transform.SetParent(Root.transform, false);
            var status = new GameObject("Status", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            status.transform.SetParent(Root.transform, false);
            Apply = new GameObject("Apply", typeof(RectTransform)).AddComponent<Button>();
            Apply.transform.SetParent(Root.transform, false);
            var revert = new GameObject("Revert", typeof(RectTransform)).AddComponent<Button>();
            revert.transform.SetParent(Root.transform, false);
            Page = new ModSettingsPage(Catalog, template, new ManualLogSource(), Content, scroll, status, Apply, revert,
                new ModConfigFeatureCommands(Registry, Emergency));
        }

        public void Dispose()
        {
            Page.Dispose();
            Statuses.Dispose();
            UnityEngine.Object.Destroy(Root);
        }
    }
}
