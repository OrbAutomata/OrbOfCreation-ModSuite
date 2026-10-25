using System;
using System.Collections.Generic;
using System.Linq;
using OrbModConfig;
using OrbModding.Common;
using UnityEngine;
using UnityEngine.UI;

namespace OrbAutomata;

internal delegate bool QuickControlIconResolver(
    AutomationFeatureControlRegistration registration,
    out Sprite? icon,
    out string reason);

internal readonly record struct QuickControlDrawerPresentation(
    bool IsOpen,
    bool HasAttention,
    ConfiguredIntentFrameTreatment FrameTreatment,
    Color Color,
    Color FrameColor,
    string TooltipLabel);

/// <summary>
/// Suite-owned emergency stop and disclosure under the audited native HelpButtons anchor. The
/// disclosure footer opens the registered feature controls in a transient panel below the compound
/// control.
/// </summary>
internal sealed class QuickControlColumn : IDisposable
{
    internal const string ObjectName = "OrbModSuite.QuickControls";
    internal const string EmergencyStopId = "emergency-stop";
    internal const string DrawerControlId = "feature-drawer";
    internal const string DrawerObjectName = "FeatureDrawer";
    internal const string DrawerFillObjectName = "PanelFill";
    internal const float FeatureControlSize = 52f;
    internal const float DisclosureHeight = 32f;
    internal const float DisclosureOverlap = 4f;
    internal const float DrawerGap = 4f;
    internal const float DrawerPadding = 10f;
    internal const float DrawerGridGap = 6f;
    internal const float DrawerBorderInset = 4f;
    internal const int DrawerColumnCount = 4;
    internal static readonly Color DrawerBackgroundColor = ModConfigPalette.Background;
    internal static readonly Color DisclosureClearFrameColor =
        new(0.11f, 0.48f, 0.18f, 1.0f);
    internal static readonly Color DisclosureStoppedFrameColor =
        new(0.52f, 0.10f, 0.10f, 1.0f);
    internal static readonly Color DisclosureAttentionFrameColor =
        new(0.72f, 0.12f, 0.12f, 1.0f);

    private readonly GameObject _root;
    private readonly Dictionary<string, Entry> _entries;
    private readonly DrawerEntry _drawer;
    private readonly IReadOnlyCollection<string> _controlIds;
    private readonly IReadOnlyCollection<string> _drawerControlIds;
    private readonly IReadOnlyDictionary<string, string> _failures;
    private bool _disposed;

    private QuickControlColumn(
        GameObject root,
        Dictionary<string, Entry> entries,
        DrawerEntry drawer,
        IReadOnlyCollection<string> controlIds,
        IReadOnlyCollection<string> drawerControlIds,
        bool allowsFeatureControls,
        IReadOnlyDictionary<string, string> failures)
    {
        _root = root;
        _entries = entries;
        _drawer = drawer;
        _controlIds = controlIds;
        _drawerControlIds = drawerControlIds;
        AllowsFeatureControls = allowsFeatureControls;
        _failures = failures;
    }

    internal bool IsAlive => !_disposed && _root != null;
    internal bool AllowsFeatureControls { get; }
    internal bool IsDrawerOpen => _drawer.IsOpen;
    internal IReadOnlyCollection<string> ControlIds => _controlIds;
    internal IReadOnlyCollection<string> DrawerControlIds => _drawerControlIds;
    internal IReadOnlyDictionary<string, string> Failures => _failures;

    internal static bool TryCreate(
        AutomationFeatureControlRegistry registry,
        EmergencyStopControl emergencyStop,
        QuickControlNativePrimitives? native,
        bool allowFeatureControls,
        out QuickControlColumn? column,
        out string reason,
        QuickControlIconResolver? resolveIcon = null)
    {
        column = null;
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (emergencyStop is null) throw new ArgumentNullException(nameof(emergencyStop));
        if (native?.Anchor is null)
        {
            reason = "audited top-left HelpButtons anchor is unavailable";
            return false;
        }
        if (native.StateVisuals?.InactiveFrame is null ||
            native.StateVisuals.ActiveFrame is null)
        {
            reason =
                "audited UIViewRadioButton inactive/active state frame pair is unconstructible";
            return false;
        }
        if (native.EmergencyStopIcon is null)
        {
            reason = "audited power-lightning emergency-stop sprite is unavailable";
            return false;
        }

        resolveIcon ??= ResolveNativeIcon;
        GameObject? root = null;
        try
        {
            if (!TryCreateRectTransformObject(
                    ObjectName,
                    out root,
                    out var rootRect,
                    out reason))
                return false;
            root.SetActive(false);
            rootRect.SetParent(native.Anchor, false);
            rootRect.anchorMin = native.Geometry.AnchorMin;
            rootRect.anchorMax = native.Geometry.AnchorMax;
            rootRect.pivot = native.Geometry.Pivot;
            rootRect.anchoredPosition = native.Geometry.AnchoredPosition;

            var compoundHeight = native.Geometry.ControlSize.y +
                                 DisclosureHeight - DisclosureOverlap;

            var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            var failures = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!TryCreateEmergencyEntry(
                    rootRect,
                    native.Geometry.ControlSize,
                    emergencyStop,
                    native.EmergencyStopIcon,
                    native.StateVisuals,
                    out var stopEntry,
                    out var stopReason))
            {
                UnityEngine.Object.Destroy(root);
                reason = "Suite emergency stop state visual unavailable: " + stopReason;
                return false;
            }
            entries.Add(EmergencyStopId, stopEntry!);

            if (!TryCreateDrawerContainer(
                    rootRect,
                    registry.Features.Count,
                    compoundHeight,
                    native.StateVisuals.InactiveFrame,
                    out var drawerObject,
                    out var drawerRect,
                    out var drawerContainerReason))
            {
                UnityEngine.Object.Destroy(root);
                reason = "feature drawer container unavailable: " + drawerContainerReason;
                return false;
            }

            if (allowFeatureControls)
            {
                var slot = 0;
                foreach (var registration in registry.Features)
                {
                    if (!resolveIcon(registration, out var icon, out var iconReason) ||
                        icon is null)
                    {
                        failures[registration.FeatureId] =
                            $"{registration.DisplayName}: audited sprite unavailable: {iconReason}";
                        slot++;
                        continue;
                    }
                    if (!TryCreateFeatureEntry(
                            drawerRect,
                            slot++,
                            registration,
                            icon,
                            native.StateVisuals,
                            out var entry,
                            out var entryReason))
                    {
                        failures[registration.FeatureId] =
                            $"{registration.DisplayName}: state visual unavailable: {entryReason}";
                        continue;
                    }
                    entries.Add(registration.FeatureId, entry!);
                }
            }

            if (!TryCreateDrawerEntry(
                    rootRect,
                    new Vector2(
                        0f,
                        -(native.Geometry.ControlSize.y - DisclosureOverlap)),
                    new Vector2(native.Geometry.ControlSize.x, DisclosureHeight),
                    drawerObject,
                    registry,
                    emergencyStop,
                    allowFeatureControls,
                    native.StateVisuals,
                    out var drawer,
                    out var drawerReason))
            {
                UnityEngine.Object.Destroy(root);
                reason = "feature drawer disclosure unavailable: " + drawerReason;
                return false;
            }
            stopEntry!.Button.onClick.AddListener(() => drawer!.Render(force: true));

            rootRect.sizeDelta = new Vector2(
                native.Geometry.ControlSize.x,
                compoundHeight);
            var drawerControlIds = registry.Features
                .Where(feature => entries.ContainsKey(feature.FeatureId))
                .Select(feature => feature.FeatureId)
                .ToArray();
            var controlIds = entries.Keys
                .Append(DrawerControlId)
                .ToArray();
            column = new QuickControlColumn(
                root,
                entries,
                drawer!,
                controlIds,
                drawerControlIds,
                allowFeatureControls,
                failures);
            column.Render(force: true);
            root.SetActive(true);
            reason = failures.Count == 0
                ? string.Empty
                : string.Join(
                    "; ",
                    failures.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => pair.Value));
            return true;
        }
        catch (Exception ex)
        {
            if (root is not null) UnityEngine.Object.Destroy(root);
            var failure = ex.GetBaseException();
            reason =
                "quick-control column construction failed after the " +
                "UnityEngine.GameObject..ctor(System.String, System.Type[]) RectTransform check: " +
                $"{failure.GetType().FullName}: {failure.Message}";
            return false;
        }
    }

    internal void Render(bool force = false)
    {
        if (!IsAlive) return;
        foreach (var entry in _entries.Values) entry.Render(force);
        _drawer.Render(force);
    }

    internal bool TryGetButton(string controlId, out Button button)
    {
        if (string.Equals(controlId, DrawerControlId, StringComparison.Ordinal))
        {
            button = _drawer.Button;
            return true;
        }
        if (_entries.TryGetValue(controlId, out var entry))
        {
            button = entry.Button;
            return true;
        }
        button = null!;
        return false;
    }

    internal bool TryGetPresentation(
        string controlId,
        out ConfiguredIntentPresentation presentation)
    {
        if (_entries.TryGetValue(controlId, out var entry) &&
            entry.Visual.Rendered is { } rendered)
        {
            presentation = rendered;
            return true;
        }
        presentation = default;
        return false;
    }

    internal bool TryGetDrawerPresentation(
        out QuickControlDrawerPresentation presentation)
    {
        if (_drawer.Rendered is { } rendered)
        {
            presentation = rendered;
            return true;
        }
        presentation = default;
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _entries.Values) entry.Button.onClick.RemoveAllListeners();
        _drawer.Button.onClick.RemoveAllListeners();
        if (_root != null) UnityEngine.Object.Destroy(_root);
    }

    private static bool TryCreateDrawerContainer(
        RectTransform parent,
        int featureCount,
        float compoundHeight,
        Sprite panelFrameSprite,
        out GameObject drawer,
        out RectTransform drawerRect,
        out string reason)
    {
        if (!TryCreateRectTransformObject(
                DrawerObjectName,
                out drawer,
                out drawerRect,
                out reason))
            return false;
        drawer.SetActive(false);
        drawerRect.SetParent(parent, false);
        drawerRect.anchorMin = new Vector2(0f, 1f);
        drawerRect.anchorMax = new Vector2(0f, 1f);
        drawerRect.pivot = new Vector2(0f, 1f);
        drawerRect.anchoredPosition = new Vector2(
            0f,
            -(compoundHeight + DrawerGap));
        var rowCount = Math.Max(
            1,
            (featureCount + DrawerColumnCount - 1) / DrawerColumnCount);
        drawerRect.sizeDelta = new Vector2(
            (2f * DrawerPadding) + (DrawerColumnCount * FeatureControlSize) +
                ((DrawerColumnCount - 1) * DrawerGridGap),
            (2f * DrawerPadding) + (rowCount * FeatureControlSize) +
                (Math.Max(0, rowCount - 1) * DrawerGridGap));

        var frame = drawer.AddComponent<Image>();
        frame.sprite = panelFrameSprite;
        frame.type = Image.Type.Sliced;
        frame.color = Color.white;
        // Own the complete drawer rectangle so wheel and pointer input cannot pass through its
        // padding or grid gaps to a native control underneath. Child buttons still raycast first.
        frame.raycastTarget = true;
        if (!TryCreateGlyphObject(
                DrawerFillObjectName,
                drawerRect,
                Vector2.zero,
                Vector2.one,
                out var fillObject,
                out reason))
        {
            UnityEngine.Object.Destroy(drawer);
            return false;
        }
        var fillRect = (RectTransform)fillObject.transform;
        fillRect.offsetMin = new Vector2(DrawerBorderInset, DrawerBorderInset);
        fillRect.offsetMax = new Vector2(-DrawerBorderInset, -DrawerBorderInset);
        var fill = fillObject.AddComponent<Image>();
        fill.color = DrawerBackgroundColor;
        fill.raycastTarget = false;
        reason = string.Empty;
        return true;
    }

    private static bool TryCreateFeatureEntry(
        RectTransform parent,
        int slot,
        AutomationFeatureControlRegistration registration,
        Sprite iconSprite,
        NativeButtonStateVisualPrimitives stateVisuals,
        out Entry? entry,
        out string reason)
    {
        if (!TryCreateControlObject(
                "Feature." + registration.FeatureId,
                parent,
                DrawerGridPositionFor(slot),
                new Vector2(FeatureControlSize, FeatureControlSize),
                out var root,
                out reason))
        {
            entry = null;
            return false;
        }
        var button = root.AddComponent<Button>();
        if (!TryCreateGlyphObject(
                "Icon",
                root.transform,
                new Vector2(0.16f, 0.16f),
                new Vector2(0.84f, 0.84f),
                out var iconObject,
                out reason))
        {
            UnityEngine.Object.Destroy(root);
            entry = null;
            return false;
        }
        var icon = iconObject.AddComponent<Image>();
        icon.sprite = iconSprite;
        if (!ConfiguredIntentIconButtonVisual.TryCreate(
                root,
                button,
                new[] { icon },
                stateVisuals,
                out var visual,
                out reason))
        {
            UnityEngine.Object.Destroy(root);
            entry = null;
            return false;
        }
        root.AddComponent<HoverTooltip>().Setup(registration.Tooltip);
        Entry? created = null;
        created = new Entry(
            button,
            visual!,
            () => ConfiguredIntentIconButtonVisual.FromFeatureStatus(registration.Status));
        button.onClick.AddListener(() =>
        {
            registration.Toggle();
            created.Render(force: true);
        });
        entry = created;
        return true;
    }

    private static bool TryCreateEmergencyEntry(
        RectTransform parent,
        Vector2 size,
        EmergencyStopControl control,
        Sprite iconSprite,
        NativeButtonStateVisualPrimitives stateVisuals,
        out Entry? entry,
        out string reason)
    {
        if (!TryCreateControlObject(
                "Safety.EmergencyStop",
                parent,
                Vector2.zero,
                size,
                out var root,
                out reason))
        {
            entry = null;
            return false;
        }
        var button = root.AddComponent<Button>();
        if (!TryCreateGlyphObject(
                "PowerIcon",
                root.transform,
                new Vector2(0.14f, 0.14f),
                new Vector2(0.86f, 0.86f),
                out var iconObject,
                out reason))
        {
            UnityEngine.Object.Destroy(root);
            entry = null;
            return false;
        }
        var icon = iconObject.AddComponent<Image>();
        icon.sprite = iconSprite;
        if (!ConfiguredIntentIconButtonVisual.TryCreate(
                root,
                button,
                new[] { icon },
                stateVisuals,
                out var visual,
                out reason))
        {
            UnityEngine.Object.Destroy(root);
            entry = null;
            return false;
        }
        root.AddComponent<HoverTooltip>().Setup(new EmergencyStopTooltip(control));
        Entry? created = null;
        created = new Entry(
            button,
            visual!,
            () => ConfiguredIntentIconButtonVisual.FromEmergencyStop(control));
        button.onClick.AddListener(() =>
        {
            control.Activate();
            created.Render(force: true);
        });
        entry = created;
        return true;
    }

    private static bool TryCreateDrawerEntry(
        RectTransform parent,
        Vector2 position,
        Vector2 size,
        GameObject drawerObject,
        AutomationFeatureControlRegistry registry,
        EmergencyStopControl emergencyStop,
        bool allowFeatureControls,
        NativeButtonStateVisualPrimitives stateVisuals,
        out DrawerEntry? entry,
        out string reason)
    {
        if (!TryCreateControlObject(
                "Drawer.Disclosure",
                parent,
                position,
                size,
                out var root,
                out reason))
        {
            entry = null;
            return false;
        }
        var button = root.AddComponent<Button>();
        if (!TryCreateDrawerGlyph(
                "ClosedGlyph",
                root.transform,
                closed: true,
                out var closedGlyph,
                out var closedImages,
                out reason) ||
            !TryCreateDrawerGlyph(
                "OpenGlyph",
                root.transform,
                closed: false,
                out var openGlyph,
                out var openImages,
                out reason) ||
            !TryCreateAttentionMarker(
                root.transform,
                out var attentionMarker,
                out var attentionImages,
                out reason))
        {
            UnityEngine.Object.Destroy(root);
            entry = null;
            return false;
        }
        var glyphs = closedImages
            .Concat(openImages)
            .Concat(attentionImages)
            .ToArray();
        if (!ConfiguredIntentIconButtonVisual.TryCreate(
                root,
                button,
                glyphs,
                stateVisuals,
                out var visual,
                out reason))
        {
            UnityEngine.Object.Destroy(root);
            entry = null;
            return false;
        }
        var created = new DrawerEntry(
            button,
            visual!,
            drawerObject,
            closedGlyph,
            openGlyph,
            attentionMarker,
            registry,
            emergencyStop,
            allowFeatureControls);
        root.AddComponent<HoverTooltip>().Setup(new QuickControlDrawerTooltip(created));
        if (allowFeatureControls)
            button.onClick.AddListener(created.Toggle);
        else
            button.interactable = false;
        entry = created;
        return true;
    }

    private static bool TryCreateDrawerGlyph(
        string name,
        Transform parent,
        bool closed,
        out GameObject glyph,
        out IReadOnlyList<Image> images,
        out string reason)
    {
        if (!TryCreateGlyphObject(
                name,
                parent,
                Vector2.zero,
                Vector2.one,
                out glyph,
                out reason))
        {
            images = Array.Empty<Image>();
            return false;
        }
        var anchors = closed
            ? new[]
            {
                (new Vector2(0.25f, 0.55f), new Vector2(0.41f, 0.75f)),
                (new Vector2(0.42f, 0.30f), new Vector2(0.58f, 0.50f)),
                (new Vector2(0.59f, 0.55f), new Vector2(0.75f, 0.75f)),
            }
            : new[]
            {
                (new Vector2(0.25f, 0.30f), new Vector2(0.41f, 0.50f)),
                (new Vector2(0.42f, 0.55f), new Vector2(0.58f, 0.75f)),
                (new Vector2(0.59f, 0.30f), new Vector2(0.75f, 0.50f)),
            };
        var created = new List<Image>(anchors.Length);
        for (var index = 0; index < anchors.Length; index++)
        {
            if (!TryCreateGlyphObject(
                    "Segment." + index,
                    glyph.transform,
                    anchors[index].Item1,
                    anchors[index].Item2,
                    out var segment,
                    out reason))
            {
                UnityEngine.Object.Destroy(glyph);
                images = Array.Empty<Image>();
                return false;
            }
            created.Add(segment.AddComponent<Image>());
        }
        images = created;
        reason = string.Empty;
        return true;
    }

    private static bool TryCreateAttentionMarker(
        Transform parent,
        out GameObject marker,
        out IReadOnlyList<Image> images,
        out string reason)
    {
        if (!TryCreateGlyphObject(
                "AttentionMarker",
                parent,
                new Vector2(0.78f, 0.10f),
                new Vector2(0.96f, 0.90f),
                out marker,
                out reason))
        {
            images = Array.Empty<Image>();
            return false;
        }
        if (!TryCreateGlyphObject(
                "Bar",
                marker.transform,
                new Vector2(0.40f, 0.32f),
                new Vector2(0.60f, 0.90f),
                out var bar,
                out reason) ||
            !TryCreateGlyphObject(
                "Dot",
                marker.transform,
                new Vector2(0.36f, 0.06f),
                new Vector2(0.64f, 0.26f),
                out var dot,
                out reason))
        {
            UnityEngine.Object.Destroy(marker);
            images = Array.Empty<Image>();
            return false;
        }
        images = new[] { bar.AddComponent<Image>(), dot.AddComponent<Image>() };
        reason = string.Empty;
        return true;
    }

    private static bool TryCreateControlObject(
        string name,
        Transform parent,
        Vector2 position,
        Vector2 size,
        out GameObject root,
        out string reason)
    {
        if (!TryCreateRectTransformObject(name, out root, out var rect, out reason))
            return false;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        root.AddComponent<Image>();
        reason = string.Empty;
        return true;
    }

    private static bool TryCreateGlyphObject(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        out GameObject glyph,
        out string reason)
    {
        if (!TryCreateRectTransformObject(name, out glyph, out var rect, out reason))
            return false;
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        reason = string.Empty;
        return true;
    }

    private static bool TryCreateRectTransformObject(
        string name,
        out GameObject gameObject,
        out RectTransform rect,
        out string reason)
    {
        try
        {
            gameObject = new GameObject(name, typeof(RectTransform));
        }
        catch (Exception ex)
        {
            gameObject = null!;
            rect = null!;
            var root = ex.GetBaseException();
            reason =
                $"UnityEngine.GameObject..ctor(System.String, System.Type[]) failed for '{name}' " +
                "while requesting UnityEngine.RectTransform: " +
                $"{root.GetType().FullName}: {root.Message}";
            return false;
        }
        if (TryRequireRectTransform(
                gameObject,
                $"UnityEngine.GameObject('{name}').transform",
                out rect,
                out reason))
            return true;
        UnityEngine.Object.Destroy(gameObject);
        gameObject = null!;
        return false;
    }

    internal static bool TryRequireRectTransform(
        GameObject gameObject,
        string member,
        out RectTransform rect,
        out string reason)
    {
        if (gameObject.transform is RectTransform actual)
        {
            rect = actual;
            reason = string.Empty;
            return true;
        }
        rect = null!;
        reason =
            $"{member} type check failed: expected UnityEngine.RectTransform, actual " +
            $"{gameObject.transform?.GetType().FullName ?? "<null>"}";
        return false;
    }

    private static Vector2 DrawerGridPositionFor(int slot) =>
        new(
            DrawerPadding +
            ((slot % DrawerColumnCount) * (FeatureControlSize + DrawerGridGap)),
            -(DrawerPadding +
              ((slot / DrawerColumnCount) * (FeatureControlSize + DrawerGridGap))));

    private static bool ResolveNativeIcon(
        AutomationFeatureControlRegistration registration,
        out Sprite? icon,
        out string reason) =>
        NativeFeatureIconResolver.TryResolve(
            registration.IconIdentity,
            capturedRail: null,
            out icon,
            out reason);

    private sealed class DrawerEntry
    {
        private readonly GameObject _drawerObject;
        private readonly GameObject _closedGlyph;
        private readonly GameObject _openGlyph;
        private readonly GameObject _attentionMarker;
        private readonly AutomationFeatureControlRegistry _registry;
        private readonly EmergencyStopControl _emergencyStop;
        private readonly bool _allowFeatureControls;
        private bool _isOpen;

        internal DrawerEntry(
            Button button,
            ConfiguredIntentIconButtonVisual visual,
            GameObject drawerObject,
            GameObject closedGlyph,
            GameObject openGlyph,
            GameObject attentionMarker,
            AutomationFeatureControlRegistry registry,
            EmergencyStopControl emergencyStop,
            bool allowFeatureControls)
        {
            Button = button;
            Visual = visual;
            _drawerObject = drawerObject;
            _closedGlyph = closedGlyph;
            _openGlyph = openGlyph;
            _attentionMarker = attentionMarker;
            _registry = registry;
            _emergencyStop = emergencyStop;
            _allowFeatureControls = allowFeatureControls;
        }

        internal Button Button { get; }
        internal ConfiguredIntentIconButtonVisual Visual { get; }
        internal bool IsOpen => _isOpen;
        internal QuickControlDrawerPresentation? Rendered { get; private set; }

        internal void Toggle()
        {
            if (!_allowFeatureControls) return;
            _isOpen = !_isOpen;
            _drawerObject.SetActive(_isOpen);
            Render(force: true);
        }

        internal void Render(bool force)
        {
            var presentation = ReadPresentation();
            if (!force && Rendered == presentation) return;
            Rendered = presentation;
            _closedGlyph.SetActive(!presentation.IsOpen);
            _openGlyph.SetActive(presentation.IsOpen);
            _attentionMarker.SetActive(presentation.HasAttention);
            Visual.Render(
                new ConfiguredIntentPresentation(
                    presentation.HasAttention
                        ? ConfiguredIntentIconState.Unhealthy
                        : presentation.IsOpen
                            ? ConfiguredIntentIconState.On
                            : ConfiguredIntentIconState.Off,
                    presentation.FrameTreatment,
                    presentation.Color,
                    presentation.FrameColor,
                    presentation.TooltipLabel),
                force);
        }

        internal QuickControlDrawerPresentation ReadPresentation()
        {
            var hasAttention = HasAttention();
            var stopped = _emergencyStop.IsStopped;
            return new QuickControlDrawerPresentation(
                _isOpen,
                hasAttention,
                _isOpen
                    ? ConfiguredIntentFrameTreatment.ActiveRaised
                    : ConfiguredIntentFrameTreatment.InactiveRecessed,
                hasAttention
                    ? ConfiguredIntentIconButtonVisual.UnhealthyColor
                    : stopped
                        ? ConfiguredIntentIconButtonVisual.EmergencyStoppedColor
                        : ConfiguredIntentIconButtonVisual.EmergencyClearColor,
                hasAttention
                    ? DisclosureAttentionFrameColor
                    : stopped
                        ? DisclosureStoppedFrameColor
                        : DisclosureClearFrameColor,
                !_allowFeatureControls
                    ? "FEATURES / UNAVAILABLE"
                    : _isOpen
                        ? hasAttention
                            ? "FEATURES / OPEN / ATTENTION"
                            : "FEATURES / OPEN"
                        : hasAttention
                            ? "FEATURES / CLOSED / ATTENTION"
                            : "FEATURES / CLOSED");
        }

        private bool HasAttention()
        {
            if (!_allowFeatureControls) return false;
            foreach (var registration in _registry.Features)
            {
                var status = registration.Status;
                var runtime = FeatureStatusPresenter.Present(status).RuntimeState;
                if (runtime == FeatureRuntimePresentationState.Blocked ||
                    ConfiguredIntentIconButtonVisual.FromFeatureStatus(status).State ==
                    ConfiguredIntentIconState.Unhealthy)
                    return true;
            }
            return false;
        }
    }

    private sealed class QuickControlDrawerTooltip : ITooltipable
    {
        private readonly DrawerEntry _drawer;

        internal QuickControlDrawerTooltip(DrawerEntry drawer) => _drawer = drawer;
        public string GetName() => "Automation feature drawer";
        public string GetDisplayType() => _drawer.ReadPresentation().TooltipLabel;
        public Sprite GetIcon() => null!;
        public Color GetColor() => _drawer.ReadPresentation().Color;
        public bool IsColoredIcon() => false;
        public bool HasAltTooltips() => false;
        public string GetDescription() => _drawer.IsOpen
            ? "Closes the transient automation feature panel."
            : "Opens the registered automation feature controls in a panel.";
        public List<TooltipNode> GetTooltipNodes()
        {
            var presentation = _drawer.ReadPresentation();
            return new List<TooltipNode>
            {
                new(presentation.IsOpen
                    ? "Click to close the feature drawer."
                    : "Click to open the feature drawer."),
                new(presentation.HasAttention
                    ? "One or more contained features is faulted or blocked."
                    : "No contained feature currently needs attention."),
            };
        }
        public List<TooltipNode> GetAltTooltipNodes() => new();
    }

    private sealed class Entry
    {
        private readonly Func<ConfiguredIntentPresentation> _readPresentation;

        internal Entry(
            Button button,
            ConfiguredIntentIconButtonVisual visual,
            Func<ConfiguredIntentPresentation> readPresentation)
        {
            Button = button;
            Visual = visual;
            _readPresentation = readPresentation;
        }

        internal Button Button { get; }
        internal ConfiguredIntentIconButtonVisual Visual { get; }
        internal void Render(bool force) => Visual.Render(_readPresentation(), force);
    }
}
