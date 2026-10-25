using System;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbMentor;
using OrbModConfig;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class ModSettingsFeedbackTests
{
    [Fact]
    public void LiveFeaturePresentationUpdatesExistingControlsWithoutReplacingEditedRows()
    {
        using var fixture = new SettingsFixture();
        fixture.Page.Hide();
        var session = new ConfigEditSession(fixture.Catalog);
        var presentation = new ModConfigFeatureCommandPresentation("Waiting for wood.", true, "Turn off");
        var rebuilds = 0;
        var command = new ModConfigFeatureCommand("Feature", () => presentation, () => { });
        using var list = new ModSettingListView(
            session, fixture.Content, fixture.Template, () => rebuilds++, _ => { });
        list.Render(fixture.Catalog.Mods[0].Sections[0].Settings, command);
        var row = Child(fixture.Content, "Setting.Count");
        var input = Child(row, "Input").GetComponent<TMP_InputField>()!;
        Type(input, "7");
        fixture.Content.anchoredPosition = new Vector2(0f, 41f);
        var header = Child(fixture.Content, "Feature.Feature");
        var status = Child(header, "Status").GetComponent<TextMeshProUGUI>()!;
        var button = Child(header, "ImmediateMode").GetComponent<Button>()!;
        Assert.Contains("immediately", Child(header, "StagingHint").GetComponent<TextMeshProUGUI>()!.text);
        Assert.Same(fixture.Visuals.FeatureRailActiveFrame, button.GetComponent<Image>()!.sprite);

        // Detail-only changes and subsequent mode changes must both reach the same controls.
        presentation = presentation with { StatusText = "Waiting for stone." };
        list.RefreshFeaturePresentation();
        Assert.Equal("Waiting for stone.", status.text);
        presentation = new ModConfigFeatureCommandPresentation("Disabled", false, "Turn on");
        list.RefreshFeaturePresentation();
        list.RefreshFeaturePresentation();

        Assert.Equal("Disabled", status.text);
        Assert.Equal("Turn on", Child(button.transform, "Label").GetComponent<TextMeshProUGUI>()!.text);
        Assert.Same(fixture.Visuals.FeatureRailBaseFrame, button.GetComponent<Image>()!.sprite);
        Assert.Same(row, Child(fixture.Content, "Setting.Count"));
        Assert.Same(input, Child(row, "Input").GetComponent<TMP_InputField>());
        Assert.Equal("7", input.text);
        Assert.Equal("7", session.Get(fixture.Catalog.Mods[0].Sections[0].Settings.Single()).StagedSerialized);
        Assert.Equal(41f, fixture.Content.anchoredPosition.y);
        Assert.Equal(0, rebuilds);
    }

    [Fact]
    public void TextEditsShowInvalidAndUnsavedMarkersUntilAppliedOrReverted()
    {
        using var fixture = new SettingsFixture();
        var originalInput = fixture.Input;
        Type(originalInput, "invalid");
        Assert.StartsWith("[Invalid] ", fixture.Title.text);
        Assert.Contains("Count:", fixture.Status.text);
        Assert.False(fixture.Apply.interactable);
        Assert.Same(originalInput, fixture.Input);

        fixture.Page.Hide();
        fixture.Page.ShowSection(0, 0);
        Assert.StartsWith("[Invalid] ", fixture.Title.text);
        Assert.Contains("Count:", fixture.Status.text);
        Assert.False(fixture.Apply.interactable);

        Type(fixture.Input, "7");
        Assert.StartsWith("[Unsaved] ", fixture.Title.text);
        Assert.Contains("press Apply", fixture.Status.text);
        Assert.Equal(5, fixture.Count.Value);
        Assert.True(fixture.Apply.interactable);
        fixture.Apply.onClick.Invoke();
        Assert.Equal(7, fixture.Count.Value);
        Assert.Equal("Count", fixture.Title.text);
        Assert.False(fixture.Apply.interactable);

        Type(fixture.Input, "8");
        fixture.Revert.onClick.Invoke();
        Assert.Equal("7", fixture.Input.text);
        Assert.Equal("Count", fixture.Title.text);
        Assert.Equal(7, fixture.Count.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExternalConflictsKeepVisibleMarkersUntilThePlayerChooses(bool keepMine)
    {
        using var fixture = new SettingsFixture();
        Type(fixture.Input, "8");
        fixture.Count.Value = 9;
        fixture.Page.RefreshExternalValues();
        Assert.StartsWith("[Conflict] ", fixture.Title.text);
        Assert.Contains("Keep mine or Take live", fixture.Status.text);
        Assert.False(fixture.Apply.interactable);
        Child(fixture.Row, keepMine ? "KeepMine" : "TakeLive").GetComponent<Button>()!.onClick.Invoke();

        if (keepMine)
        {
            Assert.StartsWith("[Unsaved] ", fixture.Title.text);
            Assert.Equal(9, fixture.Count.Value);
            fixture.Apply.onClick.Invoke();
            Assert.Equal(8, fixture.Count.Value);
        }
        else
        {
            Assert.Equal("9", fixture.Input.text);
            Assert.Equal(9, fixture.Count.Value);
        }
        Assert.Equal("Count", fixture.Title.text);
        Assert.False(fixture.Apply.interactable);
    }

    private static void Type(TMP_InputField input, string value)
    {
        input.text = value;
        input.onValueChanged.Invoke(value);
    }

    private static Transform Child(Transform parent, string name) =>
        Enumerable.Range(0, parent.childCount)
            .Select(parent.GetChild)
            // Unity defers destruction; select the current row when a rebuild shares a frame.
            .Last(child => child.gameObject.name == name);

    private sealed class SettingsFixture : IDisposable
    {
        private readonly GameObject _root = new("SettingsFeedback", typeof(RectTransform));
        private readonly AutomataFeatureStatuses _statuses;
        internal readonly ConfigEntry<int> Count;
        internal readonly ConfigCatalogSnapshot Catalog;
        internal readonly RectTransform Content;
        internal readonly TextMeshProUGUI Template;
        internal readonly TextMeshProUGUI Status;
        internal readonly Button Apply;
        internal readonly Button Revert;
        internal readonly ModSettingsPage Page;
        internal readonly NativeFeatureRailVisualPrimitives Visuals;
        internal Transform Row => Child(Content, "Setting.Count");
        internal TextMeshProUGUI Title => Child(Row, "Key").GetComponent<TextMeshProUGUI>()!;
        internal TMP_InputField Input => Child(Row, "Input").GetComponent<TMP_InputField>()!;

        internal SettingsFixture()
        {
            var file = new ConfigFile();
            Count = file.Bind("Settings", "Count", 5,
                new ConfigDescription("How many actions to prepare.", new AcceptableValueRange<int>(1, 10)));
            Catalog = ConfigCatalog.Build(new[] { new ConfigPluginSource("plugin.test", "Test", "test", file) });
            Content = (RectTransform)ModConfigUiFactory.CreateRectObject(
                "Content", _root.transform, Vector2.zero, Vector2.one).transform;
            Content.rect = new Rect(0f, 0f, 1000f, 600f);
            var viewport = (RectTransform)_root.transform;
            viewport.rect = new Rect(0f, 0f, 1000f, 400f);
            var scroll = _root.AddComponent<ScrollRect>();
            scroll.content = Content;
            scroll.viewport = viewport;
            Template = new GameObject("Template", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            Template.transform.SetParent(_root.transform, false);
            Template.fontSize = 24f;
            Status = new GameObject("Status", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            Status.transform.SetParent(_root.transform, false);
            Apply = new GameObject("Apply", typeof(RectTransform)).AddComponent<Button>();
            Apply.transform.SetParent(_root.transform, false);
            Revert = new GameObject("Revert", typeof(RectTransform)).AddComponent<Button>();
            Revert.transform.SetParent(_root.transform, false);
            var prototype = new GameObject("NativeRailPrototype", typeof(RectTransform));
            prototype.transform.SetParent(_root.transform, false);
            Visuals = new NativeFeatureRailVisualPrimitives(
                new Sprite(), new Sprite(), new Sprite(), new Sprite(),
                new Sprite(), new Sprite(), new Sprite(), new Sprite());
            ModConfigUiFactory.UseNativeVisuals(Visuals);
            var suiteFile = new ConfigFile();
            var configuration = BepInExAutomataConfiguration.Bind(suiteFile);
            var mentor = MentorConfig.Bind(suiteFile);
            configuration.AttachMentor(mentor);
            var store = new AutomataConfigurationStore(configuration, (_, _) => { });
            _statuses = new AutomataFeatureStatuses(store.Current, 1, new FeatureStatusRegistry());
            var commands = new ModConfigFeatureCommands(
                AutomationFeatureControlRegistry.Create(store, _statuses, new SpellLevelCapabilityState(), mentor),
                new EmergencyStopControl(store, _ => { }));
            Page = new ModSettingsPage(Catalog, Template, new ManualLogSource(), Content,
                scroll, Status, Apply, Revert, commands);
            Page.ShowSection(0, 0);
        }

        public void Dispose()
        {
            Page.Dispose();
            _statuses.Dispose();
            UnityEngine.Object.Destroy(_root);
        }
    }
}
