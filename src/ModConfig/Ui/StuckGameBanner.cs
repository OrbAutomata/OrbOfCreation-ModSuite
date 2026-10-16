using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

/// <summary>
/// The suite-owned strip across the top of the game's root canvas that tells the player the game itself
/// stopped advancing. It is click-through, built from the Mods panel's captured label font, and
/// touches nothing native beyond parenting under that canvas.
/// </summary>
internal sealed class StuckGameBanner : IDisposable
{
    internal const string ObjectName = "OrbModSuite.StuckGameBanner";
    internal const float Height = 96f;
    internal static readonly Color Background = new(0.42f, 0.07f, 0.07f, 0.96f);

    private readonly Transform _panelParent;
    private readonly TextMeshProUGUI _labelTemplate;
    private GameObject? _root;
    private TextMeshProUGUI? _text;

    internal StuckGameBanner(Transform panelParent, TextMeshProUGUI labelTemplate)
    {
        _panelParent = panelParent ?? throw new ArgumentNullException(nameof(panelParent));
        _labelTemplate = labelTemplate ?? throw new ArgumentNullException(nameof(labelTemplate));
    }

    internal string? Shown { get; private set; }

    internal void Show(string? notice)
    {
        if (notice == Shown) return;
        Shown = notice;
        if (notice is null)
        {
            Dispose();
            return;
        }
        if (_root == null) Create();
        _text!.text = notice;
    }

    public void Dispose()
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);
        _root = null;
        _text = null;
    }

    private void Create()
    {
        var host = _panelParent;
        while (host.parent != null) host = host.parent;
        _root = ModConfigUiFactory.CreateRectObject(
            ObjectName,
            host,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            Background);
        _root.GetComponent<Image>()!.raycastTarget = false;
        var rect = (RectTransform)_root.transform;
        ModConfigUiFactory.SetTopAnchoredHeight(rect, 0f, Height);
        rect.SetAsLastSibling();
        _text = ModConfigUiFactory.CreateText(
            "Notice",
            _root.transform,
            new Vector2(0.03f, 0.08f),
            new Vector2(0.97f, 0.92f),
            _labelTemplate,
            string.Empty,
            TextAlignmentOptions.Midline,
            1f);
        _text.color = Color.white;
    }
}
