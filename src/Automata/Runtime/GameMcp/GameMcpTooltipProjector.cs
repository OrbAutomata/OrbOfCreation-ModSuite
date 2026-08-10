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
        AppendTooltip(primary, lines, visited, ref truncated);
        AppendTooltips(authoredNested, lines, visited, ref truncated);
        AppendTooltips(inspectedPanels, lines, visited, ref truncated);
        DropRepeatedTail(lines);
        if (truncated) lines.Add("Tooltip truncated after 200 lines.");
        return new JObject { ["text"] = string.Join("\n", lines) };
    }

    /// <summary>
    /// Drops a trailing block that repeats the block immediately before it. Adjacent duplicate
    /// lines were already suppressed one at a time, which never caught a panel that painted its
    /// whole last block twice — half a tooltip body, saying nothing the half above it had not.
    /// </summary>
    private static void DropRepeatedTail(List<string> lines)
    {
        for (var length = lines.Count / 2; length >= 2; length--)
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
