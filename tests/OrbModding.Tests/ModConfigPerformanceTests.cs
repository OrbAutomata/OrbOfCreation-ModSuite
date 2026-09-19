using OrbModConfig;
using OrbModding.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace OrbModding.Tests;

public sealed class ModConfigPerformanceTests
{
    [Fact]
    public void ModsTabSelectionAlwaysRequestsTheOpenState()
    {
        Assert.True(ModConfigTabSelectionPolicy.RequestedOpenState(currentlyOpen: false));
        Assert.True(ModConfigTabSelectionPolicy.RequestedOpenState(currentlyOpen: true));
    }

    [Fact]
    public void ZeroCandidatesDuringStartupRetryFastAndAdmitBothWhenReady()
    {
        var missing = NativeTopBarReadinessPolicy.RequiredItemNames
            .Select(name => new NativeTopBarCandidateFact(name, MatchCount: 0, HasIcon: false))
            .ToArray();
        var ready = NativeTopBarReadinessPolicy.RequiredItemNames
            .Select(name => new NativeTopBarCandidateFact(name, MatchCount: 1, HasIcon: true))
            .ToArray();
        var gate = new UiStartupReadinessGate();
        gate.Begin();

        Assert.True(gate.ShouldInspect(0f));
        Assert.Equal(
            NativeUiStartupReadinessKind.NotYetPresent,
            NativeTopBarReadinessPolicy.Classify(missing));
        var waiting = gate.Observe(NativeUiStartupReadinessKind.NotYetPresent);
        Assert.False(waiting.QuickControls);
        Assert.False(waiting.ModsRail);
        Assert.False(gate.ShouldInspect(0.05f));
        Assert.True(gate.ShouldInspect(0.05f));

        Assert.Equal(
            NativeUiStartupReadinessKind.Ready,
            NativeTopBarReadinessPolicy.Classify(ready));
        var admitted = gate.Observe(NativeUiStartupReadinessKind.Ready);
        Assert.True(admitted.QuickControls);
        Assert.True(admitted.ModsRail);
    }

    [Fact]
    public void GenuineCaptureMismatchUsesSlowTerminalPathImmediately()
    {
        var candidates = NativeTopBarReadinessPolicy.RequiredItemNames
            .Select(name => new NativeTopBarCandidateFact(name, MatchCount: 1, HasIcon: true))
            .ToArray();
        candidates[2] = candidates[2] with { MatchCount = 2 };
        var gate = new UiStartupReadinessGate();
        gate.Begin();

        Assert.Equal(
            NativeUiStartupReadinessKind.Mismatch,
            NativeTopBarReadinessPolicy.Classify(candidates));
        var admission = gate.Observe(NativeUiStartupReadinessKind.Mismatch);
        Assert.True(admission.QuickControls);
        Assert.True(admission.ModsRail);

        var quickControls = new UiInstallationRetryState();
        var modsRail = new UiInstallationRetryState();
        for (var attempt = 1; attempt < UiInstallationRetryState.TerminalAttempt; attempt++)
        {
            Assert.False(quickControls.ObserveFailure().IsTerminal);
            Assert.False(modsRail.ObserveFailure().IsTerminal);
        }
        Assert.True(quickControls.ObserveFailure().IsTerminal);
        Assert.True(modsRail.ObserveFailure().IsTerminal);
    }

    [Fact]
    public void SharedReadinessGateNeverAdmitsOnlyOneSurface()
    {
        var gate = new UiStartupReadinessGate();
        gate.Begin();

        var waiting = gate.Observe(NativeUiStartupReadinessKind.NotYetPresent);
        Assert.Equal(waiting.QuickControls, waiting.ModsRail);

        gate.Reset();
        gate.Begin();
        var ready = gate.Observe(NativeUiStartupReadinessKind.Ready);
        Assert.Equal(ready.QuickControls, ready.ModsRail);

        gate.Reset();
        gate.Begin();
        var mismatch = gate.Observe(NativeUiStartupReadinessKind.Mismatch);
        Assert.Equal(mismatch.QuickControls, mismatch.ModsRail);
    }

    [Fact]
    public void StartupFastLaneWaitsUntilNamedBoundThenEntersSlowFailurePath()
    {
        var gate = new UiStartupReadinessGate();
        gate.Begin();
        Assert.True(gate.ShouldInspect(
            UiStartupReadinessGate.StartupWindowSeconds -
            UiStartupReadinessGate.FastRetryIntervalSeconds));

        var waiting = gate.Observe(NativeUiStartupReadinessKind.NotYetPresent);
        Assert.False(waiting.QuickControls);
        Assert.False(waiting.ModsRail);
        Assert.True(gate.ShouldInspect(UiStartupReadinessGate.FastRetryIntervalSeconds));

        var admission = gate.Observe(NativeUiStartupReadinessKind.NotYetPresent);

        Assert.True(admission.QuickControls);
        Assert.True(admission.ModsRail);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void UiMaintenanceRunsAtMostOncePerFrameAndRetainsDueRepair()
    {
        long frame = 12;
        using var ui = new ModConfigFrameWork(() => frame);
        var listeners = new HashSet<string>(StringComparer.Ordinal);
        var runs = 0;

        Assert.True(ui.TryRun(true, pending: true, () =>
        {
            runs++;
            listeners.Add("magic");
            listeners.Add("time");
        }));
        Assert.False(ui.TryRun(true, pending: true, () => runs++));
        Assert.True(ui.IsPending);
        Assert.Equal(1, runs);
        Assert.Equal(2, listeners.Count);
        frame++;
        Assert.True(ui.TryRun(true, pending: true, () =>
        {
            runs++;
            listeners.Add("magic");
            listeners.Add("time");
        }));
        ui.SetState(true, pending: false);
        Assert.Equal(2, runs);
        Assert.Equal(2, listeners.Count);

        ui.SetState(false, pending: true);
        Assert.False(ui.IsPending);
        Assert.False(ui.TryRun(false, pending: true, () => runs++));
        Assert.Equal(2, runs);
    }

    [Fact]
    public void CatalogDiscoveryAndLoggingRemainIdempotentAcrossMaintenanceFrames()
    {
        long frame = 31;
        using var ui = new ModConfigFrameWork(() => frame);
        ConfigCatalogSnapshot? catalog = null;
        var generation = default(ConfigCatalogGeneration);
        var currentGeneration = ConfigCatalogGeneration.Capture(Array.Empty<ConfigPluginSource>());
        var discoveryCalls = 0;
        var logCalls = 0;
        void DiscoverAndLog()
        {
            ModConfigCatalogSession.GetOrDiscover(
                ref catalog,
                ref generation,
                currentGeneration,
                () =>
                {
                    discoveryCalls++;
                    return new ConfigCatalogSnapshot(Array.Empty<ModConfigDescriptor>());
                },
                _ => logCalls++);
        }

        Assert.True(ui.TryRun(true, pending: true, DiscoverAndLog));
        Assert.NotNull(catalog);
        Assert.Equal(1, discoveryCalls);
        Assert.Equal(1, logCalls);

        frame++;
        Assert.True(ui.TryRun(true, pending: true, DiscoverAndLog));
        Assert.Equal(1, discoveryCalls);
        Assert.Equal(1, logCalls);
    }

    [Fact]
    public void CatalogGenerationRebuildsOnceForAddedAndRemovedDefinitionsButNotValues()
    {
        var config = new BepInEx.Configuration.ConfigFile();
        var first = config.Bind("General", "First", 1, "First");
        var sources = new List<ConfigPluginSource>
        {
            new ConfigPluginSource("plugin", "Plugin", "1.0.0", config),
        };
        ConfigCatalogSnapshot? catalog = null;
        var generation = default(ConfigCatalogGeneration);
        var discoveries = 0;

        ConfigCatalogSnapshot Discover()
        {
            discoveries++;
            return ConfigCatalog.Build(sources);
        }

        var initial = ConfigCatalogGeneration.Capture(sources);
        ModConfigCatalogSession.GetOrDiscover(
            ref catalog, ref generation, initial, Discover, _ => { });
        first.Value = 2;
        var valueOnly = ConfigCatalogGeneration.Capture(sources);
        ModConfigCatalogSession.GetOrDiscover(
            ref catalog, ref generation, valueOnly, Discover, _ => { });
        Assert.Equal(initial, valueOnly);
        Assert.Equal(1, discoveries);

        var second = config.Bind("General", "Second", true, "Second");
        var added = ConfigCatalogGeneration.Capture(sources);
        ModConfigCatalogSession.GetOrDiscover(
            ref catalog, ref generation, added, Discover, _ => { });
        ModConfigCatalogSession.GetOrDiscover(
            ref catalog, ref generation, added, Discover, _ => { });
        Assert.Equal(2, discoveries);
        Assert.Equal(2, catalog!.SettingCount);

        Assert.True(config.Remove(second.Definition));
        var removed = ConfigCatalogGeneration.Capture(sources);
        ModConfigCatalogSession.GetOrDiscover(
            ref catalog, ref generation, removed, Discover, _ => { });
        ModConfigCatalogSession.GetOrDiscover(
            ref catalog, ref generation, removed, Discover, _ => { });
        Assert.Equal(3, discoveries);
        Assert.Equal(1, catalog!.SettingCount);

        var lateConfig = new BepInEx.Configuration.ConfigFile();
        lateConfig.Bind("General", "Enabled", true, "Enabled");
        sources.Add(new ConfigPluginSource("late.plugin", "Late Plugin", "1.0.0", lateConfig));
        var pluginAdded = ConfigCatalogGeneration.Capture(sources);
        ModConfigCatalogSession.GetOrDiscover(
            ref catalog, ref generation, pluginAdded, Discover, _ => { });
        ModConfigCatalogSession.GetOrDiscover(
            ref catalog, ref generation, pluginAdded, Discover, _ => { });
        Assert.Equal(4, discoveries);
        Assert.Equal(2, catalog!.Mods.Count);

        sources.RemoveAt(1);
        var pluginRemoved = ConfigCatalogGeneration.Capture(sources);
        ModConfigCatalogSession.GetOrDiscover(
            ref catalog, ref generation, pluginRemoved, Discover, _ => { });
        ModConfigCatalogSession.GetOrDiscover(
            ref catalog, ref generation, pluginRemoved, Discover, _ => { });
        Assert.Equal(5, discoveries);
        Assert.Single(catalog!.Mods);
    }

    [Fact]
    public void CatalogNavigationBookmarkUsesStablePluginAndSectionIdentity()
    {
        var firstConfig = new BepInEx.Configuration.ConfigFile();
        firstConfig.Bind("Section A", "Value", 1, "Value");
        var selectedConfig = new BepInEx.Configuration.ConfigFile();
        selectedConfig.Bind("Section B", "Value", 2, "Value");
        var original = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("z.plugin", "Z Plugin", "1", selectedConfig),
            new ConfigPluginSource("a.plugin", "A Plugin", "1", firstConfig),
        });
        var bookmark = new ModConfigNavigationBookmark("z.plugin", "Section B", 42f);
        var rebuilt = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("z.plugin", "Z Plugin", "1", selectedConfig),
        });

        var pageIndex = ModConfigNavigationBookmarkPolicy.ResolveTopPageIndex(rebuilt, bookmark);
        var selected = rebuilt.Mods[ModConfigTopNavigation.Build(rebuilt, 0)[pageIndex].PluginIndex];

        Assert.Equal("z.plugin", original.Mods[
            ModConfigTopNavigation.Build(original, 0)[
                ModConfigNavigationBookmarkPolicy.ResolveTopPageIndex(original, bookmark)].PluginIndex].Guid);
        Assert.Equal("z.plugin", selected.Guid);
        Assert.Equal(
            "Section B",
            selected.Sections[
                ModConfigNavigationBookmarkPolicy.ResolveSectionIndex(selected, bookmark)].Name);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void NavigationIntegrityCadenceDoesNotRunEveryFrame()
    {
        var remaining = 5.0f;

        for (var frame = 0; frame < 299; frame++)
            Assert.False(global::OrbModding.Plugin.AdvanceCadence(ref remaining, 1.0f / 60.0f, 5.0f));

        Assert.True(global::OrbModding.Plugin.AdvanceCadence(ref remaining, 1.0f / 60.0f, 5.0f));
        Assert.InRange(remaining, 4.999f, 5.001f);
        Assert.False(global::OrbModding.Plugin.AdvanceCadence(ref remaining, -10.0f, 5.0f));
    }

    [Fact]
    public void NavigationIntegrityCadenceRecoversAfterLargeFrameGap()
    {
        var remaining = 1.0f;

        Assert.True(global::OrbModding.Plugin.AdvanceCadence(ref remaining, 3.0f, 5.0f));
        Assert.Equal(5.0f, remaining);
    }

    [Fact]
    public void DeadUiReferencesArePrunedInPlaceAndDetachExactlyOnce()
    {
        var alive = new FakeReference(true, "alive");
        var deadA = new FakeReference(false, "dead-a");
        var deadB = new FakeReference(false, "dead-b");
        var references = new List<FakeReference> { deadA, alive, deadB };
        var detached = new List<string>();

        var removed = ModConfigNativeNavigationPolicy.PruneDead(
            references,
            item => item.Alive,
            item => detached.Add(item.Name));

        Assert.Equal(2, removed);
        Assert.Same(alive, Assert.Single(references));
        Assert.Equal(new[] { "dead-b", "dead-a" }, detached);
        Assert.Equal(0, ModConfigNativeNavigationPolicy.PruneDead(
            references,
            item => item.Alive,
            item => detached.Add(item.Name)));
        Assert.Equal(2, detached.Count);
    }

    [Fact]
    public void PanelLossInvalidatesShellEvenWhenButtonSurvives()
    {
        Assert.False(ModConfigNativeNavigationPolicy.HostsAlive(
            hostHealthy: true, buttonAlive: true, panelAlive: false, parentsAlive: true));
        Assert.True(ModConfigNativeNavigationPolicy.HostsAlive(
            hostHealthy: true, buttonAlive: true, panelAlive: true, parentsAlive: true));
    }

    [Fact]
    public void OpenFailureRestoresPreviousNativeViewAndRequestsRepair()
    {
        var recovery = ModConfigNativeNavigationPolicy.OpenFailureRecovery(
            restoreRequested: true, previousAlive: true, fallbackAlive: true, anyNativeActive: false);

        Assert.True(recovery.RestorePrevious);
        Assert.False(recovery.RestoreFallback);
        Assert.True(recovery.RepairRequired);
        Assert.False(ModConfigNativeNavigationPolicy.OpenFailureRecovery(
            restoreRequested: true, previousAlive: true, fallbackAlive: true, anyNativeActive: true).RestorePrevious);
        Assert.False(ModConfigNativeNavigationPolicy.HostsAlive(
            hostHealthy: !recovery.RepairRequired, buttonAlive: true, panelAlive: true, parentsAlive: true));
    }

    [Fact]
    public void OpenPanelHostLossRestoresFallbackAndDetachesOldListenersExactlyOnce()
    {
        var recovery = ModConfigNativeNavigationPolicy.OpenFailureRecovery(
            restoreRequested: true, previousAlive: false, fallbackAlive: true, anyNativeActive: false);
        var listeners = new List<string> { "magic", "time", "mods" };
        var detached = new List<string>();

        Assert.False(recovery.RestorePrevious);
        Assert.True(recovery.RestoreFallback);
        Assert.True(recovery.RepairRequired);
        Assert.Equal(3, ModConfigNativeNavigationPolicy.DetachAll(listeners, detached.Add));
        Assert.Empty(listeners);
        Assert.Equal(new[] { "magic", "time", "mods" }, detached);
        Assert.Equal(0, ModConfigNativeNavigationPolicy.DetachAll(listeners, detached.Add));
        Assert.Equal(3, detached.Count);

        // A repaired shell owns a fresh listener ledger; none of the disposed
        // bindings can leak into it.
        var reinstalledListeners = new List<string> { "magic", "time" };
        Assert.Equal(2, reinstalledListeners.Count);
        Assert.Empty(listeners);
    }

    [Fact]
    public void NativeViewContractIsValidatedAndCachedOncePerType()
    {
        Assert.False(NativeViewAdapter.IsViewTypeCached(typeof(FakeNativeView)));
        Assert.True(NativeViewAdapter.TryValidateViewType(typeof(FakeNativeView), out var reason), reason);
        Assert.True(NativeViewAdapter.IsViewTypeCached(typeof(FakeNativeView)));
        Assert.True(NativeViewAdapter.TryValidateViewType(typeof(FakeNativeView), out reason), reason);
    }

    [Fact]
    public void NativeViewContractRejectsIncorrectMethodShapes()
    {
        Assert.False(NativeViewAdapter.TryValidateViewType(typeof(InvalidNativeView), out var reason));
        Assert.Contains("bool IsActive()", reason);
    }

    [Theory]
    [InlineData("Canvas/ContentArea/MainContentContainer/SubviewRadio/ViewNoIconRadioButtonSub(Clone)", true)]
    [InlineData("Canvas/ContentArea/MainContentContainer/SubviewRadio/ViewNoIconRadioButtonSub(Clone)/Nested", false)]
    [InlineData("Canvas/ContentArea/MainContentContainer/TopBar/ViewRadio", false)]
    [InlineData("PreviewCanvas/ContentArea/MainContentContainer/SubviewRadio/ViewNoIconRadioButtonSub(Clone)", false)]
    [InlineData("Canvas/ContentArea/MainContentContainer/SubviewRadio", false)]
    public void NativeFeatureRailSamplingRequiresTheAuditedStructuralPath(string path, bool expected)
    {
        Assert.Equal(expected, NativeViewAdapter.IsFeatureRailPath(path));
    }

    [Theory]
    [InlineData("Canvas/ContentArea/MainContentContainer/TopBar/ViewRadio/ViewRadioButtonLong(Clone)", true)]
    [InlineData("Canvas/ContentArea/MainContentContainer/TopBar/ViewRadio/ViewRadioButtonLong(Clone)/Icon", false)]
    [InlineData("Canvas/ContentArea/MainContentContainer/SubviewRadio/ViewRadioButtonLong(Clone)", false)]
    public void TopBarIconSamplingRequiresTheAuditedDirectChildPath(string path, bool expected)
    {
        Assert.Equal(expected, NativeViewAdapter.IsAuditedTopBarPath(path));
    }

    [Fact]
    public void FeatureRailCaptureAcceptsInactiveNativeCandidatesWhileModsIsOpen()
    {
        var canvas = new GameObject("Canvas");
        var contentArea = Child(canvas, "ContentArea");
        var main = Child(contentArea, "MainContentContainer");
        var group = Child(main, "SubviewRadio");
        var buttonObject = Child(group, "ViewNoIconRadioButtonSub(Clone)");
        var frame = buttonObject.AddComponent<Image>();
        var candidate = buttonObject.AddComponent<FakeRailButton>();
        candidate.buttonImage = frame;
        candidate.viewImage = null;
        candidate.baseImage = new Sprite();
        candidate.activeImage = new Sprite();
        main.SetActive(false);
        buttonObject.activeInHierarchy = false;

        Assert.False(candidate.gameObject.activeInHierarchy);
        Assert.True(
            NativeViewAdapter.TryReadFeatureRailFrames(
                new Component[] { candidate },
                out var prototype,
                out var baseFrame,
                out var activeFrame,
                out var reason),
            reason);
        Assert.Same(candidate, prototype);
        Assert.Same(candidate.baseImage, baseFrame);
        Assert.Same(candidate.activeImage, activeFrame);
    }

    [Fact]
    public void TopBarIconCaptureAcceptsInactiveSceneCandidateWithRealFieldPopulation()
    {
        var canvas = new GameObject("Canvas");
        var contentArea = Child(canvas, "ContentArea");
        var main = Child(contentArea, "MainContentContainer");
        var topBar = Child(main, "TopBar");
        var group = Child(topBar, "ViewRadio");
        var buttonObject = Child(group, "ViewRadioButtonLong(Clone)");
        var candidate = buttonObject.AddComponent<FakeRailButton>();
        candidate.item = new GameObject("ScreenTime");
        candidate.viewImage = buttonObject.AddComponent<Image>();
        candidate.viewImage.sprite = new Sprite();
        main.SetActive(false);
        buttonObject.activeInHierarchy = false;

        Assert.False(candidate.gameObject.activeInHierarchy);
        Assert.True(
            NativeViewAdapter.TryReadNamedTopBarIcon(
                new Component[] { candidate },
                "ScreenTime",
                out var icon,
                out var reason),
            reason);
        Assert.Same(candidate.viewImage.sprite, icon);
    }

    [Fact]
    public void RailFailureCensusReportsInactiveLifecycleItemAndEveryVisualField()
    {
        var canvas = new GameObject("Canvas");
        var buttonObject = Child(canvas, "ViewNoIconRadioButtonSub(Clone)");
        var candidate = buttonObject.AddComponent<FakeRailButton>();
        candidate.item = new GameObject("ScholarConcepts");
        candidate.buttonImage = buttonObject.AddComponent<Image>();
        candidate.viewImage = null;
        candidate.baseImage = new Sprite();
        candidate.activeImage = new Sprite();
        buttonObject.SetActive(false);

        var census = NativeViewAdapter.BuildFeatureRailCandidateCensus(
            new Component[] { candidate });

        Assert.Contains("path='Canvas/ViewNoIconRadioButtonSub(Clone)'", census);
        Assert.Contains("activeSelf=False", census);
        Assert.Contains("activeInHierarchy=False", census);
        Assert.Contains("scene=Main(loaded=True)", census);
        Assert.Contains("pathMatch=False", census);
        Assert.Contains("item='ScholarConcepts' (GameObject)", census);
        Assert.Contains("buttonImage=present", census);
        Assert.Contains("buttonOwned=True", census);
        Assert.Contains("viewImage=null", census);
        Assert.Contains("viewIcon=null", census);
        Assert.Contains("baseImage=present", census);
        Assert.Contains("activeImage=present", census);
    }

    [Fact]
    public void OpeningUiSchedulesRefreshForCoordinatorInsteadOfRunningItInline()
    {
        var refresh = new ModConfigRefreshScheduler(0.1f);

        refresh.Open();

        Assert.True(refresh.IsPending);
        refresh.Complete();
        Assert.False(refresh.IsPending);
        Assert.False(refresh.Schedule(0.05f));
        Assert.True(refresh.Schedule(0.06f));
        refresh.Close();
        Assert.False(refresh.IsPending);
    }

    [Fact]
    public void RefreshDiagnosticsExposePendingAndLastCompletedAge()
    {
        var refresh = new ModConfigRefreshScheduler(0.1f);

        refresh.Open();
        Assert.True(refresh.ConsumeDiagnosticsDue());
        refresh.Schedule(0.35f);
        Assert.Contains(
            "pending for 0.4s; last completed not yet",
            ModConfigRefreshDiagnosticsPresentation.Build(refresh.Diagnostics));

        refresh.Complete();
        Assert.Contains(
            "idle; last completed 0.0s ago",
            ModConfigRefreshDiagnosticsPresentation.Build(refresh.Diagnostics));
        refresh.Schedule(0.25f);
        Assert.Contains(
            "pending for 0.0s; last completed 0.3s ago",
            ModConfigRefreshDiagnosticsPresentation.Build(refresh.Diagnostics));
    }

    private sealed record FakeReference(bool Alive, string Name);

    private sealed class FakeNativeView
    {
        public bool IsActive() => true;
        public void SetActive(bool active) { }
        public bool IsAvailable() => true;
    }

    private sealed class InvalidNativeView
    {
        public int IsActive() => 1;
        public void SetActive(bool active) { }
        public bool IsAvailable() => true;
    }

    private sealed class FakeRailButton : Behaviour
    {
        public object item = new();
        public Sprite? baseImage;
        public Sprite? activeImage;
        public Image? buttonImage;
        public Image? viewImage;
    }

    private static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        return child;
    }
}
