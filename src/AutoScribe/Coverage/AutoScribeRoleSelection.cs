using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// Owns the semantic Auto Scribe role-list format. The worker parses it once per configuration
/// generation; actions deliberately carry no role key and never consult a newer configuration.
/// </summary>
internal static class AutoScribeRoleSelection
{
    internal const string NoneValue = "none";

    /// <summary>
    /// The selected roles: <see langword="null"/> for every producible role, empty for none. A value
    /// naming any role Auto Scribe cannot write selects nothing, never the roles that happened to match.
    /// </summary>
    internal static PublicationTable<ScrollRoleKey>? ParsePublication(
        string serialized,
        PublicationTable<AutoScribeRoleDescriptor> roles)
    {
        if (roles is null) throw new ArgumentNullException(nameof(roles));
        if (string.IsNullOrWhiteSpace(serialized)) return null;
        if (IsNone(serialized) || FindUnwritableRole(serialized, roles) is not null)
            return PublicationTable<ScrollRoleKey>.Empty;

        var selected = new ScrollRoleKey[roles.Count];
        var count = 0;
        foreach (var entry in serialized.Split(','))
        {
            if (!TryFindWritable(entry.Trim(), roles, out var role) ||
                Contains(selected, count, role)) continue;
            selected[count++] = role;
        }
        Array.Sort(selected, 0, count);
        return PublicationTable<ScrollRoleKey>.Create(selected, count);
    }

    /// <summary>Why the configured roles select nothing by mistake, or <see langword="null"/>.</summary>
    internal static string? ConfigurationProblem(string serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized) || IsNone(serialized)) return null;
        var unwritable = FindUnwritableRole(serialized, AutoScribeIdentityCatalog.Audited.Roles);
        return unwritable is null
            ? null
            : $"AutoScribe/Roles names '{unwritable}', which is not a Scroll role Auto Scribe writes, " +
                "so Auto Scribe writes nothing.";
    }

    private static bool IsNone(string serialized) =>
        string.Equals(serialized.Trim(), NoneValue, StringComparison.OrdinalIgnoreCase);

    private static string? FindUnwritableRole(
        string serialized,
        PublicationTable<AutoScribeRoleDescriptor> roles)
    {
        foreach (var entry in serialized.Split(','))
        {
            var normalized = entry.Trim();
            if (normalized.Length != 0 && !TryFindWritable(normalized, roles, out _)) return normalized;
        }
        return null;
    }

    private static bool TryFindWritable(
        string key,
        PublicationTable<AutoScribeRoleDescriptor> roles,
        out ScrollRoleKey role)
    {
        for (var index = 0; index < roles.Count; index++)
        {
            if (!roles[index].IsProducible ||
                !string.Equals(roles[index].Key.Value, key, StringComparison.Ordinal)) continue;
            role = roles[index].Key;
            return true;
        }
        role = default;
        return false;
    }

    internal static bool Contains(
        PublicationTable<ScrollRoleKey>? selected,
        ScrollRoleKey role)
    {
        if (selected is null) return true;
        var rows = selected.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var comparison = rows[middle].CompareTo(role);
            if (comparison == 0) return true;
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        return false;
    }

    private static bool Contains(ScrollRoleKey[] values, int count, ScrollRoleKey value)
    {
        for (var index = 0; index < count; index++)
            if (values[index] == value) return true;
        return false;
    }
}
