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

    /// <summary>One stretch of consecutive paths that hang off one parent, and that parent.</summary>
    public readonly struct Run
    {
        public Run(int start, int count, string prefix)
        {
            Start = start;
            Count = count;
            Prefix = prefix ?? string.Empty;
        }

        public int Start { get; }
        public int Count { get; }
        public string Prefix { get; }
    }

    /// <summary>
    /// Hierarchy-ordered paths split into the stretches that share a parent.
    /// </summary>
    /// <remarks>
    /// One prefix over a whole page is only as deep as its most distant pair of rows, so a page
    /// spanning three panels factored out a canvas name and left every row of every panel carrying
    /// its panel's whole ancestry — the eleven rows of one glyph list repeated some 150 identical
    /// characters, and two more stretches beside them did the same. The shared part of a stretch is
    /// what the rows in it actually share, so it is computed over the stretch: siblings arrive
    /// adjacent in hierarchy order, so a stretch is the consecutive run under one parent, and each
    /// of its rows is left holding only its own last segment.
    /// </remarks>
    public static IReadOnlyList<Run> Runs(IReadOnlyList<string> paths)
    {
        var result = new List<Run>();
        if (paths is null || paths.Count == 0) return result;
        var start = 0;
        var parent = Parent(paths[0]);
        for (var index = 1; index <= paths.Count; index++)
        {
            if (index < paths.Count &&
                string.Equals(Parent(paths[index]), parent, StringComparison.Ordinal))
            {
                continue;
            }
            result.Add(new Run(start, index - start, parent));
            if (index >= paths.Count) break;
            start = index;
            parent = Parent(paths[index]);
        }
        return result;
    }

    /// <summary>The ancestry a path's siblings share: everything above its own last segment.</summary>
    private static string Parent(string path)
    {
        var cut = path is null ? -1 : path.LastIndexOf('/');
        return cut <= 0 ? string.Empty : path!.Substring(0, cut);
    }

    /// <summary>
    /// Whether one live path is the element a caller addressed, given that a catalog page hands out
    /// only the part its own shared prefix did not already say. Which prefix that was depends on
    /// which page the row came from, so a row addresses its element by the tail it was handed —
    /// matched at a segment boundary, so <c>Row[1]</c> never answers for <c>NarrowRow[1]</c>.
    /// </summary>
    public static bool Addresses(string path, string requested) =>
        string.Equals(path, requested, StringComparison.Ordinal) ||
        path.EndsWith("/" + requested, StringComparison.Ordinal);
}
