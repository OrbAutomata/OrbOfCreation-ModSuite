using System;
using System.Linq;
using OrbAutomata;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

/// <summary>Discovers the audited native top bar and creates its reversible Mods host.</summary>
internal static class ModConfigNativeNavigationInstaller
{
    internal const string ButtonObjectName = "OrbModConfig.ModsButton";

    public static bool TryInstall(
        string panelObjectName,
        out ModConfigNativeNavigationHost? host,
        out string reason)
    {
        host = null;
        reason = string.Empty;
        var buttonType = Type.GetType("UIViewRadioButton, Assembly-CSharp", false);
        if (buttonType is null)
        {
            reason = "UIViewRadioButton type unavailable";
            return false;
        }

        var nativeButtons = Resources.FindObjectsOfTypeAll(buttonType)
            .OfType<Component>()
            .Where(component => NativeObjectPath.Build(component)
                .IndexOf("MainContentContainer/TopBar/ViewRadio", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();
        var cloneSource = nativeButtons
            .Where(component => component.gameObject.activeInHierarchy)
            .OrderBy(component => component.transform.GetSiblingIndex())
            .LastOrDefault()
            ?? nativeButtons.OrderBy(component => component.transform.GetSiblingIndex()).LastOrDefault();
        if (cloneSource is null)
        {
            reason = "native top-bar button unavailable";
            return false;
        }

        var screenContent = GameObject.Find("Canvas/ContentArea/MainContentContainer/ScreenContent");
        if (screenContent is null)
        {
            reason = "ScreenContent container unavailable";
            return false;
        }

        var buttonParent = cloneSource.transform.parent;
        if (buttonParent is null)
        {
            reason = "native top-bar button has no parent";
            return false;
        }

        RemoveOwnedChild(buttonParent, ButtonObjectName);
        RemoveOwnedChild(screenContent.transform, panelObjectName);
        GameObject? buttonObject = null;
        try
        {
            var inactiveSprite = NativeViewAdapter.ReadSprite(cloneSource, "baseImage");
            var activeSprite = NativeViewAdapter.ReadSprite(cloneSource, "activeImage");
            var nativeViews = nativeButtons
                .Select(NativeViewAdapter.ReadView)
                .Where(view => view is not null)
                .Cast<object>()
                .Distinct()
                .ToList();
            foreach (var viewType in nativeViews.Select(view => view.GetType()).Distinct())
            {
                if (NativeViewAdapter.TryValidateViewType(viewType, out reason)) continue;
                reason = "native view contract unavailable: " + reason;
                return false;
            }
            buttonObject = UnityEngine.Object.Instantiate(cloneSource.gameObject, buttonParent, false);
            buttonObject.name = ButtonObjectName;
            buttonObject.SetActive(true);
            buttonObject.transform.SetSiblingIndex(buttonParent.childCount - 1);
            RemoveNativeComponents(buttonObject, buttonType);

            var button = buttonObject.GetComponent<Button>();
            if (button is null)
            {
                reason = "cloned top-bar object has no Unity Button";
                UnityEngine.Object.Destroy(buttonObject);
                return false;
            }

            button.onClick.RemoveAllListeners();
            ConfiguredIntentButtonVisualOwnership.Claim(button);
            var label = buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label is null)
            {
                reason = "cloned top-bar button has no TextMeshPro label";
                UnityEngine.Object.Destroy(buttonObject);
                return false;
            }

            label.text = "Mods";
            var image = buttonObject.GetComponent<Image>();
            if (image is not null && inactiveSprite is not null) image.sprite = inactiveSprite;
            host = new ModConfigNativeNavigationHost(
                buttonObject,
                button,
                image,
                inactiveSprite,
                activeSprite,
                buttonType,
                buttonParent,
                screenContent.transform,
                label,
                nativeViews);
            return true;
        }
        catch (Exception ex)
        {
            if (buttonObject is not null) UnityEngine.Object.Destroy(buttonObject);
            reason = ex.GetBaseException().Message;
            return false;
        }
    }

    private static void RemoveNativeComponents(GameObject buttonObject, Type nativeButtonType)
    {
        var clonedGameComponent = buttonObject.GetComponent(nativeButtonType);
        if (clonedGameComponent is Behaviour clonedBehaviour) clonedBehaviour.enabled = false;
        if (clonedGameComponent is not null) UnityEngine.Object.Destroy(clonedGameComponent);

        foreach (var tooltip in buttonObject.GetComponents<Component>()
                     .Where(component => component.GetType().Name == "HoverTooltip"))
        {
            if (tooltip is Behaviour tooltipBehaviour) tooltipBehaviour.enabled = false;
            UnityEngine.Object.Destroy(tooltip);
        }
    }

    private static void RemoveOwnedChild(Transform parent, string objectName)
    {
        for (var index = parent.childCount - 1; index >= 0; index--)
        {
            var child = parent.GetChild(index);
            if (string.Equals(child.name, objectName, StringComparison.Ordinal))
                UnityEngine.Object.Destroy(child.gameObject);
        }
    }
}
