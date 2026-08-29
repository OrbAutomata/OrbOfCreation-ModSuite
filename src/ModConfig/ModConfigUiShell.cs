using System;
using System.Collections.Generic;
using BepInEx.Logging;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using UnityEngine;

namespace OrbModConfig;

/// <summary>
/// Coordinates panel visibility and bounded refresh work. Native game navigation
/// is isolated behind <see cref="ModConfigNativeNavigationHost"/>.
/// </summary>
internal sealed class ModConfigUiShell : IDisposable
{
    private const float ExternalRefreshIntervalSeconds = 0.1f;
    internal const string PanelObjectName = "OrbModConfig.Panel";

    private readonly ManualLogSource _log;
    private readonly ModConfigNativeNavigationHost _navigation;
    private readonly ModConfigPanel _panel;
    private readonly Action _maintenanceRequested;
    private bool _disposed;
    private bool _open;
    private bool _repairRequired;
    private readonly ModConfigRefreshScheduler _refresh =
        new(ExternalRefreshIntervalSeconds);

    private ModConfigUiShell(
        ManualLogSource log,
        ModConfigNativeNavigationHost navigation,
        ModConfigPanel panel,
        Action maintenanceRequested)
    {
        _log = log;
        _navigation = navigation;
        _panel = panel;
        _maintenanceRequested = maintenanceRequested;
    }

    public bool IsAlive => ModConfigNativeNavigationPolicy.HostsAlive(
        !_disposed && !_repairRequired,
        _navigation.IsAlive,
        _navigation.HostsPanel(_panel.Root),
        parentsAlive: true);

    public static bool TryCreate(
        ManualLogSource log,
        ConfigCatalogSnapshot catalog,
        GameplayInvalidationBus invalidationBus,
        ModConfigRuntimeSources runtimeSources,
        ModConfigFeatureCommands featureCommands,
        ModConfigNavigationBookmark navigationBookmark,
        Action maintenanceRequested,
        Action navigationMaintenanceRequested,
        out ModConfigUiShell? shell,
        out string reason)
    {
        if (log is null) throw new ArgumentNullException(nameof(log));
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (invalidationBus is null) throw new ArgumentNullException(nameof(invalidationBus));
        if (runtimeSources is null) throw new ArgumentNullException(nameof(runtimeSources));
        if (featureCommands is null) throw new ArgumentNullException(nameof(featureCommands));
        if (maintenanceRequested is null) throw new ArgumentNullException(nameof(maintenanceRequested));
        if (navigationMaintenanceRequested is null)
            throw new ArgumentNullException(nameof(navigationMaintenanceRequested));

        shell = null;
        if (!ModConfigNativeRailFactory.TryCapture(out var nativeRail, out var captureReason) ||
            nativeRail is null)
        {
            reason = "Mods rail native capture failed: " + captureReason;
            return false;
        }
        if (!ModConfigNativeNavigationInstaller.TryInstall(
                PanelObjectName,
                out var navigation,
                out reason) || navigation is null)
            return false;

        ModConfigPanel? panel = null;
        try
        {
            panel = ModConfigPanel.Create(
                navigation.PanelParent,
                navigation.LabelTemplate,
                catalog,
                log,
                invalidationBus,
                runtimeSources,
                featureCommands,
                nativeRail);
            panel.RestoreNavigation(navigationBookmark);
            shell = new ModConfigUiShell(log, navigation, panel, maintenanceRequested);
            navigation.Connect(shell.SelectMods, shell.CloseFromNativeTab, navigationMaintenanceRequested);
            log.LogInfo(
                $"Mod Config UI shell installed. ButtonPath={NativeObjectPath.Build(navigation.ButtonObject)}; " +
                $"PanelPath={NativeObjectPath.Build(panel.Root)}; " +
                $"Mods={catalog.Mods.Count}; Settings={catalog.SettingCount}.");
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            try { panel?.Dispose(); } catch { }
            navigation.Dispose();
            shell = null;
            reason = ex.GetBaseException().Message;
            return false;
        }
    }

    public void SelectMods()
    {
        var requestedOpen = ModConfigTabSelectionPolicy.RequestedOpenState(_open);
        TrySetOpen(requestedOpen, restorePreviousNativeView: !requestedOpen);
    }

    public ModConfigNavigationBookmark CaptureNavigation() => _panel.CaptureNavigation();

    public void RefreshNavigation() => _navigation.RefreshNavigation();

#if SERVICE_CYCLE_PROFILE
    internal bool IsOpenForGameMcp => _open && IsAlive;

    internal IReadOnlyList<GameMcpNativeTab> CaptureNativeTabsForGameMcp() =>
        _navigation.CaptureNativeTabsForGameMcp();

    internal IReadOnlyList<string> CapturePagesForGameMcp() =>
        _panel.CapturePagesForGameMcp();

    internal int SelectedPageIndexForGameMcp => _panel.SelectedPageIndexForGameMcp;

    internal bool IsNativeTabForGameMcp(Component component) =>
        _navigation.IsNativeTabForGameMcp(component);

    internal int NativeTabCountForGameMcp() =>
        _navigation.NativeTabCountForGameMcp();

    internal bool TrySelectNativeTabForGameMcp(int index, out string reason)
        => _navigation.TrySelectNativeTabForGameMcp(index, out reason);

    internal bool TrySelectPageForGameMcp(int index, out string reason) =>
        _panel.TrySelectPageForGameMcp(index, out reason);
#endif

    public bool ScheduleRefresh(float unscaledDeltaTime)
    {
        if (_disposed || !IsAlive || !_open)
        {
            _refresh.Close();
            return false;
        }

        var pending = _refresh.Schedule(unscaledDeltaTime);
        RefreshDiagnosticsIfDue();
        return pending;
    }

    public bool HasPendingRefresh => _refresh.IsPending;

    public void RunPendingRefresh()
    {
        if (!_refresh.IsPending || _disposed || !IsAlive || !_open) return;
        _panel.RefreshExternalValues();
        _panel.RefreshResponsiveLayout();
        _panel.RefreshRuntimeDashboardIfNeeded();
        _refresh.Complete();
        RefreshDiagnosticsIfDue();
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_open) TrySetOpen(false, restorePreviousNativeView: true);
        _disposed = true;
        try { _panel.Dispose(); } catch { }
        _navigation.Dispose();
    }

    private void CloseFromNativeTab() =>
        TrySetOpen(false, restorePreviousNativeView: false);

    private void SetOpen(bool open, bool restorePreviousNativeView)
    {
        if (_disposed || _open == open) return;
        if (open) _navigation.ActivateMods();
        _open = open;
        _panel.SetActive(open);
        if (open)
        {
            // Root activation is intentionally cheap. Refreshing settings,
            // consuming runtime transitions, and rendering occur only after the
            // shared coordinator admits the pending maintenance work.
            _refresh.Open();
            RefreshDiagnosticsIfDue();
            _maintenanceRequested();
            return;
        }

        _refresh.Close();
        _navigation.DeactivateMods(restorePreviousNativeView);
    }

    private void TrySetOpen(bool open, bool restorePreviousNativeView)
    {
        try { SetOpen(open, restorePreviousNativeView); }
        catch (Exception ex)
        {
            try { _panel.SetActive(false); } catch { }
            _navigation.RecoverAfterPanelFailure(open || restorePreviousNativeView);
            _open = false;
            _refresh.Close();
            _repairRequired = true;
            _log.LogWarning(
                $"Mod Config UI open/close failed; scheduling shell repair: {ex.GetBaseException().Message}");
        }
    }

    private void RefreshDiagnosticsIfDue()
    {
        if (_refresh.ConsumeDiagnosticsDue())
            _panel.RefreshRefreshDiagnostics(_refresh.Diagnostics);
    }
}
