using System;
using System.Collections.Generic;
using OrbAutomata;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace OrbModConfig;

internal static class ModConfigPalette
{
    public static readonly Color Background = new(0.055f, 0.065f, 0.085f, 0.985f);
    public static readonly Color Bar = new(0.09f, 0.105f, 0.135f, 1f);
    public static readonly Color Button = new(0.16f, 0.18f, 0.23f, 1f);
    public static readonly Color ActiveButton = new(0.38f, 0.22f, 0.12f, 1f);
    public static readonly Color Row = new(0.075f, 0.087f, 0.11f, 0.96f);
    public static readonly Color Invalid = new(0.95f, 0.42f, 0.35f, 1f);
}

internal static class ModConfigUiFactory
{
    private static NativeFeatureRailVisualPrimitives? _nativeVisuals;

    internal static void UseNativeVisuals(NativeFeatureRailVisualPrimitives primitives) =>
        _nativeVisuals = primitives ?? throw new ArgumentNullException(nameof(primitives));

    internal static NativeFeatureRailVisualPrimitives NativeVisuals =>
        _nativeVisuals ??
        throw new InvalidOperationException(
            "Native Mods visuals must be captured before constructing controls.");

    public static GameObject CreateRectObject(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Color? color = null)
    {
        var types = color.HasValue
            ? new[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(Image) }
            : new[] { typeof(RectTransform) };
        var gameObject = new GameObject(name, types);
        gameObject.transform.SetParent(parent, false);
        var rect = (RectTransform)gameObject.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        if (color.HasValue) gameObject.GetComponent<Image>()!.color = color.Value;
        return gameObject;
    }

    public static TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        TextMeshProUGUI template,
        string value,
        TextAlignmentOptions alignment,
        float sizeScale,
        TextOverflowModes overflowMode = TextOverflowModes.Ellipsis)
    {
        var gameObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        gameObject.transform.SetParent(parent, false);
        var rect = (RectTransform)gameObject.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var text = gameObject.GetComponent<TextMeshProUGUI>()!;
        text.font = template.font;
        text.fontSharedMaterial = template.fontSharedMaterial;
        text.fontSize = Math.Max(12f, template.fontSize * sizeScale);
        text.color = template.color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = overflowMode;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    public static Button CreateButton(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        TextMeshProUGUI template,
        string label,
        UnityAction action,
        bool active = false)
    {
        var gameObject = CreateRectObject(
            name,
            parent,
            anchorMin,
            anchorMax,
            active ? ModConfigPalette.ActiveButton : ModConfigPalette.Button);
        var button = gameObject.AddComponent<Button>();
        SetButtonActive(button, active);
        ConfiguredIntentButtonVisualOwnership.Claim(button);
        CreateText(
            "Label",
            gameObject.transform,
            new Vector2(0.03f, 0.05f),
            new Vector2(0.97f, 0.95f),
            template,
            label,
            TextAlignmentOptions.Midline,
            0.68f);
        button.onClick.AddListener(action);
        return button;
    }

    public static void SetButtonActive(Button button, bool active)
    {
        var image = button.GetComponent<Image>()!;
        var native = NativeVisuals;
        image.sprite = active
            ? native.FeatureRailActiveFrame
            : native.FeatureRailBaseFrame;
        image.color = Color.white;
    }

    public static void SetTopAnchoredHeight(RectTransform rect, float topInset, float height)
    {
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -topInset);
        rect.sizeDelta = new Vector2(0f, height);
    }

    public static void ClearObjects(ICollection<GameObject> objects)
    {
        foreach (var gameObject in objects)
        {
            if (gameObject is not null) UnityEngine.Object.Destroy(gameObject);
        }
        objects.Clear();
    }
}
