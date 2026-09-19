using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

internal static class ModConfigShellLayout
{
    internal const float OuterLeft = 0.02f;
    internal const float OuterRight = 0.98f;
    internal const float TitleBottom = 0.885f;
    internal const float TitleTop = 0.975f;
    internal const float BodyBottom = 0.115f;
    internal const float BodyTop = 0.875f;
    internal const float FooterBottom = 0.025f;
    internal const float FooterTop = 0.105f;
}

/// <summary>
/// Composes the Mods surface and switches between independent top-level pages.
/// Editable settings and runtime diagnostics own their rendering and navigation
/// state; this shell owns neither page's domain behavior.
/// </summary>
internal sealed class ModConfigPanel : IDisposable
{
    internal const string SavedRuntimeMessage = ModSettingsPage.SavedRuntimeMessage;
    internal const string ConfigurationSavedMessage = ModSettingsPage.ConfigurationSavedMessage;

    private readonly ConfigCatalogSnapshot _catalog;
    private readonly TextMeshProUGUI _labelTemplate;
    private readonly RectTransform _modTabs;
    private readonly RectTransform _settingsViewport;
    private readonly RectTransform _settingsContent;
    private readonly ModConfigRuntimeSources _runtimeSources;
    private readonly RuntimeDiagnosticsPage _runtimePage;
    private readonly ModSettingsPage _settingsPage;
    private readonly TextMeshProUGUI _statusText;
    private readonly TextMeshProUGUI _pageTitle;
    private readonly Button _applyButton;
    private readonly Button _revertButton;
    private readonly NativeFeatureRailVisualPrimitives _nativeRail;
    private readonly List<GameObject> _topTabObjects = new();
    private readonly RuntimeDiagnosticsDirtyLatch _fullDashboardDirty = new();
    private readonly RuntimeDiagnosticsTransitionQueue _runtimeTransitions = new();
    private RuntimeDiagnosticsDashboard _dashboard;
    private ModConfigRefreshDiagnostics? _refreshDiagnostics;
    private int _selectedTopPageIndex;
    private float _measuredRuntimeWidth;
    private bool _disposed;

    private ModConfigPanel(
        GameObject root,
        ConfigCatalogSnapshot catalog,
        TextMeshProUGUI labelTemplate,
        ManualLogSource log,
        RectTransform modTabs,
        RectTransform settingsViewport,
        RectTransform settingsContent,
        ScrollRect settingsScroll,
        GameplayInvalidationBus invalidationBus,
        ModConfigRuntimeSources runtimeSources,
        ModConfigFeatureCommands featureCommands,
        TextMeshProUGUI pageTitle,
        TextMeshProUGUI statusText,
        Button applyButton,
        Button revertButton,
        NativeFeatureRailVisualPrimitives nativeRail)
    {
        Root = root;
        _catalog = catalog;
        _labelTemplate = labelTemplate;
        _modTabs = modTabs;
        _settingsViewport = settingsViewport;
        _settingsContent = settingsContent;
        _runtimeSources = runtimeSources;
        _pageTitle = pageTitle;
        _statusText = statusText;
        _applyButton = applyButton;
        _revertButton = revertButton;
        _nativeRail = nativeRail;
        RuntimeDiagnosticsPage? runtimePage = null;
        ModSettingsPage? settingsPage = null;
        var subscribed = false;
        try
        {
            runtimePage = new RuntimeDiagnosticsPage(
                settingsContent,
                settingsScroll,
                labelTemplate,
                runtimeSources.DiagnosticsBundle,
                runtimeSources.DifferentialVerification,
                runtimeSources.DecisionJournal,
                runtimeSources.ActionOutcomes,
                runtimeSources.PumpTiming
#if SERVICE_CYCLE_PROFILE
                , runtimeSources.PerformanceProfile
#endif
                );
            settingsPage = new ModSettingsPage(
                catalog,
                labelTemplate,
                log,
                settingsContent,
                settingsScroll,
                statusText,
                applyButton,
                revertButton,
                invalidationBus,
                featureCommands);
            _runtimePage = runtimePage;
            _settingsPage = settingsPage;
            _runtimeSources.SchemaStatuses.Transitioned += OnSchemaStatusTransitioned;
            _runtimeSources.FeatureStatuses.Transitioned += OnFeatureStatusTransitioned;
            _runtimeSources.Diagnostics.Transitioned += OnRuntimeDiagnosticsTransitioned;
            subscribed = true;
            _dashboard = RuntimeDiagnosticsProjection.Build(
                _catalog,
                _runtimeSources.SchemaStatuses,
                _runtimeSources.FeatureStatuses,
                _runtimeSources.Diagnostics);
            RebuildTopTabs();
            ShowSelectedPage();
            SetActive(false);
        }
        catch
        {
            if (subscribed)
            {
                _runtimeSources.SchemaStatuses.Transitioned -= OnSchemaStatusTransitioned;
                _runtimeSources.FeatureStatuses.Transitioned -= OnFeatureStatusTransitioned;
                _runtimeSources.Diagnostics.Transitioned -= OnRuntimeDiagnosticsTransitioned;
            }
            try { settingsPage?.Dispose(); } catch { }
            try { runtimePage?.Dispose(); } catch { }
            try { ModConfigUiFactory.ClearObjects(_topTabObjects); } catch { }
            throw;
        }
    }

    public GameObject Root { get; }

    public ModConfigNavigationBookmark CaptureNavigation() =>
        IsRuntimeSelected
            ? ModConfigNavigationBookmark.Runtime
            : _settingsPage.CaptureBookmark();

    public void RestoreNavigation(ModConfigNavigationBookmark bookmark)
    {
        _settingsPage.RestoreBookmark(bookmark);
        _selectedTopPageIndex =
            ModConfigNavigationBookmarkPolicy.ResolveTopPageIndex(_catalog, bookmark);
        RebuildTopTabs();
        ShowSelectedPage();
    }

    public static ModConfigPanel Create(
        Transform parent,
        TextMeshProUGUI labelTemplate,
        ConfigCatalogSnapshot catalog,
        ManualLogSource log,
        GameplayInvalidationBus invalidationBus,
        ModConfigRuntimeSources runtimeSources,
        ModConfigFeatureCommands featureCommands,
        NativeFeatureRailVisualPrimitives nativeRail)
    {
        ModConfigUiFactory.UseNativeVisuals(nativeRail);
        var root = ModConfigUiFactory.CreateRectObject(
            ModConfigUiShell.PanelObjectName,
            parent,
            Vector2.zero,
            Vector2.one,
            ModConfigPalette.Background);
        try
        {
            return CreateContents(
                root,
                labelTemplate,
                catalog,
                log,
                invalidationBus,
                runtimeSources,
                featureCommands,
                nativeRail);
        }
        catch
        {
            try { UnityEngine.Object.Destroy(root); } catch { }
            throw;
        }
    }

    private static ModConfigPanel CreateContents(
        GameObject root,
        TextMeshProUGUI labelTemplate,
        ConfigCatalogSnapshot catalog,
        ManualLogSource log,
        GameplayInvalidationBus invalidationBus,
        ModConfigRuntimeSources runtimeSources,
        ModConfigFeatureCommands featureCommands,
        NativeFeatureRailVisualPrimitives nativeRail)
    {
        var detailLeft = ModConfigNativeRailFactory.DetailLeft;
        var topNavigationRight = ModConfigNativeRailFactory.RailWidth + 0.02f;
        var header = ModConfigUiFactory.CreateRectObject(
            "ModTabs",
            root.transform,
            new Vector2(ModConfigShellLayout.OuterLeft, ModConfigShellLayout.BodyBottom),
            new Vector2(topNavigationRight, ModConfigShellLayout.BodyTop),
            ModConfigPalette.Bar);
        var sections = ModConfigUiFactory.CreateRectObject(
            "SectionTabs",
            root.transform,
            new Vector2(ModConfigShellLayout.OuterLeft, ModConfigShellLayout.TitleBottom),
            new Vector2(ModConfigShellLayout.OuterRight, ModConfigShellLayout.TitleTop),
            ModConfigPalette.Bar);
        var viewport = ModConfigUiFactory.CreateRectObject(
            "SettingsViewport",
            root.transform,
            new Vector2(detailLeft, ModConfigShellLayout.BodyBottom),
            new Vector2(ModConfigShellLayout.OuterRight, ModConfigShellLayout.BodyTop),
            new Color(0.035f, 0.043f, 0.06f, 0.98f));
        viewport.AddComponent<RectMask2D>();
        var scroll = viewport.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 32f;

        var content = new GameObject("SettingsContent", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var contentRect = (RectTransform)content.transform;
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;
        scroll.viewport = (RectTransform)viewport.transform;
        scroll.content = contentRect;

        var footer = ModConfigUiFactory.CreateRectObject(
            "Footer",
            root.transform,
            new Vector2(detailLeft, ModConfigShellLayout.FooterBottom),
            new Vector2(ModConfigShellLayout.OuterRight, ModConfigShellLayout.FooterTop),
            ModConfigPalette.Bar);
        ModConfigNativeRailFactory.SkinPanel(
            root.GetComponent<Image>()!,
            nativeRail.FeatureRailBaseFrame,
            ModConfigPalette.Background);
        ModConfigNativeRailFactory.SkinPanel(
            header.GetComponent<Image>()!,
            nativeRail.FeatureRailBaseFrame,
            Color.white);
        ModConfigNativeRailFactory.SkinPanel(
            sections.GetComponent<Image>()!,
            nativeRail.FeatureRailBaseFrame,
            Color.white);
        ModConfigNativeRailFactory.SkinPanel(
            viewport.GetComponent<Image>()!,
            nativeRail.FeatureRailBaseFrame,
            new Color(0.72f, 0.72f, 0.72f, 1f));
        ModConfigNativeRailFactory.SkinPanel(
            footer.GetComponent<Image>()!,
            nativeRail.FeatureRailBaseFrame,
            Color.white);
        var pageTitle = ModConfigUiFactory.CreateText(
            "PageTitle",
            sections.transform,
            new Vector2(0.025f, 0.08f),
            new Vector2(0.975f, 0.92f),
            labelTemplate,
            "Runtime",
            TextAlignmentOptions.MidlineLeft,
            0.82f);
        var status = ModConfigUiFactory.CreateText(
            "Status",
            footer.transform,
            new Vector2(0.02f, 0.12f),
            new Vector2(0.62f, 0.88f),
            labelTemplate,
            "Ready",
            TextAlignmentOptions.MidlineLeft,
            0.72f);
        var apply = ModConfigUiFactory.CreateButton(
            "Apply",
            footer.transform,
            new Vector2(0.65f, 0.12f),
            new Vector2(0.81f, 0.88f),
            labelTemplate,
            "Apply",
            () => { });
        var revert = ModConfigUiFactory.CreateButton(
            "Revert",
            footer.transform,
            new Vector2(0.82f, 0.12f),
            new Vector2(0.98f, 0.88f),
            labelTemplate,
            "Revert",
            () => { });

        return new ModConfigPanel(
            root,
            catalog,
            labelTemplate,
            log,
            (RectTransform)header.transform,
            (RectTransform)viewport.transform,
            contentRect,
            scroll,
            invalidationBus,
            runtimeSources,
            featureCommands,
            pageTitle,
            status,
            apply,
            revert,
            nativeRail);
    }

    public void SetActive(bool active)
    {
        if (_disposed) return;
        Root.SetActive(active);
    }

    public void RefreshExternalValues() => _settingsPage.RefreshExternalValues();

#if SERVICE_CYCLE_PROFILE
    internal IReadOnlyList<string> CapturePagesForGameMcp() =>
        ModConfigTopNavigation.Build(_catalog, _dashboard.AttentionCount)
            .Select(page => page.Label)
            .ToArray();

    internal int SelectedPageIndexForGameMcp => _selectedTopPageIndex;

    internal bool TrySelectPageForGameMcp(int index, out string reason)
    {
        var pages = ModConfigTopNavigation.Build(_catalog, _dashboard.AttentionCount);
        if (index < 0 || index >= pages.Count)
        {
            reason = "Mods page index " + index + " is outside the current live catalog";
            return false;
        }
        SelectTopPage(index);
        reason = string.Empty;
        return true;
    }
#endif

    public void RefreshResponsiveLayout()
    {
        if (_disposed || !Root.activeInHierarchy) return;
        if (!IsRuntimeSelected)
        {
            _settingsPage.RefreshResponsiveLayout();
            return;
        }

        var width = _settingsContent.rect.width;
        if (!ModSettingsLayout.DescriptionWidthChanged(_measuredRuntimeWidth, width)) return;
        _measuredRuntimeWidth = width;
        _runtimePage.Render(_dashboard, resetScroll: false);
    }

    public void RefreshRuntimeDashboardIfNeeded()
    {
        if (_disposed) return;
        if (IsRuntimeSelected) _runtimePage.RefreshActivity();
        // Consume both latches independently. Short-circuiting here would leave an
        // overflow pending and force the same authoritative rebuild on the next pass.
        var fullDashboardDirty = _fullDashboardDirty.TryConsume();
        var transitionOverflow = _runtimeTransitions.ConsumeOverflow();
        var rebuildAll = fullDashboardDirty || transitionOverflow;
        var changed = rebuildAll || IsRuntimeSelected && _runtimePage.ObservabilityChanged;
        var previousAttentionCount = _dashboard.AttentionCount;
        while (_runtimeTransitions.TryDequeue(out var transition))
        {
            changed = true;
            if (!rebuildAll && !_dashboard.TryApplyChangedRuntime(transition, out _))
                rebuildAll = true;
        }
        if (!changed) return;
        if (rebuildAll)
        {
            _dashboard = RuntimeDiagnosticsProjection.Build(
                _catalog,
                _runtimeSources.SchemaStatuses,
                _runtimeSources.FeatureStatuses,
                _runtimeSources.Diagnostics);
        }
        if (previousAttentionCount != _dashboard.AttentionCount) RebuildTopTabs();
        if (IsRuntimeSelected) _runtimePage.Render(_dashboard, resetScroll: false);
    }

    public void RefreshRefreshDiagnostics(ModConfigRefreshDiagnostics diagnostics)
    {
        _refreshDiagnostics = diagnostics;
        if (_disposed || !IsRuntimeSelected) return;
        _statusText.text = ModConfigRefreshDiagnosticsPresentation.Build(diagnostics);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runtimeSources.SchemaStatuses.Transitioned -= OnSchemaStatusTransitioned;
        _runtimeSources.FeatureStatuses.Transitioned -= OnFeatureStatusTransitioned;
        _runtimeSources.Diagnostics.Transitioned -= OnRuntimeDiagnosticsTransitioned;
        _settingsPage.Dispose();
        _runtimePage.Dispose();
        ModConfigUiFactory.ClearObjects(_topTabObjects);
        UnityEngine.Object.Destroy(Root);
    }

    internal static bool CanApplySelection(
        ModConfigDescriptor selectedMod,
        bool sessionDirty,
        bool sessionValid) =>
        ModSettingsPage.CanApplySelection(selectedMod, sessionDirty, sessionValid);

    internal static float CalculateSettingRowHeight(float preferredDescriptionHeight) =>
        ModSettingsLayout.CalculateSettingRowHeight(preferredDescriptionHeight);

    internal static float ClampScrollOffset(float requestedOffset, float contentHeight, float viewportHeight) =>
        ModSettingsLayout.ClampScrollOffset(requestedOffset, contentHeight, viewportHeight);

    internal static float CalculateVerticalNormalizedPosition(
        float scrollOffset,
        float contentHeight,
        float viewportHeight) =>
        ModSettingsLayout.CalculateVerticalNormalizedPosition(scrollOffset, contentHeight, viewportHeight);

    internal static float CalculateDescriptionWidth(float contentWidth) =>
        ModSettingsLayout.CalculateDescriptionWidth(contentWidth);

    internal static bool DescriptionWidthChanged(float previousWidth, float currentWidth) =>
        ModSettingsLayout.DescriptionWidthChanged(previousWidth, currentWidth);

    private bool IsRuntimeSelected => _selectedTopPageIndex == 0;

    private void RebuildTopTabs()
    {
        ModConfigUiFactory.ClearObjects(_topTabObjects);
        var pages = ModConfigTopNavigation.Build(_catalog, _dashboard.AttentionCount);
        if (ModConfigNativeRailFactory.TryBuild(
            _modTabs,
            pages.Select(page => page.Label).ToArray(),
            _selectedTopPageIndex,
            _topTabObjects,
            _labelTemplate,
            _nativeRail,
            SelectTopPage,
            out var reason))
            return;
        throw new InvalidOperationException("Native Mods rail construction failed: " + reason);
    }

    private void ShowSelectedPage()
    {
        var pages = ModConfigTopNavigation.Build(_catalog, _dashboard.AttentionCount);
        _selectedTopPageIndex = Math.Max(0, Math.Min(_selectedTopPageIndex, pages.Count - 1));
        var page = pages[_selectedTopPageIndex];
        if (page.Kind == ModConfigTopPageKind.Runtime)
        {
            _settingsPage.Hide();
            _applyButton.gameObject.SetActive(false);
            _revertButton.gameObject.SetActive(false);
            ((RectTransform)_statusText.transform).anchorMax = new Vector2(0.98f, 0.88f);
            _settingsViewport.anchorMax = new Vector2(
                _settingsViewport.anchorMax.x,
                ModConfigShellLayout.BodyTop);
            _pageTitle.text = ModConfigTopNavigation.DetailTitle(_catalog, page);
            _runtimePage.Render(_dashboard, resetScroll: false);
            _measuredRuntimeWidth = _settingsContent.rect.width;
            _statusText.color = _labelTemplate.color;
            _statusText.text = _refreshDiagnostics is { } refreshDiagnostics
                ? ModConfigRefreshDiagnosticsPresentation.Build(refreshDiagnostics)
                : "Runtime evidence updates live. Waiting for the first Mods refresh.";
            return;
        }

        _runtimePage.Hide();
        _applyButton.gameObject.SetActive(true);
        _revertButton.gameObject.SetActive(true);
        ((RectTransform)_statusText.transform).anchorMax = new Vector2(0.62f, 0.88f);
        _settingsViewport.anchorMax = new Vector2(
            _settingsViewport.anchorMax.x,
            ModConfigShellLayout.BodyTop);
        var mod = _catalog.Mods[page.PluginIndex];
        _pageTitle.text = ModConfigTopNavigation.DetailTitle(_catalog, page);
        _settingsPage.ShowSection(page.PluginIndex, page.SectionIndex);
    }

    private void SelectTopPage(int index)
    {
        if (index == _selectedTopPageIndex) return;
        _selectedTopPageIndex = index;
        RebuildTopTabs();
        ShowSelectedPage();
    }

    private void OnFeatureStatusTransitioned(FeatureStatusTransition transition)
    {
        if (!_disposed) _fullDashboardDirty.MarkDirty();
    }

    private void OnSchemaStatusTransitioned(ConfigurationSchemaStatusTransition transition)
    {
        if (!_disposed) _fullDashboardDirty.MarkDirty();
    }

    private void OnRuntimeDiagnosticsTransitioned(RuntimeDiagnosticsTransition transition)
    {
        if (!_disposed) _runtimeTransitions.Enqueue(transition);
    }
}
