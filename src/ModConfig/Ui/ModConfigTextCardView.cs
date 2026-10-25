using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

/// <summary>Native-skinned, wrapping text card shared by read-only Mods pages.</summary>
internal sealed class ModConfigTextCardView : IDisposable
{
    private const float RowGap = 8f;
    private const float RowInset = 12f;
    private const float MinimumRowHeight = 112f;
    private readonly GameObject _root;
    private readonly RectTransform _rect;
    private readonly Image _frame;
    private readonly TextMeshProUGUI _title;
    private readonly TextMeshProUGUI _body;
    private string _titleValue = string.Empty;
    private string _bodyValue = string.Empty;
    private float _measuredWidth;
    private float _height = MinimumRowHeight;

    public ModConfigTextCardView(RectTransform parent, TextMeshProUGUI template)
    {
        _root = ModConfigUiFactory.CreateRectObject(
            "TextCard",
            parent,
            new Vector2(0.01f, 1f),
            new Vector2(0.99f, 1f),
            ModConfigPalette.Row);
        _rect = (RectTransform)_root.transform;
        _rect.pivot = new Vector2(0.5f, 1f);
        _frame = _root.GetComponent<Image>()!;
        ModConfigNativeRailFactory.SkinPanel(
            _frame,
            ModConfigUiFactory.NativeVisuals.FeatureRailBaseFrame,
            ModConfigPalette.Row);
        _title = ModConfigUiFactory.CreateText(
            "Title",
            _root.transform,
            new Vector2(0.018f, 1f),
            new Vector2(0.982f, 1f),
            template,
            string.Empty,
            TextAlignmentOptions.TopLeft,
            0.78f,
            TextOverflowModes.Overflow);
        _body = ModConfigUiFactory.CreateText(
            "Body",
            _root.transform,
            new Vector2(0.018f, 1f),
            new Vector2(0.982f, 1f),
            template,
            string.Empty,
            TextAlignmentOptions.TopLeft,
            0.54f,
            TextOverflowModes.Overflow);
        ModConfigUiFactory.SetTopAnchoredHeight((RectTransform)_title.transform, RowInset, 30f);
    }

    public int RenderGeneration { get; private set; }

    public float Layout(
        string title, string body, Color color,
        float contentWidth, float topOffset, int siblingIndex, int renderGeneration)
    {
        RenderGeneration = renderGeneration;
        var changed = !string.Equals(_titleValue, title, StringComparison.Ordinal) ||
            !string.Equals(_bodyValue, body, StringComparison.Ordinal);
        _titleValue = title;
        _bodyValue = body;
        _frame.color = color;
        var width = Math.Max(1f, contentWidth * 0.98f * 0.964f);
        if (changed || ModSettingsLayout.DescriptionWidthChanged(_measuredWidth, width))
        {
            _title.text = title;
            _body.text = body;
            var titleHeight = Math.Max(30f, _title.GetPreferredValues(title, width, 0f).y);
            var bodyTop = RowInset + titleHeight + 2f;
            var preferred = _body.GetPreferredValues(body, width, 0f).y;
            _height = Math.Max(MinimumRowHeight, bodyTop + preferred + RowInset + RowGap);
            _rect.sizeDelta = new Vector2(0f, _height - RowGap);
            ModConfigUiFactory.SetTopAnchoredHeight((RectTransform)_title.transform, RowInset, titleHeight);
            ModConfigUiFactory.SetTopAnchoredHeight(
                (RectTransform)_body.transform, bodyTop, Math.Max(1f, preferred));
            _measuredWidth = width;
        }
        _rect.anchoredPosition = new Vector2(0f, -topOffset);
        _root.transform.SetSiblingIndex(siblingIndex);
        return _height;
    }

    public void Dispose() => UnityEngine.Object.Destroy(_root);
}
