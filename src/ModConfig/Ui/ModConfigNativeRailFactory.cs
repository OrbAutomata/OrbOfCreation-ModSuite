using System;
using System.Collections.Generic;
using OrbAutomata;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

/// <summary>Builds suite-owned navigation using audited static native assets.</summary>
internal static class ModConfigNativeRailFactory
{
    internal const float RowHeight = 54f;
    internal const float RowPitch = 62f;
    internal const float Padding = 8f;

    public static bool TryCapture(out NativeFeatureRailVisualPrimitives? primitives, out string reason) =>
        NativeViewAdapter.TryCaptureFeatureRailVisuals(out primitives, out reason);

    public static void SkinPanel(Image image, Sprite sprite, Color color)
    {
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = true;
    }

    public static bool TryBuild(
        RectTransform parent,
        IReadOnlyList<ModConfigTopPage> pages,
        int selected,
        ICollection<GameObject> owned,
        TextMeshProUGUI template,
        NativeFeatureRailVisualPrimitives primitives,
        Action<int> onSelected,
        out string reason)
    {
        reason = string.Empty;
        try
        {
            for (var index = 0; index < pages.Count; index++)
            {
                var page = pages[index];
                if (!NativeFeatureIconResolver.TryResolve(page.Icon, primitives, out var iconSprite, out var iconReason))
                {
                    reason = $"rail entry '{page.Label}' has no audited native icon: {iconReason}";
                    ModConfigUiFactory.ClearObjects(owned);
                    return false;
                }
                var captured = index;
                var buttonObject = ModConfigUiFactory.CreateRectObject(
                    "Rail." + page.Label, parent, new Vector2(0f, 1f), new Vector2(1f, 1f), Color.white);
                owned.Add(buttonObject);
                var rect = (RectTransform)buttonObject.transform;
                ModConfigUiFactory.SetTopAnchoredHeight(rect, Padding + index * RowPitch, RowHeight);
                rect.offsetMin = new Vector2(Padding, rect.offsetMin.y);
                rect.offsetMax = new Vector2(-Padding, rect.offsetMax.y);
                var button = buttonObject.AddComponent<Button>();
                button.onClick.AddListener(() => onSelected(captured));
                ConfiguredIntentButtonVisualOwnership.Claim(button);
                SkinPanel(buttonObject.GetComponent<Image>()!,
                    index == selected ? primitives.FeatureRailActiveFrame : primitives.FeatureRailBaseFrame,
                    Color.white);
                var iconObject = ModConfigUiFactory.CreateRectObject(
                    "Icon", buttonObject.transform, new Vector2(0.04f, 0.16f), new Vector2(0.24f, 0.84f));
                var icon = iconObject.AddComponent<Image>();
                icon.sprite = iconSprite;
                icon.color = Color.white;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                ModConfigUiFactory.CreateText("Label", buttonObject.transform,
                    new Vector2(0.29f, 0.08f), new Vector2(0.96f, 0.92f), template,
                    page.Label, TextAlignmentOptions.MidlineLeft, 0.68f);
                buttonObject.AddComponent<HoverTooltip>().Setup(new ModConfigRailTooltip(page, iconSprite!));
            }
            return true;
        }
        catch (Exception exception)
        {
            ModConfigUiFactory.ClearObjects(owned);
            reason = exception.GetBaseException().Message;
            return false;
        }
    }
}
