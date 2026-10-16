using System;
using System.Linq;
using BepInEx.Configuration;
using OrbAutomata;
using OrbModConfig;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems;

public sealed class AutoItemsTemporaryItemPickerViewTests : IDisposable
{
    public AutoItemsTemporaryItemPickerViewTests()
    {
        ConsumableSO.All.Clear();
        EntityIdentityCatalogPublication.Publish(EntityIdentityCatalogSnapshot.Unbound(0));
        var prototype = new GameObject("prototype").AddComponent<MonoBehaviour>();
        ModConfigUiFactory.UseNativeVisuals(new NativeFeatureRailVisualPrimitives(
            prototype,
            new Sprite(),
            new Sprite(),
            new Sprite(),
            new Sprite(),
            new Sprite(),
            new Sprite(),
            new Sprite(),
            new Sprite()));
    }

    public void Dispose()
    {
        ConsumableSO.All.Clear();
        EntityIdentityCatalogPublication.Publish(EntityIdentityCatalogSnapshot.Unbound(0));
    }

    [Fact]
    public void PickerRowsUseAuditedFramesAndIconsAndStageThroughTheEditValue()
    {
        var itemId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var unknown = Guid.Parse("30000000-0000-0000-0000-000000000002");
        var icon = new Sprite();
        var edit = Edit($"{unknown:D}");
        var catalog = AutoItemsTemporaryItemCatalogSnapshot.Available(new[]
        {
            new AutoItemsTemporaryItemOption(
                itemId,
                AutoItemsConsumableFamily.Fruit,
                new[]
                {
                    new AutoItemsTemporaryItemFamily(
                        KnownEntities.ConsumableFruitType.Uuid,
                        AutoItemsConsumableFamily.Fruit,
                        "Fruit"),
                },
                "Bright Fruit",
                Stock: 4,
                icon),
        });
        var rebuilds = 0;
        ConfigEditValue? changed = null;
        var view = new AutoItemsTemporaryItemPickerView(
            LabelTemplate(),
            () => rebuilds++,
            value => changed = value);

        var parent = Parent();
        view.Render(parent, edit, catalog, editorWidth: 392f);

        Assert.Equal("0 of 1 approved", Text(Child(parent, "PickerStateLine")));
        var item = Child(parent, "PickerItem." + itemId.ToString("N"));
        Assert.Same(
            ModConfigUiFactory.NativeVisuals.FeatureRailBaseFrame,
            item.gameObject.GetComponent<Image>()!.sprite);
        Assert.Same(icon, Child(item, "Icon").gameObject.GetComponent<Image>()!.sprite);
        Assert.Contains("Bright Fruit", Text(Child(item, "Label")));
        Assert.Contains("Fruit · Stock 4", Text(Child(item, "Label")));
        Assert.Equal("Approve", Text(Child(item, "Approval")));
        Assert.Null(item.gameObject.GetComponent<Button>()!.targetGraphic);
        Assert.Equal(
            "Unresolvable stored UUID",
            Text(Child(Child(parent, "PickerUnresolvable." + unknown.ToString("N")), "Label"))
                .Split('\n')[0]);
        Assert.DoesNotContain(
            Children(parent),
            child => child.gameObject.GetComponent<TMP_InputField>() is not null);

        item.gameObject.GetComponent<Button>()!.onClick.Invoke();

        Assert.Contains(itemId.ToString("D"), edit.StagedSerialized);
        Assert.Contains(unknown.ToString("D"), edit.StagedSerialized);
        Assert.Same(edit, changed);
        Assert.Equal(1, rebuilds);
    }

    [Fact]
    public void HealthyEmptyAndDiscoveryFailureRenderDifferentExplicitStates()
    {
        var label = LabelTemplate();
        var view = new AutoItemsTemporaryItemPickerView(label, () => { }, _ => { });
        var stored = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var emptyEdit = Edit(string.Empty);
        var failedEdit = Edit(stored.ToString("D"));

        var healthyParent = Parent();
        view.Render(
            healthyParent,
            emptyEdit,
            AutoItemsTemporaryItemCatalogSnapshot.Available(
                Array.Empty<AutoItemsTemporaryItemOption>()),
            editorWidth: 392f);
        var failedParent = Parent();
        view.Render(
            failedParent,
            failedEdit,
            AutoItemsTemporaryItemCatalogSnapshot.Failed(
                "ConsumableSO.All was unreadable."),
            editorWidth: 392f);

        Assert.Equal(
            "No discovered temporary items yet.",
            Text(Child(healthyParent, "PickerEmptyState")));
        Assert.Equal(
            "Approval count unavailable — discovery read failed",
            Text(Child(failedParent, "PickerStateLine")));
        var failure = Child(failedParent, "PickerDiscoveryFailure");
        Assert.Contains("Discovery read failed", Text(Child(failure, "Reason")));
        Assert.NotEqual(
            Child(healthyParent, "PickerEmptyState").gameObject.GetComponent<TextMeshProUGUI>()!.color,
            Child(failure, "Reason").gameObject.GetComponent<TextMeshProUGUI>()!.color);
        var unresolved = Child(failedParent, "PickerUnresolvable." + stored.ToString("N"));
        Assert.Equal("Remove", Text(Child(unresolved, "Remove")));
        unresolved.gameObject.GetComponent<Button>()!.onClick.Invoke();
        Assert.Equal(string.Empty, failedEdit.StagedSerialized);
    }

    [Theory]
    [InlineData(640f)]
    [InlineData(1000f)]
    [InlineData(1440f)]
    public void FailureCompositionMeasuresDisjointStateAndPanelRects(float contentWidth)
    {
        var file = new ConfigFile();
        var automata = BepInExAutomataConfiguration.Bind(file);
        automata.AutoItemsMode.Value = AutoItemsOperationMode.Active;
        var family = new ConsumableTypeSO { DisplayName = string.Empty };
        family.SetGuid(KnownEntities.ConsumableFruitType.Uuid);
        var nativeItem = new ConsumableSO
        {
            DisplayName = "Continuous Coconut",
            Icon = new Sprite(),
            visible = true,
        };
        nativeItem.SetGuid(Guid.Parse("a1799c52-f9ff-4556-b052-f577ac3e7270"));
        nativeItem.consumableTypes.Add(family);
        ConsumableSO.All.Add(nativeItem);
        EntityIdentityCatalogPublication.Publish(Catalog());
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource(
                PluginIds.SuiteGuid,
                "Orb Of Creation ModSuite",
                "test",
                file),
        });
        var settings = catalog.Mods.Single().Sections
            .Single(section => section.Name == "Auto Items")
            .Settings
            .Where(setting => !ModSettingsPage.IsImmediateCommandSetting(setting))
            .ToArray();
        var contentObject = new GameObject("content", typeof(RectTransform));
        var content = (RectTransform)contentObject.transform;
        content.rect = new Rect(0f, 0f, contentWidth, 800f);
        var session = new ConfigEditSession(catalog);
        using var list = new ModSettingListView(
            session,
            content,
            LabelTemplate(),
            () => { },
            _ => { });

        list.Render(settings);

        var editorWidth = AutoItemsTemporaryItemPickerView.CalculateEditorWidth(contentWidth);
        var row = (RectTransform)Child(content, "Setting.TemporaryItemAllowlist");
        var state = (RectTransform)Child(row, "PickerStateLine");
        var defaultButton = (RectTransform)Child(row, "Default");
        var panel = (RectTransform)Child(row, "PickerDiscoveryFailure");
        Assert.True(Bottom(state) <= Top(panel));
        Assert.True(Bottom(defaultButton) <= Top(panel));
        Assert.True(state.anchorMax.x <= defaultButton.anchorMin.x);
        Assert.True(Bottom(panel) <= row.sizeDelta.y);

        var stateText = state.gameObject.GetComponent<TextMeshProUGUI>()!;
        var statePreferred = stateText.GetPreferredValues(
            stateText.text,
            editorWidth * 0.7f,
            0f).y;
        Assert.True(statePreferred <= state.sizeDelta.y);

        var reasonText = Child(panel, "Reason").gameObject.GetComponent<TextMeshProUGUI>()!;
        Assert.Contains("has no live catalog name", reasonText.text);
        var reasonPreferred = reasonText.GetPreferredValues(
            reasonText.text,
            editorWidth * 0.93f,
            0f).y;
        Assert.True(reasonPreferred <= panel.sizeDelta.y * 0.84f + 0.01f);
    }

    [Fact]
    public void AutoItemsPageComposesPickerInsteadOfGenericTextInput()
    {
        var file = new ConfigFile();
        var automata = BepInExAutomataConfiguration.Bind(file);
        automata.AutoItemsMode.Value = AutoItemsOperationMode.Active;
        var itemId = Guid.Parse("40000000-0000-0000-0000-000000000001");
        var family = new ConsumableTypeSO { DisplayName = "Fruit" };
        family.SetGuid(KnownEntities.ConsumableFruitType.Uuid);
        var nativeItem = new ConsumableSO
        {
            DisplayName = "Picker Fruit",
            Icon = new Sprite(),
            visible = true,
        };
        nativeItem.SetGuid(itemId);
        nativeItem.SetStock(3, 0, 0);
        nativeItem.consumableTypes.Add(family);
        ConsumableSO.All.Add(nativeItem);
        EntityIdentityCatalogPublication.Publish(Catalog());

        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource(
                PluginIds.SuiteGuid,
                "Orb Of Creation ModSuite",
                "test",
                file),
        });
        var mod = catalog.Mods.Single();
        var settings = mod.Sections
            .Single(section => section.Name == "Auto Items")
            .Settings
            .Where(setting => !ModSettingsPage.IsImmediateCommandSetting(setting))
            .ToArray();
        var contentObject = new GameObject("content", typeof(RectTransform));
        var content = (RectTransform)contentObject.transform;
        content.rect = new Rect(0f, 0f, 1000f, 800f);
        var session = new ConfigEditSession(catalog);
        using var list = new ModSettingListView(
            session,
            content,
            LabelTemplate(),
            () => { },
            _ => { });

        list.Render(settings);

        var row = Child(content, "Setting.TemporaryItemAllowlist");
        Assert.Contains(Children(row), child => child.gameObject.name == "PickerStateLine");
        Assert.Contains(
            Children(row),
            child => child.gameObject.name == "PickerItem." + itemId.ToString("N"));
        Assert.DoesNotContain(Children(row), child => child.gameObject.name == "Input");
        Assert.DoesNotContain(
            Children(row),
            child => child.gameObject.GetComponent<TMP_InputField>() is not null);

        Child(row, "PickerItem." + itemId.ToString("N"))
            .gameObject.GetComponent<Button>()!.onClick.Invoke();
        var allowlist = settings.Single(setting => setting.Key == "TemporaryItemAllowlist");
        Assert.Equal(itemId.ToString("D"), session.Get(allowlist).StagedSerialized);
        Assert.True(session.Apply(mod, out var error, out var applied), error);
        Assert.Equal(itemId.ToString("D"), automata.AutoItemsTemporaryItemAllowlist.Value);
        Assert.Contains(applied, setting => ReferenceEquals(setting, allowlist));
    }

    private static ConfigEditValue Edit(string initialValue)
    {
        var entry = new ConfigFile().Bind(
            "AutoItems",
            "TemporaryItemAllowlist",
            initialValue,
            "test");
        return new ConfigEditValue(
            new ConfigSettingDescriptor(PluginIds.SuiteGuid, entry));
    }

    private static TextMeshProUGUI LabelTemplate() =>
        new GameObject("label", typeof(TextMeshProUGUI))
            .GetComponent<TextMeshProUGUI>()!;

    private static Transform Parent() =>
        new GameObject("parent", typeof(RectTransform)).transform;

    private static Transform Child(Transform parent, string name) =>
        Children(parent).Single(child => child.gameObject.name == name);

    private static Transform[] Children(Transform parent) =>
        Enumerable.Range(0, parent.childCount).Select(parent.GetChild).ToArray();

    private static string Text(Transform transform) =>
        transform.gameObject.GetComponent<TextMeshProUGUI>()!.text;

    private static float Top(RectTransform rect) => -rect.anchoredPosition.y;

    private static EntityIdentityCatalogSnapshot Catalog()
    {
        var rows = new System.Collections.Generic.Dictionary<Guid, EntityIdentityName>();
        foreach (var item in ConsumableSO.All)
        {
            rows[item.GetGuid()] = new EntityIdentityName(
                item.GetGuid(), nameof(ConsumableSO), item.GetName(), item.name);
            foreach (var family in item.consumableTypes)
            {
                rows[family.GetGuid()] = new EntityIdentityName(
                    family.GetGuid(), nameof(ConsumableTypeSO), family.GetName(), family.name);
            }
        }
        return EntityIdentityCatalogSnapshot.Bound(
            1,
            rows.Values.OrderBy(row => row.EntityId).ToArray());
    }

    private static float Bottom(RectTransform rect) => Top(rect) + rect.sizeDelta.y;
}
