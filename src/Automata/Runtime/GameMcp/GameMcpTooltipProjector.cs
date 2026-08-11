#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Text;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;

namespace OrbAutomata.GameMcp;

/// <summary>Renders the screen's tooltip words without serializing Unity's node graph.</summary>
internal static class GameMcpTooltipProjector
{
    private const int MaximumLines = 200;

    internal static JObject Project(
        ITooltipable primary,
        IEnumerable<ITooltipable>? authoredNested,
        IEnumerable<ITooltipable>? inspectedPanels)
    {
        if (primary is null) throw new ArgumentNullException(nameof(primary));
        var lines = new List<string>();
        var visited = new HashSet<ITooltipable>(ReferenceComparer.Instance);
        var truncated = false;
        AppendSource(primary, lines, visited, ref truncated);
        AppendSources(authoredNested, lines, visited, ref truncated);
        AppendSources(inspectedPanels, lines, visited, ref truncated);
        DropRepeatedTail(lines);
        if (truncated) lines.Add("Tooltip truncated after 200 lines.");
        return new JObject { ["text"] = string.Join("\n", lines) };
    }

    /// <summary>
    /// One of the graphs this body is built from, appended only if it says something the body does
    /// not already say.
    /// </summary>
    /// <remarks>
    /// An inspected panel paints the same entity the hovered element does, from its own object, so
    /// reference identity does not recognise it and the body carried every statistic twice — once
    /// beside its description and once as a bare value block, which was 40% of the response and the
    /// half with nothing in it. Whole-source is the level this can be judged at: dropping a repeated
    /// <em>line</em> would take the second statistic that happens to read <c>0</c> and leave its
    /// label with no value under it, while a source every line of which is already on the page adds
    /// nothing to remove.
    /// </remarks>
    private static void AppendSource(
        ITooltipable tooltip,
        List<string> lines,
        HashSet<ITooltipable> visited,
        ref bool truncated)
    {
        var start = lines.Count;
        AppendTooltip(tooltip, lines, visited, ref truncated);
        for (var index = start; index < lines.Count; index++)
        {
            var said = false;
            for (var earlier = 0; !said && earlier < start; earlier++)
                said = string.Equals(lines[earlier], lines[index], StringComparison.Ordinal);
            if (!said) return;
        }
        lines.RemoveRange(start, lines.Count - start);
    }

    private static void AppendSources(
        IEnumerable<ITooltipable>? tooltips,
        List<string> lines,
        HashSet<ITooltipable> visited,
        ref bool truncated)
    {
        if (tooltips is null) return;
        foreach (var tooltip in tooltips)
            if (tooltip is not null) AppendSource(tooltip, lines, visited, ref truncated);
    }

    /// <summary>
    /// Drops a trailing block that repeats the block immediately before it. Adjacent duplicate
    /// lines were already suppressed one at a time, which never caught a panel that painted its
    /// whole last block twice — half a tooltip body, saying nothing the half above it had not.
    /// </summary>
    /// <remarks>
    /// The smallest repeat wins, not the largest. Every candidate here already ends at the last
    /// line, so the choice is only how much of the closing text to call a repeat — and taking the
    /// largest let a page whose tail repeated at two scales lose half the body when one line of it
    /// would have done. A panel that genuinely paints two identical blocks at the end is
    /// indistinguishable from one that painted its last block twice, so the smallest match is the
    /// most this can honestly claim.
    /// </remarks>
    private static void DropRepeatedTail(List<string> lines)
    {
        for (var length = 2; length <= lines.Count / 2; length++)
        {
            var repeated = true;
            for (var offset = 0; repeated && offset < length; offset++)
            {
                repeated = string.Equals(
                    lines[lines.Count - length + offset],
                    lines[lines.Count - (2 * length) + offset],
                    StringComparison.Ordinal);
            }
            if (!repeated) continue;
            lines.RemoveRange(lines.Count - length, length);
            return;
        }
    }

    private static void AppendTooltips(
        IEnumerable<ITooltipable>? tooltips,
        List<string> lines,
        HashSet<ITooltipable> visited,
        ref bool truncated)
    {
        if (tooltips is null) return;
        foreach (var tooltip in tooltips)
            if (tooltip is not null) AppendTooltip(tooltip, lines, visited, ref truncated);
    }

    private static void AppendTooltip(
        ITooltipable tooltip,
        List<string> lines,
        HashSet<ITooltipable> visited,
        ref bool truncated)
    {
        if (lines.Count >= MaximumLines)
        {
            truncated = true;
            return;
        }
        if (!visited.Add(tooltip)) return;
        AppendLine(lines, tooltip.GetName(), ref truncated);
        AppendLine(lines, tooltip.GetDisplayType(), ref truncated);
        AppendLine(lines, tooltip.GetDescription(), ref truncated);
        var primaryStart = lines.Count;
        AppendNodes(tooltip.GetTooltipNodes(), lines, visited, ref truncated);
        if (tooltip.HasAltTooltips())
        {
            var alternate = new List<string>();
            var alternateVisited = new HashSet<ITooltipable>(visited, ReferenceComparer.Instance);
            var alternateTruncated = false;
            AppendNodes(
                tooltip.GetAltTooltipNodes(), alternate, alternateVisited, ref alternateTruncated);
            if (!SameLines(lines, primaryStart, alternate))
            {
                for (var index = 0; index < alternate.Count; index++)
                    AppendLine(lines, alternate[index], ref truncated);
                visited.UnionWith(alternateVisited);
            }
            if (alternateTruncated) truncated = true;
        }
    }

    private static void AppendNodes(
        IReadOnlyList<TooltipNode>? nodes,
        List<string> lines,
        HashSet<ITooltipable> visited,
        ref bool truncated)
    {
        if (nodes is null) return;
        for (var index = 0; index < nodes.Count; index++)
        {
            if (lines.Count >= MaximumLines)
            {
                truncated = true;
                return;
            }
            var node = nodes[index];
            if (node is null) continue;
            try
            {
                AppendLine(lines, node.textFn is null ? node.text : node.textFn(), ref truncated);
            }
            catch (Exception exception)
            {
                AppendLine(lines, "Tooltip text unavailable: " +
                    exception.GetBaseException().Message, ref truncated);
            }
            if (node.tooltipable is not null)
                AppendTooltip(node.tooltipable, lines, visited, ref truncated);
            AppendTooltips(node.subTooltips, lines, visited, ref truncated);
            AppendNodes(node.children, lines, visited, ref truncated);
        }
    }

    private static void AppendLine(List<string> lines, string? text, ref bool truncated)
    {
        var plain = GameMcpTextFormatter.Plain(text ?? string.Empty).Trim();
        if (plain.Length == 0) return;
        if (lines.Count > 0 && string.Equals(lines[^1], plain, StringComparison.Ordinal)) return;
        if (lines.Count >= MaximumLines)
        {
            truncated = true;
            return;
        }
        lines.Add(plain);
    }

    private static bool SameLines(
        IReadOnlyList<string> primary,
        int primaryStart,
        IReadOnlyList<string> alternate)
    {
        if (primary.Count - primaryStart != alternate.Count) return false;
        for (var index = 0; index < alternate.Count; index++)
            if (!string.Equals(
                    primary[primaryStart + index],
                    alternate[index],
                    StringComparison.Ordinal))
                return false;
        return true;
    }

    private sealed class ReferenceComparer : IEqualityComparer<ITooltipable>
    {
        internal static readonly ReferenceComparer Instance = new();
        public bool Equals(ITooltipable? x, ITooltipable? y) => ReferenceEquals(x, y);
        public int GetHashCode(ITooltipable obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
#endif
