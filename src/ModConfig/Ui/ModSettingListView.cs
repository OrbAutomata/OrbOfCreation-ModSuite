using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

/// <summary>
/// Renders one settings section. It owns only row views and reports edit events
/// back to the settings-page application boundary.
/// </summary>
internal sealed class ModSettingListView : IDisposable
{
    private const float MinimumSettingRowHeight = 86f;
    private const float SettingRowGap = 7f;
    private const float SettingRowTopInset = 4f;
    private const float SettingTitleTopInset = 5f;
    private const float SettingTitleHeight = 28f;
    private const float SettingDescriptionTopInset = 34f;
    private const float SettingDescriptionBottomInset = 6f;

    private readonly ConfigEditSession _session;
    private readonly RectTransform _content;
    private readonly TextMeshProUGUI _labelTemplate;
    private readonly Action _rebuildRequested;
    private readonly Action<ConfigEditValue?> _statusChanged;
    private readonly AutoItemsTemporaryItemPickerView _temporaryItemPicker;
    private readonly List<GameObject> _rows = new();
    private readonly Dictionary<ConfigEditValue, TextMeshProUGUI> _settingTitles = new();
    private ModConfigFeatureCommand? _featureCommand;
    private ModConfigFeatureCommandPresentation? _featurePresentation;
    private TextMeshProUGUI? _featureStatus;
    private TextMeshProUGUI? _featureButtonLabel;
    private Button? _featureButton;

    public ModSettingListView(
        ConfigEditSession session,
        RectTransform content,
        TextMeshProUGUI labelTemplate,
        Action rebuildRequested,
        Action<ConfigEditValue?> statusChanged)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _labelTemplate = labelTemplate ?? throw new ArgumentNullException(nameof(labelTemplate));
        _rebuildRequested = rebuildRequested ?? throw new ArgumentNullException(nameof(rebuildRequested));
        _statusChanged = statusChanged ?? throw new ArgumentNullException(nameof(statusChanged));
        _temporaryItemPicker = new AutoItemsTemporaryItemPickerView(
            _labelTemplate,
            _rebuildRequested,
            _statusChanged);
    }

    public float MeasuredDescriptionWidth { get; private set; }

    public float Render(
        IReadOnlyList<ConfigSettingDescriptor> settings,
        ModConfigFeatureCommand? featureCommand = null)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        Clear();
        MeasuredDescriptionWidth = ModSettingsLayout.CalculateDescriptionWidth(_content.rect.width);
        var contentHeight = 0f;
        if (featureCommand is not null)
            contentHeight += CreateFeatureHeader(featureCommand, contentHeight);
        foreach (var setting in settings)
            contentHeight += CreateRow(
                setting,
                contentHeight,
                MeasuredDescriptionWidth,
                _content.rect.width);
        RefreshEditFeedback();
        contentHeight = Math.Max(1f, contentHeight);
        _content.sizeDelta = new Vector2(0f, contentHeight);
        return contentHeight;
    }

    public void Clear()
    {
        ModConfigUiFactory.ClearObjects(_rows);
        _settingTitles.Clear();
        _featureCommand = null;
        _featurePresentation = null;
        _featureStatus = null;
        _featureButtonLabel = null;
        _featureButton = null;
        MeasuredDescriptionWidth = 0f;
    }

    public void RefreshFeaturePresentation()
    {
        if (_featureCommand is null) return;
        var presentation = _featureCommand.Presentation;
        if (_featurePresentation == presentation) return;
        _featureStatus!.text = presentation.StatusText;
        _featureButtonLabel!.text = presentation.ButtonLabel;
        ModConfigUiFactory.SetButtonActive(_featureButton!, presentation.IsActive);
        _featurePresentation = presentation;
    }

    private void RefreshEditFeedback()
    {
        foreach (var pair in _settingTitles)
        {
            var edit = pair.Key;
            var marker = edit.HasExternalConflict
                ? "[Conflict] "
                : !edit.IsValid
                    ? "[Invalid] "
                    : edit.IsDirty ? "[Unsaved] " : string.Empty;
            var title = marker + edit.Setting.DisplayName;
            if (!string.Equals(pair.Value.text, title, StringComparison.Ordinal))
                pair.Value.text = title;
        }
    }

    public void Dispose() => Clear();

    private float CreateFeatureHeader(ModConfigFeatureCommand command, float topOffset)
    {
        const float headerHeight = 112f;
        var presentation = command.Presentation;
        var row = ModConfigUiFactory.CreateRectObject(
            "Feature." + command.DisplayName,
            _content,
            new Vector2(0.01f, 1f),
            new Vector2(0.99f, 1f),
            ModConfigPalette.Row);
        var rect = (RectTransform)row.transform;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -topOffset - SettingRowTopInset);
        rect.sizeDelta = new Vector2(0f, headerHeight - SettingRowGap);
        _rows.Add(row);

        ModConfigUiFactory.CreateText(
            "Title",
            row.transform,
            new Vector2(0.025f, 0.64f),
            new Vector2(0.74f, 0.95f),
            _labelTemplate,
            command.DisplayName,
            TextAlignmentOptions.MidlineLeft,
            0.88f);
        _featureStatus = ModConfigUiFactory.CreateText(
            "Status",
            row.transform,
            new Vector2(0.025f, 0.26f),
            new Vector2(0.74f, 0.62f),
            _labelTemplate,
            presentation.StatusText,
            TextAlignmentOptions.MidlineLeft,
            0.56f);
        _featureButton = ModConfigUiFactory.CreateButton(
            "ImmediateMode",
            row.transform,
            new Vector2(0.77f, 0.35f),
            new Vector2(0.975f, 0.88f),
            _labelTemplate,
            presentation.ButtonLabel,
            () =>
            {
                command.Toggle();
                _session.RefreshExternalValues();
                _rebuildRequested();
            },
            active: presentation.IsActive);
        _featureButtonLabel = _featureButton.GetComponentInChildren<TextMeshProUGUI>();
        _featureCommand = command;
        _featurePresentation = presentation;
        ModConfigUiFactory.CreateText(
            "StagingHint",
            row.transform,
            new Vector2(0.025f, 0.025f),
            new Vector2(0.975f, 0.24f),
            _labelTemplate,
            "This control applies immediately. Settings below require Apply.",
            TextAlignmentOptions.MidlineLeft,
            0.52f);
        return headerHeight;
    }

    private float CreateRow(
        ConfigSettingDescriptor setting,
        float topOffset,
        float descriptionWidth,
        float contentWidth)
    {
        var edit = _session.Get(setting);
        var row = new GameObject(
            "Setting." + setting.Key,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        row.transform.SetParent(_content, false);
        var rowRect = (RectTransform)row.transform;
        rowRect.anchorMin = new Vector2(0.01f, 1f);
        rowRect.anchorMax = new Vector2(0.99f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = new Vector2(0f, -topOffset - SettingRowTopInset);
        rowRect.sizeDelta = new Vector2(0f, MinimumSettingRowHeight - SettingRowGap);
        row.GetComponent<Image>()!.color = ModConfigPalette.Row;
        _rows.Add(row);

        var keyText = ModConfigUiFactory.CreateText(
            "Key",
            row.transform,
            new Vector2(0.018f, 1f),
            new Vector2(0.53f, 1f),
            _labelTemplate,
            setting.DisplayName,
            TextAlignmentOptions.MidlineLeft,
            0.78f);
        _settingTitles.Add(edit, keyText);
        ModConfigUiFactory.SetTopAnchoredHeight(
            (RectTransform)keyText.transform,
            SettingTitleTopInset,
            SettingTitleHeight);

        var description = setting.Description;
        if (!string.IsNullOrWhiteSpace(setting.AcceptableValuesDescription))
            description += "  " + setting.AcceptableValuesDescription;
        description += setting.RestartRequired
            ? "  Restart required."
            : "  " + ModSettingsPage.SavedRuntimeMessage;
        var descriptionText = ModConfigUiFactory.CreateText(
            "Description",
            row.transform,
            new Vector2(0.018f, 1f),
            new Vector2(0.55f, 1f),
            _labelTemplate,
            description,
            TextAlignmentOptions.TopLeft,
            0.55f,
            TextOverflowModes.Overflow);
        var preferredDescriptionHeight = descriptionText.GetPreferredValues(description, descriptionWidth, 0f).y;
        var rowHeight = ModSettingsLayout.CalculateSettingRowHeight(preferredDescriptionHeight);
        AutoItemsTemporaryItemCatalogSnapshot? temporaryCatalog = null;
        if (AutoItemsTemporaryItemPickerView.AppliesTo(setting) &&
            _session.DependencySatisfied(setting) &&
            !edit.HasExternalConflict)
        {
            temporaryCatalog = _temporaryItemPicker.CaptureCatalog();
            rowHeight = Math.Max(
                rowHeight,
                _temporaryItemPicker.Measure(
                    edit,
                    temporaryCatalog,
                    AutoItemsTemporaryItemPickerView.CalculateEditorWidth(contentWidth),
                    MinimumSettingRowHeight));
        }
        var visibleRowHeight = rowHeight - SettingRowGap;
        rowRect.sizeDelta = new Vector2(0f, visibleRowHeight);
        ModConfigUiFactory.SetTopAnchoredHeight(
            (RectTransform)descriptionText.transform,
            SettingDescriptionTopInset,
            Math.Max(1f, visibleRowHeight - SettingDescriptionTopInset - SettingDescriptionBottomInset));

        if (!_session.DependencySatisfied(setting))
        {
            ModConfigUiFactory.CreateText(
                "Dependency",
                row.transform,
                new Vector2(0.58f, 0.2f),
                new Vector2(0.98f, 0.8f),
                _labelTemplate,
                _session.DescribeUnsatisfiedDependencies(setting),
                TextAlignmentOptions.Midline,
                0.6f);
            return rowHeight;
        }

        if (edit.HasExternalConflict)
        {
            CreateConflictEditor(row.transform, edit);
            return rowHeight;
        }

        CreateEditor(row.transform, setting, edit, temporaryCatalog);
        if (edit.IsEditable && !AutoItemsTemporaryItemPickerView.AppliesTo(setting))
        {
            ModConfigUiFactory.CreateButton(
                "Default",
                row.transform,
                new Vector2(0.86f, 0.18f),
                new Vector2(0.98f, 0.82f),
                _labelTemplate,
                "Default",
                () =>
                {
                    edit.StageDefault();
                    _rebuildRequested();
                });
        }
        return rowHeight;
    }

    private void CreateConflictEditor(Transform parent, ConfigEditValue edit)
    {
        ModConfigUiFactory.CreateText(
            "Conflict",
            parent,
            new Vector2(0.56f, 0.12f),
            new Vector2(0.77f, 0.88f),
            _labelTemplate,
            $"Mine: {edit.StagedSerialized}\nLive: {edit.ExternalSerialized}",
            TextAlignmentOptions.MidlineLeft,
            0.54f);
        ModConfigUiFactory.CreateButton(
            "KeepMine",
            parent,
            new Vector2(0.78f, 0.18f),
            new Vector2(0.88f, 0.82f),
            _labelTemplate,
            "Keep mine",
            () =>
            {
                edit.KeepStagedValue();
                _rebuildRequested();
            });
        ModConfigUiFactory.CreateButton(
            "TakeLive",
            parent,
            new Vector2(0.89f, 0.18f),
            new Vector2(0.99f, 0.82f),
            _labelTemplate,
            "Take live",
            () =>
            {
                edit.TakeExternalValue();
                _rebuildRequested();
            });
    }

    private void CreateEditor(
        Transform parent,
        ConfigSettingDescriptor setting,
        ConfigEditValue edit,
        AutoItemsTemporaryItemCatalogSnapshot? temporaryCatalog)
    {
        if (AutoItemsTemporaryItemPickerView.AppliesTo(setting))
        {
            _temporaryItemPicker.Render(
                parent,
                edit,
                temporaryCatalog ?? AutoItemsTemporaryItemCatalogSnapshot.Failed(
                    "The picker catalog was not captured for this setting."),
                AutoItemsTemporaryItemPickerView.CalculateEditorWidth(_content.rect.width));
            return;
        }
        switch (setting.Kind)
        {
            case ConfigEditorKind.Boolean:
                ModConfigUiFactory.CreateButton(
                    "Boolean",
                    parent,
                    new Vector2(0.58f, 0.18f),
                    new Vector2(0.84f, 0.82f),
                    _labelTemplate,
                    edit.StagedSerialized,
                    () =>
                    {
                        var current = bool.TryParse(edit.StagedSerialized, out var parsed) && parsed;
                        edit.Stage((!current).ToString());
                        _rebuildRequested();
                    });
                return;
            case ConfigEditorKind.Enum:
                CreateEnumEditor(parent, edit);
                return;
            case ConfigEditorKind.BoundedNumeric:
            case ConfigEditorKind.Numeric:
            case ConfigEditorKind.String:
            case ConfigEditorKind.KeyboardShortcut:
                CreateTextEditor(parent, edit);
                return;
            default:
                ModConfigUiFactory.CreateText(
                    "ReadOnly",
                    parent,
                    new Vector2(0.58f, 0.2f),
                    new Vector2(0.84f, 0.8f),
                    _labelTemplate,
                    edit.StagedSerialized,
                    TextAlignmentOptions.Midline,
                    0.65f);
                return;
        }
    }

    private void CreateEnumEditor(Transform parent, ConfigEditValue edit)
    {
        var names = Enum.GetNames(edit.Setting.SettingType);
        ModConfigUiFactory.CreateButton(
            "Enum",
            parent,
            new Vector2(0.58f, 0.18f),
            new Vector2(0.84f, 0.82f),
            _labelTemplate,
            edit.StagedSerialized,
            () =>
            {
                var current = Array.FindIndex(
                    names,
                    name => string.Equals(name, edit.StagedSerialized, StringComparison.OrdinalIgnoreCase));
                edit.Stage(names[(current + 1 + names.Length) % names.Length]);
                _rebuildRequested();
            });
    }

    private void CreateTextEditor(Transform parent, ConfigEditValue edit)
    {
        var inputObject = ModConfigUiFactory.CreateRectObject(
            "Input",
            parent,
            new Vector2(0.58f, 0.18f),
            new Vector2(0.84f, 0.82f),
            ModConfigPalette.Button);
        var text = ModConfigUiFactory.CreateText(
            "Text",
            inputObject.transform,
            new Vector2(0.04f, 0.05f),
            new Vector2(0.96f, 0.95f),
            _labelTemplate,
            edit.StagedSerialized,
            TextAlignmentOptions.MidlineLeft,
            0.62f);
        var input = inputObject.AddComponent<TMP_InputField>();
        input.targetGraphic = inputObject.GetComponent<Image>();
        input.textViewport = (RectTransform)text.transform;
        input.textComponent = text;
        input.text = edit.StagedSerialized;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.onSelect.AddListener(_ =>
            SteamKeyboardBridge.TryShow(input, edit.Setting.DisplayName, edit.Setting.Kind));
        input.onValueChanged.AddListener(value =>
        {
            edit.Stage(value);
            RefreshEditFeedback();
            _statusChanged(edit);
        });
        input.onEndEdit.AddListener(_ => _rebuildRequested());
    }
}
