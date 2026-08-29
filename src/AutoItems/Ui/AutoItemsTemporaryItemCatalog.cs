using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using UnityEngine;

namespace OrbAutomata;

internal readonly record struct AutoItemsTemporaryItemFamily(
    Guid TypeId,
    AutoItemsConsumableFamily SupportedFamily,
    string DisplayName);

internal readonly record struct AutoItemsTemporaryItemOption(
    Guid ItemId,
    AutoItemsConsumableFamily ResolvedOperation,
    IReadOnlyList<AutoItemsTemporaryItemFamily> Families,
    string DisplayName,
    int Stock,
    Sprite Icon)
{
    internal string FamilyDisplay
    {
        get
        {
            var value = string.Empty;
            for (var index = 0; index < Families.Count; index++)
            {
                if (value.Length != 0) value += " · ";
                value += Families[index].DisplayName;
            }
            return value;
        }
    }
}

internal sealed class AutoItemsTemporaryItemCatalogSnapshot
{
    private AutoItemsTemporaryItemCatalogSnapshot(
        IReadOnlyList<AutoItemsTemporaryItemOption> options,
        string failureReason)
    {
        Options = options;
        FailureReason = failureReason;
    }

    internal IReadOnlyList<AutoItemsTemporaryItemOption> Options { get; }
    internal string FailureReason { get; }
    internal bool IsAvailable => FailureReason.Length == 0;

    internal static AutoItemsTemporaryItemCatalogSnapshot Available(
        IReadOnlyList<AutoItemsTemporaryItemOption> options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        var copy = new AutoItemsTemporaryItemOption[options.Count];
        for (var index = 0; index < copy.Length; index++) copy[index] = options[index];
        return new AutoItemsTemporaryItemCatalogSnapshot(copy, string.Empty);
    }

    internal static AutoItemsTemporaryItemCatalogSnapshot Failed(string reason)
    {
        var normalized = (reason ?? string.Empty).Trim();
        if (normalized.Length == 0) throw new ArgumentException("A discovery failure reason is required.", nameof(reason));
        return new AutoItemsTemporaryItemCatalogSnapshot(
            Array.Empty<AutoItemsTemporaryItemOption>(),
            normalized);
    }
}

/// <summary>
/// Captures display-only facts for the temporary-item picker on the Unity main thread. Native item
/// objects never leave this call; only immutable facts and each item's audited icon asset do.
/// </summary>
internal static class AutoItemsTemporaryItemCatalog
{
    internal static AutoItemsTemporaryItemCatalogSnapshot Capture(
        EntityIdentityCatalogSnapshot? identities = null)
    {
        try
        {
            if (!AutoItemsTemporaryItemCatalogBindings.TryCreate(out var bindings, out var reason))
                return AutoItemsTemporaryItemCatalogSnapshot.Failed(reason);

            var native = bindings!;
            var catalog = identities ?? EntityIdentityCatalogPublication.Current;
            var options = new List<AutoItemsTemporaryItemOption>();
            var seen = new HashSet<Guid>();
            foreach (var entry in native.Entries)
            {
                if (entry is null || entry.GetType() != native.ConsumableType)
                    return AutoItemsTemporaryItemCatalogSnapshot.Failed(
                        "ConsumableSO.All contained an entry with the wrong exact native type.");
                if (native.Visible.GetValue(entry) is not bool visible)
                    return AutoItemsTemporaryItemCatalogSnapshot.Failed(
                        "ConsumableSO.visible did not contain a Boolean.");
                if (!visible) continue;

                var itemId = Invoke<Guid>(native.ItemGuid, entry);
                if (itemId == Guid.Empty || !seen.Add(itemId))
                    return AutoItemsTemporaryItemCatalogSnapshot.Failed(
                        "The discovered consumable catalog contained an empty or duplicate stable UUID.");

                var families = ReadFamilies(entry, itemId, native, catalog);
                var supportedFamilies = new AutoItemsConsumableFamilySet();
                for (var index = 0; index < families.Count; index++)
                {
                    var candidate = families[index].SupportedFamily;
                    if (candidate == AutoItemsConsumableFamily.Unknown) continue;
                    if (supportedFamilies.TryAdd(candidate)) continue;
                    return AutoItemsTemporaryItemCatalogSnapshot.Failed(
                        $"Discovered consumable {EntityIdentityFormatter.Format(itemId, catalog)} repeated supported family {candidate}.");
                }
                if (supportedFamilies.Count == 0) continue;
                if (!supportedFamilies.TryResolveExecutionFamily(out var operation))
                    return AutoItemsTemporaryItemCatalogSnapshot.Failed(
                        $"Discovered consumable {EntityIdentityFormatter.Format(itemId, catalog)} has incoherent supported family " +
                        $"memberships [{supportedFamilies.Describe()}].");
                if (!AutoItemsConsumableFamilies.IsTemporary(operation)) continue;

                var identity = EntityIdentityFormatter.Describe(itemId, catalog);
                if (!identity.HasName)
                    return AutoItemsTemporaryItemCatalogSnapshot.Failed(
                        $"Discovered temporary item {EntityIdentityFormatter.Format(itemId, catalog)} has no live catalog name.");
                if (native.Quantity.GetValue(entry) is not int stock || stock < 0)
                    return AutoItemsTemporaryItemCatalogSnapshot.Failed(
                        $"Discovered temporary item {EntityIdentityFormatter.Format(itemId, catalog)} returned invalid current stock.");
                if (native.GetIcon.Invoke(entry, Array.Empty<object>()) is not Sprite icon)
                    return AutoItemsTemporaryItemCatalogSnapshot.Failed(
                        $"Discovered temporary item {EntityIdentityFormatter.Format(itemId, catalog)} returned no audited native icon.");

                options.Add(new AutoItemsTemporaryItemOption(
                    itemId,
                    operation,
                    OrderFamiliesForDisplay(families, operation),
                    identity.Name,
                    stock,
                    icon));
            }

            options.Sort(Compare);
            return AutoItemsTemporaryItemCatalogSnapshot.Available(options);
        }
        catch (Exception ex) when (AutoItemsReflectionAccess.IsExpectedFailure(ex))
        {
            return AutoItemsTemporaryItemCatalogSnapshot.Failed(
                "The discovered temporary-item catalog could not be read: " +
                ex.GetBaseException().Message);
        }
    }

    private static IReadOnlyList<AutoItemsTemporaryItemFamily> ReadFamilies(
        object item,
        Guid itemId,
        AutoItemsTemporaryItemCatalogBindings bindings,
        EntityIdentityCatalogSnapshot identities)
    {
        if (bindings.Families.GetValue(item) is not IEnumerable entries)
            throw new InvalidOperationException(
                $"ConsumableSO.consumableTypes was unavailable for {EntityIdentityFormatter.Format(itemId, identities)}.");

        var families = new List<AutoItemsTemporaryItemFamily>();
        var seen = new HashSet<Guid>();
        foreach (var entry in entries)
        {
            if (entry is null || entry.GetType() != bindings.FamilyType)
                throw new InvalidOperationException(
                    $"ConsumableSO.consumableTypes contained the wrong exact native type for {EntityIdentityFormatter.Format(itemId, identities)}.");
            var typeId = Invoke<Guid>(bindings.FamilyGuid, entry);
            if (typeId == Guid.Empty || !seen.Add(typeId))
                throw new InvalidOperationException(
                    $"ConsumableSO.consumableTypes contained an empty or duplicate family UUID for {EntityIdentityFormatter.Format(itemId, identities)}.");
            var identity = EntityIdentityFormatter.Describe(
                typeId,
                identities);
            if (!identity.HasName)
                throw new InvalidOperationException(
                    $"Consumable family {EntityIdentityFormatter.Format(typeId, identities)} has no live catalog name for {EntityIdentityFormatter.Format(itemId, identities)}.");
            families.Add(new AutoItemsTemporaryItemFamily(
                typeId,
                AutoItemsConsumableFamilies.FromTypeId(typeId),
                identity.Name));
        }
        return families;
    }

    private static int Compare(
        AutoItemsTemporaryItemOption left,
        AutoItemsTemporaryItemOption right)
    {
        var family = left.ResolvedOperation.CompareTo(right.ResolvedOperation);
        if (family != 0) return family;
        var name = string.Compare(
            left.DisplayName,
            right.DisplayName,
            StringComparison.OrdinalIgnoreCase);
        return name != 0 ? name : left.ItemId.CompareTo(right.ItemId);
    }

    private static IReadOnlyList<AutoItemsTemporaryItemFamily> OrderFamiliesForDisplay(
        IReadOnlyList<AutoItemsTemporaryItemFamily> families,
        AutoItemsConsumableFamily operation)
    {
        if (families.Count < 2 || families[0].SupportedFamily == operation) return families;

        var ordered = new AutoItemsTemporaryItemFamily[families.Count];
        var cursor = 0;
        for (var index = 0; index < families.Count; index++)
        {
            if (families[index].SupportedFamily != operation) continue;
            ordered[cursor++] = families[index];
        }
        for (var index = 0; index < families.Count; index++)
        {
            if (families[index].SupportedFamily == operation) continue;
            ordered[cursor++] = families[index];
        }
        return ordered;
    }

    private static T Invoke<T>(MethodInfo method, object target) =>
        method.Invoke(target, Array.Empty<object>()) is T value
            ? value
            : throw new InvalidOperationException(
                $"{method.DeclaringType?.Name}.{method.Name} did not return {typeof(T).Name}.");
}
