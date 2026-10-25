using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using OrbModding.Common;
using OrbAutomata;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

/// <summary>
/// Owns the editable-settings page, including staged values, section navigation,
/// responsive row layout, and per-plugin navigation memory. The panel shell only
/// decides which top-level page is visible.
/// </summary>
internal sealed class ModSettingsPage : IDisposable
{
    internal const string SavedRuntimeMessage = "Saved setting; runtime effect is reported separately.";
    internal const string ConfigurationSavedMessage = "Configuration saved.";
    private readonly ConfigCatalogSnapshot _catalog;
    private readonly ConfigEditSession _session;
    private readonly TextMeshProUGUI _labelTemplate;
    private readonly ManualLogSource _log;
    private readonly RectTransform _content;
    private readonly ScrollRect _scroll;
    private readonly TextMeshProUGUI _statusText;
    private readonly Button _applyButton;
    private readonly Button _revertButton;
    private readonly Dictionary<int, ModSettingsNavigationState> _navigation = new();
    private readonly ModSettingListView _settingList;
    private readonly ModConfigFeatureCommands _featureCommands;
    private int _selectedModIndex;
    private int _selectedSectionIndex;
    private bool _visible;
    private bool _disposed;

    public ModSettingsPage(
        ConfigCatalogSnapshot catalog,
        TextMeshProUGUI labelTemplate,
        ManualLogSource log,
        RectTransform content,
        ScrollRect scroll,
        TextMeshProUGUI statusText,
        Button applyButton,
        Button revertButton,
        ModConfigFeatureCommands featureCommands)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _session = new ConfigEditSession(catalog);
        _labelTemplate = labelTemplate ?? throw new ArgumentNullException(nameof(labelTemplate));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _scroll = scroll ?? throw new ArgumentNullException(nameof(scroll));
        _statusText = statusText ?? throw new ArgumentNullException(nameof(statusText));
        _applyButton = applyButton ?? throw new ArgumentNullException(nameof(applyButton));
        _revertButton = revertButton ?? throw new ArgumentNullException(nameof(revertButton));
        _featureCommands = featureCommands ?? throw new ArgumentNullException(nameof(featureCommands));
        _settingList = new ModSettingListView(
            _session,
            _content,
            _labelTemplate,
            () => RebuildSettings(),
            RefreshStatus);
        _applyButton.onClick.RemoveAllListeners();
        _applyButton.onClick.AddListener(Apply);
        _revertButton.onClick.RemoveAllListeners();
        _revertButton.onClick.AddListener(Revert);
    }

    public bool IsVisible => _visible;

    public ModConfigNavigationBookmark CaptureBookmark()
    {
        if (!_visible || _catalog.Mods.Count == 0) return ModConfigNavigationBookmark.Runtime;
        CaptureNavigation();
        var mod = _catalog.Mods[_selectedModIndex];
        var sectionName = mod.Sections.Count == 0
            ? string.Empty
            : mod.Sections[_selectedSectionIndex].Name;
        return new ModConfigNavigationBookmark(
            mod.Guid,
            sectionName,
            Math.Max(0f, _content.anchoredPosition.y));
    }

    public void RestoreBookmark(ModConfigNavigationBookmark bookmark)
    {
        if (bookmark.IsRuntime || bookmark.IsStrategist) return;
        var pageIndex = ModConfigNavigationBookmarkPolicy.ResolveTopPageIndex(_catalog, bookmark);
        var page = ModConfigTopNavigation.Build(_catalog, attentionCount: 0)[pageIndex];
        if (page.Kind != ModConfigTopPageKind.PluginSettings) return;
        var pluginIndex = page.PluginIndex;
        var mod = _catalog.Mods[pluginIndex];
        _navigation[pluginIndex] = new ModSettingsNavigationState(
            ModConfigNavigationBookmarkPolicy.ResolveSectionIndex(mod, bookmark),
            Math.Max(0f, bookmark.ScrollOffset));
    }

    public void ShowSection(int pluginIndex, int sectionIndex)
    {
        ThrowIfDisposed();
        CaptureNavigation();
        // A quick control or external writer may have changed mode while this page was hidden.
        // Reconcile the same edit session before deciding which dependent editors to render.
        _session.RefreshExternalValues();
        _visible = true;
        _selectedModIndex = Math.Max(0, Math.Min(pluginIndex, _catalog.Mods.Count - 1));
        var mod = _catalog.Mods[_selectedModIndex];
        if (mod.Sections.Count == 0)
        {
            _selectedSectionIndex = 0;
            _settingList.Clear();
            _content.sizeDelta = new Vector2(0f, 1f);
            RestoreScrollOffset(0f, 1f);
            RefreshStatus();
            return;
        }
        var rememberedOffset = _navigation.TryGetValue(_selectedModIndex, out var remembered) &&
                               remembered.SectionIndex == sectionIndex
            ? remembered.ScrollOffset
            : 0f;
        _selectedSectionIndex = Math.Max(0, Math.Min(sectionIndex, mod.Sections.Count - 1));
        RebuildSettings(rememberedOffset);
    }

    public void Hide()
    {
        if (_disposed || !_visible) return;
        CaptureNavigation();
        _visible = false;
        _settingList.Clear();
    }

    public void RefreshExternalValues()
    {
        if (!_disposed && _session.RefreshExternalValues() && _visible) RebuildSettings();
    }

    public void RefreshFeaturePresentation()
    {
        if (_disposed || !_visible || !_settingList.RefreshFeaturePresentation()) return;
        var requestedOffset = _content.anchoredPosition.y;
        var clampedOffset = ModSettingsLayout.ClampScrollOffset(requestedOffset, _content.sizeDelta.y,
            _scroll.viewport?.rect.height ?? 0f);
        if (Math.Abs(clampedOffset - requestedOffset) > 0.01f)
            RestoreScrollOffset(clampedOffset, _content.sizeDelta.y);
    }

    public void RefreshResponsiveLayout()
    {
        if (_disposed || !_visible || _catalog.Mods.Count == 0) return;
        var mod = _catalog.Mods[_selectedModIndex];
        if (mod.Sections.Count == 0) return;
        var descriptionWidth = ModSettingsLayout.CalculateDescriptionWidth(_content.rect.width);
        if (ModSettingsLayout.DescriptionWidthChanged(_settingList.MeasuredDescriptionWidth, descriptionWidth))
            RebuildSettings();
    }

    public void RefreshStatus(ConfigEditValue? changed = null)
    {
        if (_disposed || !_visible) return;
        if (_catalog.Mods.Count == 0)
        {
            SetStatus("No loaded plugins expose configuration entries or schema status.", invalid: false);
            SetFooterInteractable(false, false);
            return;
        }

        var mod = _catalog.Mods[_selectedModIndex];
        if (mod.Sections.Count == 0)
        {
            SetStatus("Configuration schema status only; no editable settings loaded.", invalid: false);
            SetFooterInteractable(false, false);
            return;
        }

        var conflict = _session.Values.FirstOrDefault(value =>
            string.Equals(value.Setting.PluginGuid, mod.Guid, StringComparison.Ordinal) &&
            value.HasExternalConflict);
        if (conflict is not null)
            SetStatus(
                $"{_session.CountModConflicts(mod)} conflict(s): {conflict.Setting.Key} changed outside this page; choose Keep mine or Take live.",
                invalid: true);
        else
        {
            var invalid = changed is not null && !changed.IsValid
                ? changed
                : _session.Values.FirstOrDefault(value =>
                    string.Equals(value.Setting.PluginGuid, mod.Guid, StringComparison.Ordinal) &&
                    !value.IsValid);
            var staged = _session.CountModDirty(mod);
            SetStatus(
                invalid is not null
                    ? $"{invalid.Setting.Key}: {invalid.Error}"
                    : staged == 0
                        ? "Settings changes require Apply."
                        : $"{staged} unsaved change(s) in {mod.Name}; press Apply to save.",
                invalid: invalid is not null);
        }

        SetFooterInteractable(
            CanApplySelection(mod, _session.IsModDirty(mod), _session.IsModValid(mod)),
            _session.IsModDirty(mod) || _session.ModHasExternalConflicts(mod));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _applyButton.onClick.RemoveListener(Apply);
        _revertButton.onClick.RemoveListener(Revert);
        _settingList.Dispose();
    }

    internal static bool CanApplySelection(
        ModConfigDescriptor selectedMod,
        bool sessionDirty,
        bool sessionValid) =>
        selectedMod.Sections.Count > 0 && sessionDirty && sessionValid;

    private void RebuildSettings(float? requestedScrollOffset = null)
    {
        var mod = _catalog.Mods[_selectedModIndex];
        if (mod.Sections.Count == 0)
        {
            _settingList.Clear();
            _content.sizeDelta = new Vector2(0f, 1f);
            RestoreScrollOffset(0f, 1f);
            RefreshStatus();
            return;
        }

        var requestedOffset = requestedScrollOffset ?? Math.Max(0f, _content.anchoredPosition.y);
        var section = mod.Sections[_selectedSectionIndex];
        _featureCommands.TryGet(mod.Guid, section.Name, out var command);
        var settings = section.Settings
            .Where(setting => command is null || !IsImmediateCommandSetting(setting))
            .ToArray();
        var contentHeight = _settingList.Render(settings, command);
        RestoreScrollOffset(requestedOffset, contentHeight);
        RefreshStatus();
    }

    private void Apply()
    {
        if (!_visible || _catalog.Mods.Count == 0 || _catalog.Mods[_selectedModIndex].Sections.Count == 0)
        {
            RefreshStatus();
            return;
        }

        var selectedMod = _catalog.Mods[_selectedModIndex];
        if (_session.Apply(selectedMod, out var error))
        {
            RebuildSettings();
            _statusText.text = ConfigurationSavedMessage;
        }
        else
        {
            SetStatus("Apply failed: " + error, invalid: true);
            _log.LogWarning("Mod Config could not apply staged changes: " + error);
        }
    }

    private void Revert()
    {
        if (!_visible || _catalog.Mods.Count == 0 || _catalog.Mods[_selectedModIndex].Sections.Count == 0)
        {
            RefreshStatus();
            return;
        }
        _session.Revert(_catalog.Mods[_selectedModIndex]);
        _statusText.text = "Reverted staged changes.";
        RebuildSettings();
    }

    private void CaptureNavigation()
    {
        if (!_visible || _catalog.Mods.Count == 0) return;
        _navigation[_selectedModIndex] = new ModSettingsNavigationState(
            _selectedSectionIndex,
            Math.Max(0f, _content.anchoredPosition.y));
    }

    private void RestoreScrollOffset(float requestedOffset, float contentHeight)
    {
        var viewportHeight = _scroll.viewport?.rect.height ?? 0f;
        var clampedOffset = ModSettingsLayout.ClampScrollOffset(
            requestedOffset,
            contentHeight,
            viewportHeight);
        _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, clampedOffset);
        _scroll.verticalNormalizedPosition = ModSettingsLayout.CalculateVerticalNormalizedPosition(
            clampedOffset,
            contentHeight,
            viewportHeight);
    }

    private void SetStatus(string text, bool invalid)
    {
        _statusText.color = invalid ? ModConfigPalette.Invalid : _labelTemplate.color;
        _statusText.text = text;
    }

    private void SetFooterInteractable(bool apply, bool revert)
    {
        _applyButton.interactable = apply;
        _revertButton.interactable = revert;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ModSettingsPage));
    }

    internal static bool IsImmediateCommandSetting(ConfigSettingDescriptor setting) =>
        string.Equals(setting.PluginGuid, PluginIds.SuiteGuid, StringComparison.Ordinal) &&
        (setting.SourceSection == "Safety" && setting.Key == "EmergencyDisable" ||
         setting.Key == "Mode" && AutomataFeatureCatalog.All.Any(feature =>
             string.Equals(feature.PageLabel, setting.Section, StringComparison.Ordinal) &&
             string.Equals(feature.ModeConfigurationSection, setting.SourceSection, StringComparison.Ordinal)));
}
