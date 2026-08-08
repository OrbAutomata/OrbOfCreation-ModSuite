using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace OrbModConfig;

/// <summary>
/// Formats diagnostic Unity hierarchy paths without reflecting over game types.
/// This boundary is used only during native navigation discovery and setup.
/// </summary>
internal static class NativeObjectPath
{
    public static string Build(UnityEngine.Object instance)
    {
        if (instance is null) return string.Empty;

        var transform = instance switch
        {
            GameObject gameObject => gameObject.transform,
            Component component => component.transform,
            _ => null,
        };
        if (transform is null) return instance.name ?? instance.GetType().Name;

        var segments = new List<string>(8);
        for (var current = transform; current is not null && segments.Count < 64; current = current.parent)
        {
            if (!string.IsNullOrWhiteSpace(current.name)) segments.Add(current.name);
        }

        segments.Reverse();
        return string.Join("/", segments);
    }

    /// <summary>
    /// Builds a closed-world selector that remains unique when Unity has several identically named
    /// clone rows. The sibling index is native hierarchy state, not a caller supplied reflection
    /// token, and is stable for the lifetime of the currently published screen catalog.
    /// </summary>
    public static string BuildIndexed(UnityEngine.Object instance)
    {
        if (instance is null) return string.Empty;

        var transform = instance switch
        {
            GameObject gameObject => gameObject.transform,
            Component component => component.transform,
            _ => null,
        };
        if (transform is null) return instance.name ?? instance.GetType().Name;

        var segments = new List<string>(8);
        for (var current = transform; current is not null && segments.Count < 64; current = current.parent)
        {
            if (string.IsNullOrWhiteSpace(current.name)) continue;
            segments.Add(
                current.name + "[" +
                current.GetSiblingIndex().ToString(CultureInfo.InvariantCulture) + "]");
        }

        segments.Reverse();
        return string.Join("/", segments);
    }

    /// <summary>
    /// One element's indexed selector and its screen-order key, from a single walk of the ancestry.
    /// </summary>
    /// <remarks>
    /// The two strings answer different questions — which element this is, and where on the screen
    /// it sits — and a catalog needs both for every element it lists. Built separately they cost two
    /// walks of the same chain each time, and a caller that then rebuilds the path to print it costs
    /// a third. The walk is the expensive part, so it happens once and hands back both.
    /// </remarks>
    public readonly struct Placement
    {
        internal Placement(string path, string orderKey)
        {
            Path = path;
            OrderKey = orderKey;
        }

        /// <summary>The indexed selector <see cref="BuildIndexed"/> would return.</summary>
        public string Path { get; }

        /// <summary>
        /// The sibling-index key screen order sorts on. Zero-padded per segment so an ordinal
        /// comparison orders numerically, which is what a screen reads like top to bottom.
        /// </summary>
        public string OrderKey { get; }
    }

    /// <summary>Both hierarchy keys for one element, from one walk of its ancestry.</summary>
    public static Placement Locate(UnityEngine.Object instance)
    {
        if (instance is null) return new Placement(string.Empty, string.Empty);

        var transform = instance switch
        {
            GameObject gameObject => gameObject.transform,
            Component component => component.transform,
            _ => null,
        };
        if (transform is null)
            return new Placement(instance.name ?? instance.GetType().Name, string.Empty);

        var segments = new List<string>(8);
        var order = new List<string>(8);
        for (var current = transform; current is not null; current = current.parent)
        {
            order.Add(current.GetSiblingIndex().ToString("D6", CultureInfo.InvariantCulture));
            if (segments.Count >= 64 || string.IsNullOrWhiteSpace(current.name)) continue;
            segments.Add(
                current.name + "[" +
                current.GetSiblingIndex().ToString(CultureInfo.InvariantCulture) + "]");
        }

        segments.Reverse();
        order.Reverse();
        return new Placement(string.Join("/", segments), string.Join("/", order));
    }

    /// <summary>
    /// The leading segments every listed path shares, held to a proper prefix so each path keeps at
    /// least one segment of its own. A screen's selectors descend from one canvas, so this is the
    /// part a page would otherwise repeat verbatim on every row.
    /// </summary>
    public static string CommonPrefix(IReadOnlyList<string> paths)
    {
        if (paths is null || paths.Count == 0) return string.Empty;

        var shared = paths[0].Split('/');
        var common = shared.Length;
        var shortest = shared.Length;
        for (var index = 1; index < paths.Count; index++)
        {
            var candidate = paths[index].Split('/');
            if (candidate.Length < shortest) shortest = candidate.Length;
            if (candidate.Length < common) common = candidate.Length;
            for (var segment = 0; segment < common; segment++)
            {
                if (string.Equals(shared[segment], candidate[segment], StringComparison.Ordinal))
                    continue;
                common = segment;
                break;
            }
            if (common == 0) return string.Empty;
        }

        if (common >= shortest) common = shortest - 1;
        return common <= 0 ? string.Empty : string.Join("/", shared, 0, common);
    }

    /// <summary>
    /// The part of a path a shared prefix does not already say.
    /// </summary>
    public static string Relative(string path, string prefix) =>
        prefix.Length > 0 && path.Length > prefix.Length &&
        path.StartsWith(prefix, StringComparison.Ordinal) && path[prefix.Length] == '/'
            ? path.Substring(prefix.Length + 1)
            : path;
}
