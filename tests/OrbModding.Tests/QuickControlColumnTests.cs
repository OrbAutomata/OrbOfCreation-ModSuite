using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using OrbAutomata;
using OrbMentor;
using OrbModConfig;
using OrbModding.Common;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace OrbModding.Tests;

public sealed class QuickControlColumnTests
{
    [Fact]
    public void GameObjectTransformMatchesTheInstalledUnityConstructorContract()
    {
        var ordinary = new GameObject("Ordinary");
        var ui = new GameObject("Ui", typeof(RectTransform));

        Assert.IsType<Transform>(ordinary.transform);
        Assert.IsType<RectTransform>(ui.transform);
    }

    [Fact]
    public void RectTransformMismatchNamesTheMemberAndExpectedVersusActualTypes()
    {
        var ordinary = new GameObject("Ordinary");

        Assert.False(
            QuickControlColumn.TryRequireRectTransform(
                ordinary,
                "UnityEngine.GameObject('Ordinary').transform",
                out var rect,
                out var reason));

        Assert.Null(rect);
        Assert.Contains("UnityEngine.GameObject('Ordinary').transform", reason);
        Assert.Contains("expected UnityEngine.RectTransform", reason);
        Assert.Contains("actual UnityEngine.Transform", reason);
    }

    [Theory]
    [InlineData("Auto Buy")]
    [InlineData("Auto Cast")]
    [InlineData("Auto Harvest")]
    [InlineData("Mentor")]
    public void TooltipableFeatureIconsResolveThroughExactNativeReturnTypes(string pageLabel)
    {
        Assert.True(
            NativeFeatureIconResolver.TryResolve(
                pageLabel,
                capturedRail: null,
                out var icon,
                out var reason),
            reason);
        Assert.NotNull(icon);
    }

    [Fact]
    public void DrawerBuildsEveryRegisteredAutomationFeatureWithoutAnEquippedSpell()
    {
        using var context = new Context();
        var previousSpellManager = global::SpellManager.instance;
        global::SpellManager.instance = new global::SpellManager();
        try
        {
            Assert.Empty(global::SpellManager.instance.activeSpells.value);
            Assert.True(
                QuickControlColumn.TryCreate(
                    context.Registry,
                    context.EmergencyStop,
                    context.Native,
                    allowFeatureControls: true,
                    out var column,
                    out var reason,
                    context.ResolveIcon),
                reason);
            using (var liveColumn = Assert.IsType<QuickControlColumn>(column))
            {
                Assert.Equal(
                    context.Registry.Features
                        .Select(feature => feature.FeatureId)
                        .Append(QuickControlColumn.EmergencyStopId)
                        .Append(QuickControlColumn.DrawerControlId)
                        .OrderBy(value => value, StringComparer.Ordinal),
                    liveColumn.ControlIds.OrderBy(value => value, StringComparer.Ordinal));
                Assert.Equal(
                    context.Registry.Features.Select(feature => feature.FeatureId),
                    liveColumn.DrawerControlIds);
                Assert.Empty(liveColumn.Failures);
                var columnRoot = Assert.IsType<RectTransform>(
                    context.Native.Anchor.GetChild(0));
                Assert.Equal(QuickControlColumn.ObjectName, columnRoot.name);
                AssertAllRectTransforms(columnRoot);
            }
        }
        finally
        {
            global::SpellManager.instance = previousSpellManager;
        }
    }

    [Fact]
    public void ClosedFootprintIsNativeSizedCompoundControlAndDisclosureTogglesPanelGrid()
    {
        using var context = new Context();
        Assert.True(
            QuickControlColumn.TryCreate(
                context.Registry,
                context.EmergencyStop,
                context.Native,
                allowFeatureControls: true,
                out var column,
                out var reason,
                context.ResolveIcon),
            reason);
        using (var liveColumn = Assert.IsType<QuickControlColumn>(column))
        {
            var root = Assert.IsType<RectTransform>(context.Native.Anchor.GetChild(0));
            var drawer = Assert.IsType<RectTransform>(FindDescendant(
                root,
                QuickControlColumn.DrawerObjectName));
            Assert.True(liveColumn.TryGetButton(
                QuickControlColumn.EmergencyStopId,
                out var emergencyStop));
            Assert.True(liveColumn.TryGetButton(
                QuickControlColumn.DrawerControlId,
                out var disclosure));

            var emergencyRect = Assert.IsType<RectTransform>(emergencyStop.transform);
            var disclosureRect = Assert.IsType<RectTransform>(disclosure.transform);
            Assert.NotSame(emergencyStop, disclosure);
            Assert.Equal(0f, emergencyRect.anchoredPosition.x);
            Assert.Equal(0f, emergencyRect.anchoredPosition.y);
            Assert.Equal(context.Native.Geometry.ControlSize.x, emergencyRect.sizeDelta.x);
            Assert.Equal(context.Native.Geometry.ControlSize.y, emergencyRect.sizeDelta.y);
            Assert.Equal(0f, disclosureRect.anchoredPosition.x);
            Assert.Equal(
                -(context.Native.Geometry.ControlSize.y -
                  QuickControlColumn.DisclosureOverlap),
                disclosureRect.anchoredPosition.y);
            Assert.Equal(context.Native.Geometry.ControlSize.x, disclosureRect.sizeDelta.x);
            Assert.Equal(QuickControlColumn.DisclosureHeight, disclosureRect.sizeDelta.y);
            Assert.Equal(
                context.Native.Geometry.ControlSize.x,
                root.sizeDelta.x);
            var compoundHeight = context.Native.Geometry.ControlSize.y +
                                 QuickControlColumn.DisclosureHeight -
                                 QuickControlColumn.DisclosureOverlap;
            Assert.Equal(compoundHeight, root.sizeDelta.y);
            Assert.Equal(context.Native.Geometry.AnchorMin, root.anchorMin);
            Assert.Equal(context.Native.Geometry.AnchorMax, root.anchorMax);
            Assert.Equal(context.Native.Geometry.Pivot, root.pivot);
            Assert.Equal(context.Native.Geometry.AnchoredPosition, root.anchoredPosition);
            var emergencyFrame = Assert.IsType<Image>(emergencyStop.GetComponent<Image>());
            var disclosureFrame = Assert.IsType<Image>(disclosure.GetComponent<Image>());
            Assert.Same(context.Native.StateVisuals.InactiveFrame, emergencyFrame.sprite);
            Assert.Same(context.Native.StateVisuals.InactiveFrame, disclosureFrame.sprite);
            Assert.Equal(
                ConfiguredIntentIconButtonVisual.EmergencyClearFrameColor,
                emergencyFrame.color);
            Assert.Equal(QuickControlColumn.DisclosureClearFrameColor, disclosureFrame.color);
            Assert.Equal(Image.Type.Sliced, emergencyFrame.type);
            Assert.Equal(Image.Type.Sliced, disclosureFrame.type);
            var powerIcon = Assert.IsType<RectTransform>(DirectChild(
                emergencyStop.transform,
                "PowerIcon"));
            var powerImage = Assert.IsType<Image>(powerIcon.gameObject.GetComponent<Image>());
            Assert.Same(context.Native.EmergencyStopIcon, powerImage.sprite);
            Assert.Equal(new Vector2(0.14f, 0.14f), powerIcon.anchorMin);
            Assert.Equal(new Vector2(0.86f, 0.86f), powerIcon.anchorMax);
            Assert.True(powerImage.preserveAspect);
            Assert.Null(FindDirectChildOrNull(emergencyStop.transform, "ExclamationBar"));
            Assert.Null(FindDirectChildOrNull(emergencyStop.transform, "ExclamationDot"));
            Assert.False(drawer.gameObject.activeSelf);
            Assert.Equal(2, CountLiveButtons(root));

            var closedGlyph = DirectChild(disclosure.transform, "ClosedGlyph");
            var closedLeft = Assert.IsType<RectTransform>(DirectChild(
                closedGlyph,
                "Segment.0"));
            var closedCenter = Assert.IsType<RectTransform>(DirectChild(
                closedGlyph,
                "Segment.1"));
            var openGlyph = DirectChild(disclosure.transform, "OpenGlyph");
            var openLeft = Assert.IsType<RectTransform>(DirectChild(
                openGlyph,
                "Segment.0"));
            var openCenter = Assert.IsType<RectTransform>(DirectChild(
                openGlyph,
                "Segment.1"));
            Assert.True(closedCenter.anchorMin.y < closedLeft.anchorMin.y);
            Assert.True(openCenter.anchorMin.y > openLeft.anchorMin.y);
            Assert.Equal(0.30f, closedCenter.anchorMin.y);
            Assert.Equal(0.75f, closedLeft.anchorMax.y);

            disclosure.onClick.Invoke();

            Assert.True(liveColumn.IsDrawerOpen);
            Assert.True(drawer.gameObject.activeSelf);
            Assert.Equal(disclosureRect.anchoredPosition.x, drawer.anchoredPosition.x);
            Assert.Equal(
                -(compoundHeight + QuickControlColumn.DrawerGap),
                drawer.anchoredPosition.y);
            var rowCount = Math.Max(
                1,
                (context.Registry.Features.Count + QuickControlColumn.DrawerColumnCount - 1) /
                QuickControlColumn.DrawerColumnCount);
            Assert.Equal(2, rowCount);
            Assert.Equal(
                1,
                (QuickControlColumn.DrawerColumnCount * rowCount) -
                context.Registry.Features.Count);
            Assert.Equal(
                (2f * QuickControlColumn.DrawerPadding) +
                (QuickControlColumn.DrawerColumnCount *
                 QuickControlColumn.FeatureControlSize) +
                ((QuickControlColumn.DrawerColumnCount - 1) *
                 QuickControlColumn.DrawerGridGap),
                drawer.sizeDelta.x);
            Assert.Equal(
                (2f * QuickControlColumn.DrawerPadding) +
                (rowCount * QuickControlColumn.FeatureControlSize) +
                ((rowCount - 1) * QuickControlColumn.DrawerGridGap),
                drawer.sizeDelta.y);

            var panelFrame = Assert.IsType<Image>(drawer.gameObject.GetComponent<Image>());
            Assert.Same(context.Native.StateVisuals.InactiveFrame, panelFrame.sprite);
            Assert.Equal(Image.Type.Sliced, panelFrame.type);
            Assert.Equal(Color.white, panelFrame.color);
            Assert.True(panelFrame.raycastTarget);
            var fillRect = Assert.IsType<RectTransform>(DirectChild(
                drawer,
                QuickControlColumn.DrawerFillObjectName));
            var fill = Assert.IsType<Image>(fillRect.gameObject.GetComponent<Image>());
            Assert.Null(fill.sprite);
            Assert.Equal(QuickControlColumn.DrawerBackgroundColor, fill.color);
            Assert.False(fill.raycastTarget);
            Assert.Equal(QuickControlColumn.DrawerBorderInset, fillRect.offsetMin.x);
            Assert.Equal(QuickControlColumn.DrawerBorderInset, fillRect.offsetMin.y);
            Assert.Equal(-QuickControlColumn.DrawerBorderInset, fillRect.offsetMax.x);
            Assert.Equal(-QuickControlColumn.DrawerBorderInset, fillRect.offsetMax.y);

            for (var index = 0; index < context.Registry.Features.Count; index++)
            {
                var registration = context.Registry.Features[index];
                Assert.True(liveColumn.TryGetButton(registration.FeatureId, out var feature));
                var featureRect = Assert.IsType<RectTransform>(feature.transform);
                Assert.Same(drawer, featureRect.parent);
                Assert.Equal(
                    QuickControlColumn.DrawerPadding +
                    ((index % QuickControlColumn.DrawerColumnCount) *
                     (QuickControlColumn.FeatureControlSize +
                      QuickControlColumn.DrawerGridGap)),
                    featureRect.anchoredPosition.x);
                Assert.Equal(
                    -(QuickControlColumn.DrawerPadding +
                      ((index / QuickControlColumn.DrawerColumnCount) *
                       (QuickControlColumn.FeatureControlSize +
                        QuickControlColumn.DrawerGridGap))),
                    featureRect.anchoredPosition.y);
            }
            Assert.Equal(2 + context.Registry.Features.Count, CountLiveButtons(root));
            Assert.False(DirectChild(disclosure.transform, "ClosedGlyph").gameObject.activeSelf);
            Assert.True(DirectChild(disclosure.transform, "OpenGlyph").gameObject.activeSelf);

            disclosure.onClick.Invoke();

            Assert.False(liveColumn.IsDrawerOpen);
            Assert.False(drawer.gameObject.activeSelf);
            Assert.Equal(2, CountLiveButtons(root));
        }
    }

    [Fact]
    public void FaultedFeatureRaisesColorAndStructuralAttentionOnTheClosedDrawer()
    {
        using var context = new Context();
        Assert.True(
            QuickControlColumn.TryCreate(
                context.Registry,
                context.EmergencyStop,
                context.Native,
                allowFeatureControls: true,
                out var column,
                out var reason,
                context.ResolveIcon),
            reason);
        using (var liveColumn = Assert.IsType<QuickControlColumn>(column))
        {
            Assert.True(liveColumn.TryGetButton(
                QuickControlColumn.DrawerControlId,
                out var disclosure));
            Assert.True(liveColumn.TryGetDrawerPresentation(out var healthy));
            Assert.False(healthy.IsOpen);
            Assert.False(healthy.HasAttention);
            Assert.False(DirectChild(
                disclosure.transform,
                "AttentionMarker").gameObject.activeSelf);

            context.Statuses.Get(AutoItemsFeatureContribution.FeatureId).Observe(
                configuredEnabled: true,
                FeatureStatusState.Faulted,
                FeatureStatusReasonCode.RuntimeFailure,
                "Auto Items failed.");
            liveColumn.Render();

            Assert.True(liveColumn.TryGetDrawerPresentation(out var attention));
            Assert.False(attention.IsOpen);
            Assert.True(attention.HasAttention);
            Assert.Equal(
                ConfiguredIntentFrameTreatment.InactiveRecessed,
                attention.FrameTreatment);
            Assert.Equal(ConfiguredIntentIconButtonVisual.UnhealthyColor, attention.Color);
            Assert.Equal(
                QuickControlColumn.DisclosureAttentionFrameColor,
                attention.FrameColor);
            Assert.Equal(
                QuickControlColumn.DisclosureAttentionFrameColor,
                Assert.IsType<Image>(disclosure.GetComponent<Image>()).color);
            Assert.True(DirectChild(
                disclosure.transform,
                "AttentionMarker").gameObject.activeSelf);
            Assert.True(DirectChild(disclosure.transform, "ClosedGlyph").gameObject.activeSelf);
            Assert.False(DirectChild(disclosure.transform, "OpenGlyph").gameObject.activeSelf);
        }
    }

    [Fact]
    public void MissingAuditedSpriteCreatesNoLiveControlAndNamesTheFeature()
    {
        using var context = new Context();
        bool Resolve(
            AutomationFeatureControlRegistration registration,
            out Sprite? icon,
            out string reason)
        {
            if (registration.FeatureId == AutoItemsFeatureContribution.FeatureId)
            {
                icon = null;
                reason = "ScreenWorld capture missing";
                return false;
            }
            return context.ResolveIcon(registration, out icon, out reason);
        }

        Assert.True(
            QuickControlColumn.TryCreate(
                context.Registry,
                context.EmergencyStop,
                context.Native,
                allowFeatureControls: true,
                out var column,
                out var reason,
                Resolve),
            reason);
        using (var liveColumn = Assert.IsType<QuickControlColumn>(column))
        {
            Assert.False(liveColumn.TryGetButton(
                AutoItemsFeatureContribution.FeatureId,
                out _));
            Assert.Contains(
                AutoItemsFeatureContribution.FeatureId,
                liveColumn.Failures.Keys);
            Assert.DoesNotContain(
                AutoItemsFeatureContribution.FeatureId,
                liveColumn.DrawerControlIds);
            Assert.Contains("Auto Items", reason);
            Assert.Contains("ScreenWorld capture missing", reason);
        }
    }

    [Fact]
    public void MissingAnchorCreatesNoColumnAndNamesTheContract()
    {
        using var context = new Context();
        var missingAnchor = new QuickControlNativePrimitives(
            null!,
            context.Native.StateVisuals,
            context.Native.EmergencyStopIcon,
            context.Native.Geometry);

        Assert.False(
            QuickControlColumn.TryCreate(
                context.Registry,
                context.EmergencyStop,
                missingAnchor,
                allowFeatureControls: true,
                out var column,
                out var reason,
                context.ResolveIcon));
        Assert.Null(column);
        Assert.Contains("HelpButtons anchor", reason);
    }

    [Fact]
    public void UnconstructibleStateVisualCreatesNoColumn()
    {
        using var context = new Context();
        var missingActiveFrame = new QuickControlNativePrimitives(
            context.Native.Anchor,
            new NativeButtonStateVisualPrimitives(new Sprite(), null!),
            context.Native.EmergencyStopIcon,
            context.Native.Geometry);

        Assert.False(
            QuickControlColumn.TryCreate(
                context.Registry,
                context.EmergencyStop,
                missingActiveFrame,
                allowFeatureControls: true,
                out var column,
                out var reason,
                context.ResolveIcon));
        Assert.Null(column);
        Assert.Contains("inactive/active state frame pair", reason);

        var missingIcon = new QuickControlNativePrimitives(
            context.Native.Anchor,
            context.Native.StateVisuals,
            null!,
            context.Native.Geometry);
        Assert.False(
            QuickControlColumn.TryCreate(
                context.Registry,
                context.EmergencyStop,
                missingIcon,
                allowFeatureControls: true,
                out column,
                out reason,
                context.ResolveIcon));
        Assert.Null(column);
        Assert.Contains("power-lightning", reason);
    }

    [Fact]
    public void EmergencyStopButtonMutatesSafetyEmergencyDisableImmediately()
    {
        using var context = new Context();
        Assert.True(
            QuickControlColumn.TryCreate(
                context.Registry,
                context.EmergencyStop,
                context.Native,
                allowFeatureControls: true,
                out var column,
                out var reason,
                context.ResolveIcon),
            reason);
        using (var liveColumn = Assert.IsType<QuickControlColumn>(column))
        {
            Assert.True(liveColumn.TryGetButton(
                QuickControlColumn.EmergencyStopId,
                out var button));

            button.onClick.Invoke();

            Assert.True(context.Configuration.EmergencyDisable.Value);
            Assert.Equal(new[] { true }, context.EmergencyChanges);
            Assert.True(liveColumn.TryGetPresentation(
                QuickControlColumn.EmergencyStopId,
                out var presentation));
            Assert.Equal(
                ConfiguredIntentFrameTreatment.ActiveRaised,
                presentation.FrameTreatment);
            Assert.Equal("STOPPED", presentation.TooltipLabel);
            Assert.Equal(
                ConfiguredIntentIconButtonVisual.EmergencyStoppedFrameColor,
                presentation.FrameColor);
            var frame = Assert.IsType<Image>(button.GetComponent<Image>());
            Assert.Equal(
                ConfiguredIntentIconButtonVisual.EmergencyStoppedFrameColor,
                frame.color);
            Assert.True(liveColumn.TryGetButton(
                QuickControlColumn.DrawerControlId,
                out var disclosure));
            Assert.Equal(
                QuickControlColumn.DisclosureStoppedFrameColor,
                Assert.IsType<Image>(disclosure.GetComponent<Image>()).color);
            var tooltip = button.gameObject.GetComponent<HoverTooltip>()?.tooltipItem;
            Assert.NotNull(tooltip);
            Assert.Equal("STOPPED", tooltip!.GetDisplayType());
            Assert.Equal(presentation.Color, tooltip.GetColor());

            button.onClick.Invoke();

            Assert.False(context.Configuration.EmergencyDisable.Value);
            Assert.Equal(new[] { true, false }, context.EmergencyChanges);
            Assert.True(liveColumn.TryGetPresentation(
                QuickControlColumn.EmergencyStopId,
                out var cleared));
            Assert.Equal(
                ConfiguredIntentFrameTreatment.InactiveRecessed,
                cleared.FrameTreatment);
            Assert.Equal("READY / STOP ALL", cleared.TooltipLabel);
            Assert.Equal(
                ConfiguredIntentIconButtonVisual.EmergencyClearFrameColor,
                cleared.FrameColor);
            Assert.Equal(
                QuickControlColumn.DisclosureClearFrameColor,
                Assert.IsType<Image>(disclosure.GetComponent<Image>()).color);
        }
    }

    [Fact]
    public void EmergencyStopIconCaptureRequiresOneExactAuditedPowerLightningSprite()
    {
        var expected = new Sprite { name = QuickControlNativeAdapter.EmergencyStopIconName };
        var decoy = new Sprite { name = "power-ring" };

        Assert.True(
            QuickControlNativeAdapter.TryResolveEmergencyStopIcon(
                new[] { decoy, expected },
                out var icon,
                out var reason),
            reason);
        Assert.Same(expected, icon);

        Assert.False(
            QuickControlNativeAdapter.TryResolveEmergencyStopIcon(
                new[]
                {
                    expected,
                    new Sprite { name = QuickControlNativeAdapter.EmergencyStopIconName },
                },
                out icon,
                out reason));
        Assert.Null(icon);
        Assert.Contains("found 2", reason);
    }

    [Fact]
    public void AnchorResolutionRequiresTheExactAuditedHelpButtonsStructure()
    {
        var canvasObject = new GameObject("Canvas", typeof(RectTransform));
        var contentObject = Child(canvasObject, "ContentArea");
        var contentArea = contentObject.AddComponent<FakeContentArea>();
        contentArea.canvas = (RectTransform)canvasObject.transform;
        var helpButtons = Child(canvasObject, "HelpButtons");
        var settings = Child(helpButtons, "SettingsButton");
        var playerStats = Child(helpButtons, "PlayerStatsButton");
        ConfigureNativeButton(settings, new Vector2(4f, -8f));
        ConfigureNativeButton(playerStats, new Vector2(4f, -76f));

        Assert.True(
            QuickControlNativeAdapter.TryResolveAnchor(
                new Component[] { contentArea },
                out var anchor,
                out var reason),
            reason);
        Assert.Same(helpButtons.transform, anchor);
        Assert.True(
            QuickControlNativeAdapter.TryResolveControlGeometry(
                Assert.IsType<RectTransform>(helpButtons.transform),
                out var geometry,
                out var geometryReason),
            geometryReason);
        Assert.Equal(new Vector2(0f, 1f), geometry.AnchorMin);
        Assert.Equal(new Vector2(0f, 1f), geometry.AnchorMax);
        Assert.Equal(new Vector2(0f, 1f), geometry.Pivot);
        Assert.Equal(new Vector2(4f, -144f), geometry.AnchoredPosition);
        Assert.Equal(new Vector2(64f, 64f), geometry.ControlSize);

        var missing = new GameObject("WrongCanvas", typeof(RectTransform));
        var wrongContent = Child(missing, "ContentArea").AddComponent<FakeContentArea>();
        wrongContent.canvas = (RectTransform)missing.transform;
        Assert.False(
            QuickControlNativeAdapter.TryResolveAnchor(
                new Component[] { wrongContent },
                out var absent,
                out var missingReason));
        Assert.Null(absent);
        Assert.Contains(QuickControlNativeAdapter.AnchorPath, missingReason);
    }

    [Fact]
    public void AnchorResolutionNamesCanvasDeclaredTypeMismatch()
    {
        var contentObject = new GameObject("ContentArea");
        var contentArea = contentObject.AddComponent<WrongCanvasContentArea>();
        contentArea.canvas = contentObject.transform;

        Assert.False(
            QuickControlNativeAdapter.TryResolveAnchor(
                new Component[] { contentArea },
                out var anchor,
                out var reason));

        Assert.Null(anchor);
        Assert.Contains("WrongCanvasContentArea.canvas declared type check failed", reason);
        Assert.Contains("expected UnityEngine.RectTransform", reason);
        Assert.Contains("actual UnityEngine.Transform", reason);
    }

    private static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent.transform, false);
        return child;
    }

    private static void ConfigureNativeButton(GameObject button, Vector2 position)
    {
        var rect = Assert.IsType<RectTransform>(button.transform);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.rect = new Rect(0f, 0f, 64f, 64f);
    }

    private static Transform FindDescendant(Transform root, string name)
    {
        if (string.Equals(root.name, name, StringComparison.Ordinal)) return root;
        for (var index = 0; index < root.childCount; index++)
        {
            var candidate = FindDescendant(root.GetChild(index), name);
            if (candidate is not null) return candidate;
        }
        return null!;
    }

    private static Transform DirectChild(Transform parent, string name)
    {
        for (var index = 0; index < parent.childCount; index++)
        {
            var child = parent.GetChild(index);
            if (string.Equals(child.name, name, StringComparison.Ordinal)) return child;
        }
        return null!;
    }

    private static Transform? FindDirectChildOrNull(Transform parent, string name)
    {
        for (var index = 0; index < parent.childCount; index++)
        {
            var child = parent.GetChild(index);
            if (string.Equals(child.name, name, StringComparison.Ordinal)) return child;
        }
        return null;
    }

    private static int CountLiveButtons(Transform root, bool ancestorsActive = true)
    {
        var active = ancestorsActive && root.gameObject.activeSelf;
        var count = active && root.gameObject.GetComponent<Button>() is not null ? 1 : 0;
        for (var index = 0; index < root.childCount; index++)
            count += CountLiveButtons(root.GetChild(index), active);
        return count;
    }

    private static void AssertAllRectTransforms(Transform root)
    {
        Assert.IsType<RectTransform>(root);
        for (var index = 0; index < root.childCount; index++)
            AssertAllRectTransforms(root.GetChild(index));
    }

    private sealed class FakeContentArea : Behaviour
    {
        public RectTransform? canvas;
    }

    private sealed class WrongCanvasContentArea : Behaviour
    {
        public Transform? canvas;
    }

    private sealed class Context : IDisposable
    {
        private readonly Dictionary<string, Sprite> _icons = new(StringComparer.Ordinal);

        internal Context()
        {
            var file = new ConfigFile();
            Configuration = BepInExAutomataConfiguration.Bind(file);
            var mentor = MentorConfig.Bind(file);
            Configuration.AttachMentor(mentor);
            Store = new AutomataConfigurationStore(Configuration, (_, _) => { });
            Statuses = new AutomataFeatureStatuses(
                Store.Current,
                lifecycleGeneration: 1,
                registry: new FeatureStatusRegistry(),
                configurationGeneration: Store.CurrentGeneration);
            Registry = AutomationFeatureControlRegistry.Create(
                Store,
                Statuses,
                new SpellLevelCapabilityState(),
                mentor);
            EmergencyStop = new EmergencyStopControl(
                Store,
                stopped => EmergencyChanges.Add(stopped));
            var anchor = (RectTransform)new GameObject(
                "HelpButtons",
                typeof(RectTransform)).transform;
            Native = new QuickControlNativePrimitives(
                anchor,
                new NativeButtonStateVisualPrimitives(new Sprite(), new Sprite()),
                new Sprite { name = QuickControlNativeAdapter.EmergencyStopIconName },
                new QuickControlNativeGeometry(
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(4f, -144f),
                    new Vector2(64f, 64f)));
        }

        internal BepInExAutomataConfiguration Configuration { get; }
        internal AutomataConfigurationStore Store { get; }
        internal AutomataFeatureStatuses Statuses { get; }
        internal AutomationFeatureControlRegistry Registry { get; }
        internal EmergencyStopControl EmergencyStop { get; }
        internal List<bool> EmergencyChanges { get; } = new();
        internal QuickControlNativePrimitives Native { get; }

        internal bool ResolveIcon(
            AutomationFeatureControlRegistration registration,
            out Sprite? icon,
            out string reason)
        {
            if (registration.PageLabel == "Auto Cast")
            {
                return NativeFeatureIconResolver.TryResolve(
                    registration.PageLabel,
                    capturedRail: null,
                    out icon,
                    out reason);
            }
            if (!_icons.TryGetValue(registration.FeatureId, out var resolved))
            {
                resolved = new Sprite();
                _icons.Add(registration.FeatureId, resolved);
            }
            icon = resolved;
            reason = string.Empty;
            return true;
        }

        public void Dispose() => Statuses.Dispose();
    }
}
