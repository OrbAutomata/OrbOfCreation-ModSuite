using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

/// <summary>Owns the bounded navigation viewport and its independent scroll state.</summary>
internal sealed class ModConfigNavigationRail : IDisposable
{
    private readonly RectTransform _viewport;
    private readonly RectTransform _content;
    private readonly ScrollRect _scroll;
    private readonly Button _up;
    private readonly Button _down;
    private readonly NativeFeatureRailVisualPrimitives _visuals;
    private readonly List<GameObject> _rows = new();
    private IReadOnlyList<ModConfigTopPage> _pages;
    private int _selected;
    private bool _compact;
    private bool _disposed;
    private float _viewportHeight;

    internal ModConfigNavigationRail(RectTransform parent, TextMeshProUGUI template,
        NativeFeatureRailVisualPrimitives visuals, IReadOnlyList<ModConfigTopPage> pages,
        int selected, Action<int> onSelected)
    {
        _visuals = visuals;
        _pages = pages;
        var viewport = ModConfigUiFactory.CreateRectObject("NavigationViewport", parent,
            Vector2.zero, Vector2.one, Color.white);
        _viewport = (RectTransform)viewport.transform;
        _viewport.offsetMin = new Vector2(0f, 30f);
        _viewport.offsetMax = new Vector2(0f, -30f);
        ModConfigNativeRailFactory.SkinPanel(viewport.GetComponent<Image>()!, visuals.FeatureRailBaseFrame, Color.white);
        viewport.AddComponent<RectMask2D>();
        _scroll = viewport.AddComponent<ScrollRect>();
        _scroll.horizontal = false;
        _scroll.vertical = true;
        _scroll.inertia = false;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 32f;
        var content = ModConfigUiFactory.CreateRectObject("NavigationContent", _viewport,
            new Vector2(0f, 1f), new Vector2(1f, 1f));
        _content = (RectTransform)content.transform;
        _content.pivot = new Vector2(0.5f, 1f);
        _content.sizeDelta = new Vector2(0f, ContentHeight);
        _scroll.viewport = _viewport;
        _scroll.content = _content;
        _up = ModConfigUiFactory.CreateButton("NavigationUp", parent,
            new Vector2(0.08f, 1f), new Vector2(0.92f, 1f), template, "▲", () => ScrollBy(-PageStep));
        ModConfigUiFactory.SetTopAnchoredHeight((RectTransform)_up.transform, 2f, 26f);
        _down = ModConfigUiFactory.CreateButton("NavigationDown", parent,
            new Vector2(0.08f, 0f), new Vector2(0.92f, 0f), template, "▼", () => ScrollBy(PageStep));
        var downRect = (RectTransform)_down.transform;
        downRect.pivot = new Vector2(0.5f, 0f);
        downRect.anchoredPosition = new Vector2(0f, 2f);
        downRect.sizeDelta = new Vector2(0f, 26f);
        if (!ModConfigNativeRailFactory.TryBuild(_content, pages, selected, _rows,
                template, visuals, onSelected, out var reason))
            throw new InvalidOperationException("Native Mods rail construction failed: " + reason);
        SetPages(pages, selected, reveal: false);
    }

    private float ContentHeight => ModConfigNativeRailFactory.Padding * 2f +
        _pages.Count * ModConfigNativeRailFactory.RowPitch;
    private float ViewportHeight => Math.Max(0f, _viewport.rect.height);
    private float PageStep => Math.Max(ModConfigNativeRailFactory.RowPitch, ViewportHeight * 0.8f);
    internal ModConfigTopPageKind SelectedKind => _pages[_selected].Kind;
    internal float ScrollOffset => Math.Max(0f, _content.anchoredPosition.y);

    internal void SetPages(IReadOnlyList<ModConfigTopPage> pages, int selected, bool reveal)
    {
        if (pages.Count != _rows.Count) throw new InvalidOperationException("Navigation catalog changed without rebuilding its owner.");
        _pages = pages;
        _selected = Math.Max(0, Math.Min(selected, pages.Count - 1));
        for (var index = 0; index < _rows.Count; index++)
        {
            var row = _rows[index];
            row.GetComponent<Image>()!.sprite = index == _selected
                ? _visuals.FeatureRailActiveFrame : _visuals.FeatureRailBaseFrame;
            row.GetComponentInChildren<TextMeshProUGUI>(true)!.text = pages[index].Label;
        }
        RestoreScroll(ScrollOffset);
        if (reveal) RevealSelected();
    }

    internal void RefreshLayout(bool compact)
    {
        if (_disposed) return;
        var heightChanged = Math.Abs(_viewportHeight - ViewportHeight) > 0.5f;
        _viewportHeight = ViewportHeight;
        if (_compact != compact || heightChanged)
        {
            _compact = compact;
            foreach (var row in _rows)
            {
                row.GetComponentInChildren<TextMeshProUGUI>(true)!.gameObject.SetActive(!compact);
                var icon = (RectTransform)row.transform.GetChild(0);
                icon.anchorMin = compact ? new Vector2(0.16f, 0.14f) : new Vector2(0.04f, 0.16f);
                icon.anchorMax = compact ? new Vector2(0.84f, 0.86f) : new Vector2(0.24f, 0.84f);
            }
            RestoreScroll(ScrollOffset);
            RevealSelected();
        }
        RefreshOverflow();
    }

    internal void RestoreScroll(float offset)
    {
        var clamped = ModSettingsLayout.ClampScrollOffset(offset, ContentHeight, ViewportHeight);
        _content.anchoredPosition = new Vector2(0f, clamped);
        _scroll.verticalNormalizedPosition = ModSettingsLayout.CalculateVerticalNormalizedPosition(
            clamped, ContentHeight, ViewportHeight);
        RefreshOverflow();
    }

    private void RevealSelected()
    {
        if (ViewportHeight <= 0f) return;
        var top = ModConfigNativeRailFactory.Padding + _selected * ModConfigNativeRailFactory.RowPitch;
        var bottom = top + ModConfigNativeRailFactory.RowHeight;
        if (top < ScrollOffset) RestoreScroll(top);
        else if (bottom > ScrollOffset + ViewportHeight) RestoreScroll(bottom - ViewportHeight);
    }

    private void ScrollBy(float distance) => RestoreScroll(ScrollOffset + distance);

    private void RefreshOverflow()
    {
        var overflow = ContentHeight > ViewportHeight + 0.5f;
        _up.gameObject.SetActive(overflow);
        _down.gameObject.SetActive(overflow);
        _up.interactable = ScrollOffset > 0.5f;
        _down.interactable = ScrollOffset < ContentHeight - ViewportHeight - 0.5f;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ModConfigUiFactory.ClearObjects(_rows);
        UnityEngine.Object.Destroy(_up.gameObject);
        UnityEngine.Object.Destroy(_down.gameObject);
        UnityEngine.Object.Destroy(_viewport.gameObject);
    }
}
