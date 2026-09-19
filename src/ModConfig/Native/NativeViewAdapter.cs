using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

/// <summary>
/// Narrow reflection adapter for the audited native top-navigation contract.
/// Native view objects never escape the navigation host.
/// </summary>
internal static class NativeViewAdapter
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Type, NativeButtonContract> ButtonContracts = new();
    private static readonly Dictionary<Type, NativeViewContract> ViewContracts = new();
    public static bool TryCaptureButtonStateVisuals(
        out NativeButtonStateVisualPrimitives? primitives,
        out string reason)
    {
        primitives = null;
        reason = string.Empty;
        try
        {
            var viewButtonType = Type.GetType("UIViewRadioButton, Assembly-CSharp", false);
            if (viewButtonType is null)
            {
                reason = "native view-radio type unavailable";
                return false;
            }

            var allViewButtons = Resources.FindObjectsOfTypeAll(viewButtonType)
                .OfType<Component>()
                .OrderBy(component => NativeObjectPath.Build(component), StringComparer.Ordinal)
                .ToArray();
            var railCandidates = allViewButtons
                .Where(component => IsFeatureRailPath(NativeObjectPath.Build(component)))
                .ToArray();
            if (!TryReadFeatureRailFrames(
                    railCandidates,
                    out _,
                    out var inactiveFrame,
                    out var activeFrame,
                    out var frameReason))
            {
                reason = "audited inactive/active MainContentContainer/SubviewRadio frame pair unavailable: " +
                         frameReason + ". Candidate census: " +
                         BuildFeatureRailCandidateCensus(allViewButtons);
                return false;
            }

            primitives = new NativeButtonStateVisualPrimitives(inactiveFrame!, activeFrame!);
            return true;
        }
        catch (Exception ex)
        {
            reason = DescribeCaptureException(
                "UIViewRadioButton.baseImage/activeImage state-frame capture",
                ex);
            return false;
        }
    }

    public static bool TryCaptureFeatureRailVisuals(
        out NativeFeatureRailVisualPrimitives? primitives,
        out string reason)
    {
        primitives = null;
        reason = string.Empty;
        try
        {
            var viewButtonType = Type.GetType("UIViewRadioButton, Assembly-CSharp", false);
            if (viewButtonType is null)
            {
                reason = "native view-radio type unavailable";
                return false;
            }

            var allViewButtons = Resources.FindObjectsOfTypeAll(viewButtonType)
                .OfType<Component>()
                .OrderBy(component => NativeObjectPath.Build(component), StringComparer.Ordinal)
                .ToArray();
            var railCandidates = allViewButtons
                .Where(component => IsFeatureRailPath(NativeObjectPath.Build(component)))
                .ToArray();
            if (!TryReadFeatureRailFrames(
                    railCandidates,
                    out var railPrototype,
                    out var railBaseFrame,
                    out var railActiveFrame,
                    out var frameReason))
            {
                reason = "audited inactive-capable MainContentContainer/SubviewRadio frame unavailable: " +
                         frameReason + ". Candidate census: " +
                         BuildFeatureRailCandidateCensus(allViewButtons);
                return false;
            }

            var hasRuntimeIcon = TryReadNamedTopBarIcon(
                allViewButtons, "ScreenTime", out var runtimeIcon, out var runtimeReason);
            var hasGeneralIcon = TryReadNamedTopBarIcon(
                allViewButtons, "ScreenMagic", out var generalIcon, out var generalReason);
            var hasConceptIcon = TryReadNamedTopBarIcon(
                allViewButtons, "ScreenScholar", out var conceptIcon, out var conceptReason);
            var hasAdvancedIcon = TryReadNamedTopBarIcon(
                allViewButtons, "ScreenAlchemy", out var advancedIcon, out var advancedReason);
            var hasWorldIcon = TryReadNamedTopBarIcon(
                allViewButtons, "ScreenWorld", out var worldIcon, out var worldReason);
            var hasWorkshopIcon = TryReadNamedTopBarIcon(
                allViewButtons, "ScreenWorkshop", out var workshopIcon, out var workshopReason);
            if (!hasRuntimeIcon || !hasGeneralIcon || !hasConceptIcon || !hasAdvancedIcon ||
                !hasWorldIcon || !hasWorkshopIcon)
            {
                reason = "audited top-bar rail icon unavailable: " +
                         string.Join(
                             "; ",
                             new[]
                             {
                                 runtimeReason,
                                 generalReason,
                                 conceptReason,
                                 advancedReason,
                                 worldReason,
                                 workshopReason,
                             }
                                 .Where(value => !string.IsNullOrEmpty(value))) +
                         ". Candidate census: " +
                         BuildFeatureRailCandidateCensus(allViewButtons);
                return false;
            }

            primitives = new NativeFeatureRailVisualPrimitives(
                railPrototype!,
                railBaseFrame!,
                railActiveFrame!,
                runtimeIcon!,
                generalIcon!,
                conceptIcon!,
                advancedIcon!,
                worldIcon!,
                workshopIcon!);
            return true;
        }
        catch (Exception ex)
        {
            reason = DescribeCaptureException(
                "UIViewRadioButton rail frame and top-bar viewImage capture",
                ex);
            return false;
        }
    }

    internal static NativeUiStartupReadinessObservation ObserveTopBarStartupReadiness()
    {
        try
        {
            var viewButtonType = Type.GetType("UIViewRadioButton, Assembly-CSharp", false);
            if (viewButtonType is null)
                return new NativeUiStartupReadinessObservation(
                    NativeUiStartupReadinessKind.Mismatch,
                    "native view-radio type unavailable");

            var topBarCandidates = Resources.FindObjectsOfTypeAll(viewButtonType)
                .OfType<Component>()
                .Where(component => IsAuditedTopBarPath(NativeObjectPath.Build(component)))
                .OrderBy(component => NativeObjectPath.Build(component), StringComparer.Ordinal)
                .ToArray();
            var facts = new List<NativeTopBarCandidateFact>(
                NativeTopBarReadinessPolicy.RequiredItemNames.Count);
            var missing = new List<string>();
            var mismatches = new List<string>();
            foreach (var itemName in NativeTopBarReadinessPolicy.RequiredItemNames)
            {
                var matches = topBarCandidates
                    .Where(candidate => string.Equals(
                        ReadNativeItemName(candidate),
                        itemName,
                        StringComparison.Ordinal))
                    .ToArray();
                var hasIcon = matches.Length == 1 &&
                              (GetButtonContract(matches[0].GetType())
                                  .ViewImageField?.GetValue(matches[0]) as Image)?.sprite is not null;
                facts.Add(new NativeTopBarCandidateFact(itemName, matches.Length, hasIcon));
                if (matches.Length == 0)
                    missing.Add(itemName);
                else if (matches.Length != 1)
                    mismatches.Add(
                        $"{itemName}: expected one top-bar candidate, found {matches.Length}");
                else if (!hasIcon)
                    mismatches.Add($"{itemName}: viewImage sprite is null");
            }

            var kind = NativeTopBarReadinessPolicy.Classify(facts);
            var reason = kind switch
            {
                NativeUiStartupReadinessKind.Ready => string.Empty,
                NativeUiStartupReadinessKind.NotYetPresent =>
                    "required top-bar candidates are not yet present: " +
                    string.Join(", ", missing),
                _ => string.Join("; ", mismatches),
            };
            return new NativeUiStartupReadinessObservation(kind, reason);
        }
        catch (Exception ex)
        {
            return new NativeUiStartupReadinessObservation(
                NativeUiStartupReadinessKind.Mismatch,
                DescribeCaptureException("UIViewRadioButton top-bar readiness capture", ex));
        }
    }

    public static bool TryCaptureNamedTopBarIcon(
        string itemName,
        out Sprite? icon,
        out string reason)
    {
        icon = null;
        reason = string.Empty;
        try
        {
            var viewButtonType = Type.GetType("UIViewRadioButton, Assembly-CSharp", false);
            if (viewButtonType is null)
            {
                reason = "native view-radio type unavailable";
                return false;
            }
            var candidates = Resources.FindObjectsOfTypeAll(viewButtonType)
                .OfType<Component>()
                .OrderBy(component => NativeObjectPath.Build(component), StringComparer.Ordinal)
                .ToArray();
            if (TryReadNamedTopBarIcon(candidates, itemName, out icon, out reason))
                return true;
            reason += ". Candidate census: " + BuildFeatureRailCandidateCensus(candidates);
            return false;
        }
        catch (Exception ex)
        {
            reason = DescribeCaptureException(
                $"UIViewRadioButton.viewImage sprite capture for '{itemName}'",
                ex);
            return false;
        }
    }

    public static bool IsAlive(object? value)
    {
        if (value is null) return false;
        return value is not UnityEngine.Object unityObject || unityObject != null;
    }

    public static object? ReadView(Component component) =>
        GetButtonContract(component.GetType()).ViewField.GetValue(component);

    public static bool IsActive(object view)
    {
        try
        {
            return GetViewContract(view.GetType()).IsActive.Invoke(view, null) as bool? == true;
        }
        catch
        {
            return false;
        }
    }

    public static void SetActive(object view, bool active)
    {
        try
        {
            if (!IsAlive(view)) return;
            GetViewContract(view.GetType()).SetActive.Invoke(view, new object[] { active });
        }
        catch { }
    }

    /// <summary>
    /// Whether the game has unlocked this view, read from <c>ViewSO.IsAvailable()</c> — the same
    /// member the world publication reads for every view's <c>available</c>.
    /// </summary>
    public static bool IsAvailable(object view)
    {
        try
        {
            return GetViewContract(view.GetType()).IsAvailable.Invoke(view, null) as bool? == true;
        }
        catch
        {
            return false;
        }
    }

    public static Sprite? ReadSprite(Component component, string fieldName)
    {
        var contract = GetButtonContract(component.GetType());
        var field = string.Equals(fieldName, "baseImage", StringComparison.Ordinal)
            ? contract.InactiveSpriteField
            : string.Equals(fieldName, "activeImage", StringComparison.Ordinal)
                ? contract.ActiveSpriteField
                : null;
        return field?.GetValue(component) as Sprite;
    }

    internal static bool TryValidateViewType(Type type, out string reason)
    {
        try
        {
            GetViewContract(type);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.GetBaseException().Message;
            return false;
        }
    }

    internal static bool IsViewTypeCached(Type type)
    {
        lock (Gate) return ViewContracts.ContainsKey(type);
    }

    internal static bool IsFeatureRailPath(string path) =>
        path.StartsWith(
            "Canvas/ContentArea/MainContentContainer/SubviewRadio/",
            StringComparison.Ordinal) &&
        path.IndexOf(
            '/',
            "Canvas/ContentArea/MainContentContainer/SubviewRadio/".Length) < 0;

    internal static bool IsAuditedTopBarPath(string path)
    {
        const string parent =
            "Canvas/ContentArea/MainContentContainer/TopBar/ViewRadio/";
        if (!path.StartsWith(parent, StringComparison.Ordinal)) return false;
        return path.IndexOf('/', parent.Length) < 0;
    }

    private static NativeButtonContract GetButtonContract(Type type)
    {
        lock (Gate)
        {
            if (!ButtonContracts.TryGetValue(type, out var contract))
            {
                contract = NativeButtonContract.Create(type);
                ButtonContracts.Add(type, contract);
            }

            return contract;
        }
    }

    private static NativeViewContract GetViewContract(Type type)
    {
        lock (Gate)
        {
            if (!ViewContracts.TryGetValue(type, out var contract))
            {
                contract = NativeViewContract.Create(type);
                ViewContracts.Add(type, contract);
            }

            return contract;
        }
    }

    internal static bool TryReadFeatureRailFrames(
        IReadOnlyList<Component> candidates,
        out Component? prototype,
        out Sprite? baseFrame,
        out Sprite? activeFrame,
        out string reason)
    {
        prototype = null;
        baseFrame = null;
        activeFrame = null;
        if (candidates.Count == 0)
        {
            reason = "no candidate exists at the audited structural path";
            return false;
        }
        foreach (var candidate in candidates)
        {
            var contract = GetButtonContract(candidate.GetType());
            if (contract.ButtonImageField?.GetValue(candidate) is not Image buttonImage ||
                contract.InactiveSpriteField?.GetValue(candidate) is not Sprite candidateBase ||
                contract.ActiveSpriteField?.GetValue(candidate) is not Sprite candidateActive)
            {
                reason =
                    $"candidate '{NativeObjectPath.Build(candidate)}' is missing an audited visual field";
                return false;
            }
            if (buttonImage.gameObject != candidate.gameObject)
            {
                reason =
                    $"candidate '{NativeObjectPath.Build(candidate)}' does not own its button Image";
                return false;
            }
            if (prototype is null)
            {
                prototype = candidate;
                baseFrame = candidateBase;
                activeFrame = candidateActive;
                continue;
            }
            if (baseFrame != candidateBase || activeFrame != candidateActive)
            {
                reason =
                    $"candidate '{NativeObjectPath.Build(candidate)}' disagrees with the audited rail frame pair";
                return false;
            }
        }
        prototype = candidates
            .OrderBy(candidate => ReadNativeItemName(candidate), StringComparer.Ordinal)
            .First();
        reason = string.Empty;
        return true;
    }

    internal static bool TryReadNamedTopBarIcon(
        IReadOnlyList<Component> candidates,
        string itemName,
        out Sprite? icon,
        out string reason)
    {
        icon = null;
        var matches = candidates
            .Where(candidate => IsAuditedTopBarPath(NativeObjectPath.Build(candidate)))
            .Where(candidate => string.Equals(
                ReadNativeItemName(candidate),
                itemName,
                StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            reason = $"{itemName}: expected one top-bar candidate, found {matches.Length}";
            return false;
        }
        var contract = GetButtonContract(matches[0].GetType());
        icon = (contract.ViewImageField?.GetValue(matches[0]) as Image)?.sprite;
        if (icon is null)
        {
            reason = $"{itemName}: viewImage sprite is null";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static string ReadNativeItemName(Component candidate)
    {
        var item = GetButtonContract(candidate.GetType()).ViewField.GetValue(candidate);
        return item is UnityEngine.Object unityObject ? unityObject.name : item?.ToString() ?? string.Empty;
    }

    internal static string BuildFeatureRailCandidateCensus(IReadOnlyList<Component> candidates)
    {
        if (candidates.Count == 0) return "<no UIViewRadioButton objects>";
        var census = new StringBuilder();
        foreach (var candidate in candidates
                     .OrderBy(component => NativeObjectPath.Build(component), StringComparer.Ordinal))
        {
            if (census.Length > 0) census.Append(" || ");
            census.Append(DescribeFeatureRailCandidate(candidate));
        }
        return census.ToString();
    }

    private static string DescribeFeatureRailCandidate(Component candidate)
    {
        var path = NativeObjectPath.Build(candidate);
        try
        {
            var contract = GetButtonContract(candidate.GetType());
            var buttonImage = contract.ButtonImageField?.GetValue(candidate) as Image;
            var viewImage = contract.ViewImageField?.GetValue(candidate) as Image;
            var item = contract.ViewField.GetValue(candidate);
            var baseImage = contract.InactiveSpriteField?.GetValue(candidate) as Sprite;
            var activeImage = contract.ActiveSpriteField?.GetValue(candidate) as Sprite;
            return DescribeObject(candidate, path) +
                   $"; pathMatch={IsFeatureRailPath(path)}" +
                   $"; item={DescribeNativeItem(item)}" +
                   $"; buttonImage={Present(buttonImage)}" +
                   $"; buttonOwned={buttonImage is not null && buttonImage.gameObject == candidate.gameObject}" +
                   $"; viewImage={Present(viewImage)}" +
                   $"; viewIcon={Present(viewImage?.sprite)}" +
                   $"; baseImage={Present(baseImage)}" +
                   $"; activeImage={Present(activeImage)}";
        }
        catch (Exception ex)
        {
            return DescribeObject(candidate, path) +
                   "; inspectionError=" + ex.GetBaseException().Message;
        }
    }

    private static string DescribeObject(Component candidate, string path)
    {
        var scene = candidate.gameObject.scene;
        var sceneMembership = scene.IsValid()
            ? $"{scene.name}(loaded={scene.isLoaded})"
            : "<not in a loaded scene>";
        return $"path='{path}'; activeSelf={candidate.gameObject.activeSelf}; " +
               $"activeInHierarchy={candidate.gameObject.activeInHierarchy}; scene={sceneMembership}";
    }

    private static string Present(UnityEngine.Object? value) =>
        value is null || value == null ? "null" : "present";

    private static string DescribeNativeItem(object? item)
    {
        if (item is null) return "null";
        return item is UnityEngine.Object unityObject
            ? $"'{unityObject.name}' ({item.GetType().Name})"
            : $"'{item}' ({item.GetType().Name})";
    }

    private static FieldInfo? FindField(Type type, string fieldName)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(fieldName, flags);
            if (field is not null) return field;
        }

        return null;
    }

    private sealed class NativeButtonContract
    {
        private NativeButtonContract(
            FieldInfo viewField,
            FieldInfo? inactiveSpriteField,
            FieldInfo? activeSpriteField,
            FieldInfo? buttonImageField,
            FieldInfo? viewImageField,
            FieldInfo? viewTextField)
        {
            ViewField = viewField;
            InactiveSpriteField = inactiveSpriteField;
            ActiveSpriteField = activeSpriteField;
            ButtonImageField = buttonImageField;
            ViewImageField = viewImageField;
            ViewTextField = viewTextField;
        }

        public FieldInfo ViewField { get; }
        public FieldInfo? InactiveSpriteField { get; }
        public FieldInfo? ActiveSpriteField { get; }
        public FieldInfo? ButtonImageField { get; }
        public FieldInfo? ViewImageField { get; }
        public FieldInfo? ViewTextField { get; }

        public static NativeButtonContract Create(Type type) => new(
            FindField(type, "item") ??
                throw new MissingFieldException(type.FullName, "item"),
            RequireField(type, "baseImage", typeof(Sprite)),
            RequireField(type, "activeImage", typeof(Sprite)),
            RequireField(type, "buttonImage", typeof(Image)),
            RequireField(type, "viewImage", typeof(Image)),
            FindField(type, "viewText"));
    }

    private sealed class NativeViewContract
    {
        private NativeViewContract(
            MethodInfo isActive,
            MethodInfo setActive,
            MethodInfo isAvailable)
        {
            IsActive = isActive;
            SetActive = setActive;
            IsAvailable = isAvailable;
        }

        public MethodInfo IsActive { get; }
        public MethodInfo SetActive { get; }
        public MethodInfo IsAvailable { get; }

        public static NativeViewContract Create(Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var isActive = type.GetMethod("IsActive", flags, null, Type.EmptyTypes, null);
            if (isActive is null || isActive.ReturnType != typeof(bool))
                throw new MissingMethodException(type.FullName, "bool IsActive()");
            var setActive = type.GetMethod("SetActive", flags, null, new[] { typeof(bool) }, null);
            if (setActive is null || setActive.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, "void SetActive(bool)");
            var isAvailable = type.GetMethod("IsAvailable", flags, null, Type.EmptyTypes, null);
            if (isAvailable is null || isAvailable.ReturnType != typeof(bool))
                throw new MissingMethodException(type.FullName, "bool IsAvailable()");
            return new NativeViewContract(isActive, setActive, isAvailable);
        }
    }

    private static FieldInfo RequireField(Type type, string name, Type expectedType)
    {
        var field = FindField(type, name) ?? throw new MissingFieldException(type.FullName, name);
        if (field.FieldType != expectedType)
            throw new InvalidOperationException(
                $"{type.FullName}.{name} is {field.FieldType.FullName}, expected {expectedType.FullName}.");
        return field;
    }

    private static FieldInfo RequireField(Type type, string name) =>
        FindField(type, name) ?? throw new MissingFieldException(type.FullName, name);

    private static string DescribeCaptureException(string check, Exception exception)
    {
        var root = exception.GetBaseException();
        return $"{check} failed: {root.GetType().FullName}: {root.Message}";
    }
}
