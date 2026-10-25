using System;
using System.Reflection;
using System.Linq;
using OrbModConfig;
using UnityEngine;

namespace OrbAutomata;

/// <summary>
/// One icon vocabulary for feature quick controls and the Mods rail. Every borrowed sprite comes
/// from a declared native accessor or an exact audited top-bar capture.
/// </summary>
internal enum NativeFeatureIcon
{
    Runtime, Strategist, General, AutoBuy, AutoCast, AutoConcept, AutoHarvest, Mentor,
    AutoItems, AutoScribe, Advanced, Settings, Unknown,
}

internal static class NativeFeatureIconResolver
{
    internal static bool TryResolve(
        string pageLabel,
        NativeFeatureRailVisualPrimitives? capturedRail,
        out Sprite? icon,
        out string reason)
    {
        var identity = ForFeature(pageLabel);
        return identity == NativeFeatureIcon.Unknown
            ? Unknown(pageLabel, out icon, out reason)
            : TryResolve(identity, capturedRail, out icon, out reason);
    }

    internal static NativeFeatureIcon ForFeature(string feature) => feature switch
    {
        "Runtime" => NativeFeatureIcon.Runtime,
        "Strategist" => NativeFeatureIcon.Strategist,
        "General" => NativeFeatureIcon.General,
        "Advanced" => NativeFeatureIcon.Advanced,
        _ => AutomataFeatureCatalog.All.FirstOrDefault(contribution =>
            string.Equals(contribution.PageLabel, feature, StringComparison.Ordinal))?.IconIdentity
            ?? NativeFeatureIcon.Unknown,
    };

    internal static bool TryResolve(
        NativeFeatureIcon identity,
        NativeFeatureRailVisualPrimitives? capturedRail,
        out Sprite? icon,
        out string reason) => identity switch
    {
        NativeFeatureIcon.Runtime => FromCaptured(capturedRail?.RuntimeIcon, "ScreenTime", out icon, out reason),
        NativeFeatureIcon.General or NativeFeatureIcon.Settings => FromCaptured(
            capturedRail?.GeneralIcon, "ScreenMagic", out icon, out reason),
        NativeFeatureIcon.AutoBuy => TryGetTooltipableIcon(
            "GetGlobalStructureType", "StructureTypeSO", out icon, out reason),
        NativeFeatureIcon.AutoCast => TryGetTooltipableIcon(
            "GetCastingSpeedAttr", "AttributeSO", out icon, out reason),
        NativeFeatureIcon.AutoConcept or NativeFeatureIcon.Strategist => FromCaptured(
            capturedRail?.ConceptIcon, "ScreenScholar", out icon, out reason),
        NativeFeatureIcon.AutoHarvest => TryGetTooltipableIcon(
            "GetHarvestSpeedAttr", "AttributeSO", out icon, out reason),
        NativeFeatureIcon.Mentor => TryGetTooltipableIcon(
            "GetMasteryExpAttr", "AttributeSO", out icon, out reason),
        NativeFeatureIcon.AutoItems => FromCaptured(capturedRail?.WorldIcon, "ScreenWorld", out icon, out reason),
        NativeFeatureIcon.AutoScribe => FromCaptured(capturedRail?.WorkshopIcon, "ScreenWorkshop", out icon, out reason),
        NativeFeatureIcon.Advanced => FromCaptured(capturedRail?.AdvancedIcon, "ScreenAlchemy", out icon, out reason),
        _ => Unknown(identity.ToString(), out icon, out reason),
    };

    private static bool FromCaptured(
        Sprite? captured,
        string auditedItemName,
        out Sprite? icon,
        out string reason)
    {
        if (captured is not null)
        {
            icon = captured;
            reason = string.Empty;
            return true;
        }
        return NativeViewAdapter.TryCaptureNamedTopBarIcon(auditedItemName, out icon, out reason);
    }

    private static bool TryGetTooltipableIcon(
        string accessorName,
        string returnTypeName,
        out Sprite? icon,
        out string reason)
    {
        icon = null;
        reason = string.Empty;
        try
        {
            var globals = Type.GetType("GlobalVariables, Assembly-CSharp", false);
            var tooltipable = Type.GetType("TooltipableObject, Assembly-CSharp", false);
            var returnType = Type.GetType(returnTypeName + ", Assembly-CSharp", false);
            if (globals is null || tooltipable is null || returnType is null)
            {
                reason =
                    $"GlobalVariables.{accessorName}() type binding failed: expected " +
                    $"{returnTypeName} deriving from TooltipableObject; one or more native types " +
                    "are unavailable";
                return false;
            }
            const BindingFlags flags =
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var accessor = globals.GetMethod(
                accessorName, flags, null, Type.EmptyTypes, null);
            if (accessor is null)
            {
                reason =
                    $"GlobalVariables.{accessorName}() binding failed: expected public/static or " +
                    $"non-public/static {returnTypeName} {accessorName}(), actual <missing>";
                return false;
            }
            if (accessor.ReturnType != returnType)
            {
                reason =
                    $"GlobalVariables.{accessorName}() return type check failed: expected " +
                    $"{returnType.FullName}, actual {accessor.ReturnType.FullName}";
                return false;
            }
            if (!tooltipable.IsAssignableFrom(returnType))
            {
                reason =
                    $"{returnType.FullName} base-type check for GlobalVariables.{accessorName}() " +
                    $"failed: expected assignable to {tooltipable.FullName}, actual base " +
                    $"{returnType.BaseType?.FullName ?? "<null>"}";
                return false;
            }
            var attribute = accessor.Invoke(null, null);
            if (attribute is null || !returnType.IsInstanceOfType(attribute))
            {
                reason =
                    $"GlobalVariables.{accessorName}() value type check failed: expected " +
                    $"{returnType.FullName}, actual {attribute?.GetType().FullName ?? "<null>"}";
                return false;
            }
            var getIcon = tooltipable.GetMethod(
                "GetIcon",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (getIcon?.ReturnType != typeof(Sprite))
            {
                reason =
                    "TooltipableObject.GetIcon() return type check failed: expected " +
                    $"UnityEngine.Sprite, actual {getIcon?.ReturnType.FullName ?? "<missing>"}";
                return false;
            }
            icon = getIcon.Invoke(attribute, null) as Sprite;
            if (icon is not null) return true;
            reason = $"GlobalVariables.{accessorName}().GetIcon() returned no sprite";
            return false;
        }
        catch (Exception ex)
        {
            var root = ex.GetBaseException();
            reason =
                $"GlobalVariables.{accessorName}() -> TooltipableObject.GetIcon() capture failed: " +
                $"{root.GetType().FullName}: {root.Message}";
            return false;
        }
    }

    private static bool Unknown(
        string pageLabel,
        out Sprite? icon,
        out string reason)
    {
        icon = null;
        reason = $"unrecognized consolidated page '{pageLabel}'";
        return false;
    }
}
