using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.Strategy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

/// <summary>A read-only view over the latest native-free strategist publication.</summary>
internal sealed class StrategistPage : IDisposable
{
    private readonly RectTransform _content;
    private readonly ScrollRect _scroll;
    private readonly TextMeshProUGUI _template;
    private readonly IStrategistBulletinSource _source;
    private readonly List<ModConfigTextCardView> _cards = new();
    private StrategistBulletin? _bulletin;
    private string _reason = string.Empty;
    private float _width;
    private float _rememberedScrollOffset;
    private bool _visible;
    private bool _disposed;

    internal StrategistPage(RectTransform content, ScrollRect scroll,
        TextMeshProUGUI labelTemplate, IStrategistBulletinSource source)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _scroll = scroll ?? throw new ArgumentNullException(nameof(scroll));
        _template = labelTemplate ?? throw new ArgumentNullException(nameof(labelTemplate));
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    internal float ScrollOffset => _visible ? Math.Max(0f, _content.anchoredPosition.y) : _rememberedScrollOffset;

    internal void RestoreScroll(float offset)
    {
        _rememberedScrollOffset = ModSettingsLayout.ClampScrollOffset(offset, float.MaxValue, 0f);
        if (_visible) Render(_bulletin, _reason, _rememberedScrollOffset);
    }

    internal void Show()
    {
        if (_disposed) return;
        var offset = _visible ? _content.anchoredPosition.y : _rememberedScrollOffset;
        _visible = true;
        Render(_source.ReadStrategistBulletin(), _source.UnavailableReason, offset);
    }

    internal void Refresh()
    {
        if (_disposed || !_visible) return;
        var bulletin = _source.ReadStrategistBulletin();
        var reason = _source.UnavailableReason;
        if (ReferenceEquals(_bulletin, bulletin) && string.Equals(_reason, reason, StringComparison.Ordinal) &&
            !ModSettingsLayout.DescriptionWidthChanged(_width, _content.rect.width)) return;
        Render(bulletin, reason, _content.anchoredPosition.y);
    }

    internal void Hide()
    {
        if (!_visible) return;
        _rememberedScrollOffset = Math.Max(0f, _content.anchoredPosition.y);
        _visible = false;
        Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _visible = false;
        Clear();
    }

    private void Render(StrategistBulletin? bulletin, string reason, float requestedOffset)
    {
        // Reset advice scroll at a lifecycle clear so the waiting state is immediately visible.
        if (_bulletin is not null && bulletin is null) requestedOffset = 0f;
        _bulletin = bulletin;
        _reason = reason;
        _width = _content.rect.width;
        var cards = StrategistPresentation.Build(bulletin, reason);
        while (_cards.Count > cards.Count)
        {
            _cards[_cards.Count - 1].Dispose();
            _cards.RemoveAt(_cards.Count - 1);
        }
        var top = 4f;
        for (var i = 0; i < cards.Count; i++)
        {
            if (i == _cards.Count) _cards.Add(new ModConfigTextCardView(_content, _template));
            top += _cards[i].Layout(cards[i].Title, cards[i].Body, ModConfigPalette.Row,
                _width, top, i, 0);
        }
        var height = Math.Max(1f, top);
        _content.sizeDelta = new Vector2(0f, height);
        var viewport = _scroll.viewport;
        var viewportHeight = viewport is not null ? viewport.rect.height : 0f;
        var offset = ModSettingsLayout.ClampScrollOffset(requestedOffset, height, viewportHeight);
        _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, offset);
        _scroll.verticalNormalizedPosition = ModSettingsLayout.CalculateVerticalNormalizedPosition(offset, height, viewportHeight);
    }

    private void Clear()
    {
        foreach (var card in _cards) card.Dispose();
        _cards.Clear();
    }
}
