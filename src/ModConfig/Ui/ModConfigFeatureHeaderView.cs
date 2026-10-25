using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

/// <summary>Measures feature health text and updates the owned controls without replacing them.</summary>
internal sealed class ModConfigFeatureHeaderView : IDisposable
{
    private const float Padding = 8f;
    private const float Gap = 6f;
    private const float RowGap = 7f;
    private const float ButtonHeight = 44f;
    private readonly GameObject _root;
    private readonly ModConfigFeatureCommand _command;
    private readonly TextMeshProUGUI _title;
    private readonly TextMeshProUGUI _status;
    private readonly TextMeshProUGUI _hint;
    private readonly Button _button;
    private readonly TextMeshProUGUI _buttonLabel;
    private ModConfigFeatureCommandPresentation _presentation;
    private float _measuredWidth;

    internal ModConfigFeatureHeaderView(RectTransform content, TextMeshProUGUI template,
        ModConfigFeatureCommand command, float topOffset, Action onToggle)
    {
        _command = command;
        _presentation = command.Presentation;
        _root = ModConfigUiFactory.CreateRectObject("Feature." + command.DisplayName, content,
            new Vector2(0.01f, 1f), new Vector2(0.99f, 1f), ModConfigPalette.Row);
        var rootRect = (RectTransform)_root.transform;
        rootRect.pivot = new Vector2(0.5f, 1f);
        rootRect.anchoredPosition = new Vector2(0f, -topOffset - 4f);
        _title = Text("Title", template, command.DisplayName, 0.025f, 0.74f, 0.88f);
        _status = Text("Status", template, _presentation.StatusText, 0.025f, 0.975f, 0.56f);
        _hint = Text("StagingHint", template,
            "This control applies immediately. Settings below require Apply.", 0.025f, 0.975f, 0.52f);
        _button = ModConfigUiFactory.CreateButton("ImmediateMode", _root.transform,
            new Vector2(0.77f, 1f), new Vector2(0.975f, 1f), template,
            _presentation.ButtonLabel, () => { command.Toggle(); onToggle(); }, _presentation.IsActive);
        _buttonLabel = _button.GetComponentInChildren<TextMeshProUGUI>(true)!;
        Layout(content.rect.width);
    }

    internal float Height { get; private set; }

    internal float Refresh(float contentWidth)
    {
        var presentation = _command.Presentation;
        if (_presentation == presentation &&
            !ModSettingsLayout.DescriptionWidthChanged(_measuredWidth, contentWidth)) return 0f;
        _status.text = presentation.StatusText;
        _buttonLabel.text = presentation.ButtonLabel;
        ModConfigUiFactory.SetButtonActive(_button, presentation.IsActive);
        _presentation = presentation;
        var oldHeight = Height;
        Layout(contentWidth);
        return Height - oldHeight;
    }

    public void Dispose() => UnityEngine.Object.Destroy(_root);

    private TextMeshProUGUI Text(string name, TextMeshProUGUI template, string value,
        float left, float right, float scale) => ModConfigUiFactory.CreateText(name, _root.transform,
            new Vector2(left, 1f), new Vector2(right, 1f), template, value,
            TextAlignmentOptions.TopLeft, scale, TextOverflowModes.Overflow);

    private void Layout(float contentWidth)
    {
        _measuredWidth = contentWidth;
        var rowWidth = !float.IsNaN(contentWidth) && !float.IsInfinity(contentWidth) && contentWidth >= 320f
            ? contentWidth * 0.98f : 880f;
        var titleHeight = Math.Max(28f, PreferredHeight(_title, rowWidth * (0.74f - 0.025f)));
        var statusHeight = Math.Max(24f, PreferredHeight(_status, rowWidth * 0.95f));
        var hintHeight = Math.Max(20f, PreferredHeight(_hint, rowWidth * 0.95f));
        var statusTop = Padding + Math.Max(titleHeight, ButtonHeight) + Gap;
        var hintTop = statusTop + statusHeight + Gap;
        Height = Math.Max(112f, hintTop + hintHeight + Padding + RowGap);
        ((RectTransform)_root.transform).sizeDelta = new Vector2(0f, Height - RowGap);
        ModConfigUiFactory.SetTopAnchoredHeight((RectTransform)_title.transform, Padding, titleHeight);
        ModConfigUiFactory.SetTopAnchoredHeight((RectTransform)_button.transform, Padding, ButtonHeight);
        ModConfigUiFactory.SetTopAnchoredHeight((RectTransform)_status.transform, statusTop, statusHeight);
        ModConfigUiFactory.SetTopAnchoredHeight((RectTransform)_hint.transform, hintTop, hintHeight);
    }

    private static float PreferredHeight(TextMeshProUGUI text, float width)
    {
        var height = text.GetPreferredValues(text.text, width, 0f).y;
        return float.IsNaN(height) || float.IsInfinity(height) || height < 0f ? 0f : height + 2f;
    }
}
