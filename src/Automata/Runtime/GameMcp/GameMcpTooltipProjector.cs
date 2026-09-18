#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Text;
using OrbModding.Common;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;

namespace OrbAutomata.GameMcp;

/// <summary>Renders the screen's tooltip words without serializing Unity's node graph.</summary>
internal static class GameMcpTooltipProjector
{
    private const int MaximumLines = 200;

    /// <summary>
    /// How far this walk follows the graph, and how much of it it visits, before it gives up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The line budget above is not a bound on the walk: a subgraph whose nodes carry empty or
    /// repeated text spends none of it, and a graph that mints a fresh wrapper object at every level
    /// defeats <see cref="WalkBounds"/>'s reference identity as well. One tooltip read of that shape
    /// took the Unity main thread and several gigabytes with it, so the walk needs a bound that does
    /// not depend on the text it produces. Neither number is a setting.
    /// </para>
    /// <para>
    /// The game itself renders one tooltipable's own node tree and hands <c>TooltipNode.tooltipable</c>
    /// and its sub-tooltips to <c>HoverTooltip.Setup</c> as the target of the player's next hover
    /// (<c>UITooltipNode.Setup</c>), so nothing the player sees at once is nested at this depth:
    /// every level past the first is a link this projector follows on the reader's behalf. Sixty-four
    /// is many times the longest such chain the game's own tooltip builders compose, and two thousand
    /// visits is ten times a line budget the suite already treats as more tooltip than any caller
    /// wants.
    /// </para>
    /// </remarks>
    private const int MaximumDepth = 64;
    private const int MaximumVisits = 2_000;

    /// <summary>
    /// The tooltip's words, or <c>false</c> when the walk hit <see cref="MaximumDepth"/> or
    /// <see cref="MaximumVisits"/> and was abandoned.
    /// </summary>
    /// <remarks>
    /// What was collected before a bound trips is not the tooltip's first part: a walk that spends
    /// its budget inside one repeating subgraph emits nothing at all, and the name and description it
    /// did emit read exactly like a short, complete tooltip. Answering with that would present an
    /// unfinished read as the read. The 200-line budget is the other case and stays what it is — the
    /// walk finished, the body is the tooltip's own first two hundred lines, and the last line says
    /// so.
    /// </remarks>
    internal static bool TryProject(
        ITooltipable primary,
        IEnumerable<ITooltipable>? authoredNested,
        IEnumerable<ITooltipable>? inspectedPanels,
        out JObject details)
    {
        if (primary is null) throw new ArgumentNullException(nameof(primary));
        var lines = new List<string>();
        var bounds = new WalkBounds();
        var truncated = false;
        AppendSource(primary, lines, bounds, ref truncated);
        AppendSources(authoredNested, lines, bounds, ref truncated);
        AppendSources(inspectedPanels, lines, bounds, ref truncated);
        details = new JObject();
        if (bounds.Exceeded) return false;
        DropRepeatedTail(lines);
        if (truncated) lines.Add("Tooltip truncated after 200 lines.");
        details["text"] = string.Join("\n", lines);
        return true;
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
        WalkBounds bounds,
        ref bool truncated)
    {
        var start = lines.Count;
        AppendTooltip(tooltip, lines, bounds, ref truncated);
        if (SaysNothingNew(lines, start, lines, start, lines.Count))
            lines.RemoveRange(start, lines.Count - start);
    }

    /// <summary>
    /// Whether every line of a candidate block already appears in the body before
    /// <paramref name="bodyLength"/> — the one rule this projector drops a block by.
    /// </summary>
    /// <remarks>
    /// Judged per block rather than per line on purpose: dropping a repeated <em>line</em> would
    /// take the second statistic that happens to read <c>0</c> and leave its label with no value
    /// under it, while a block every line of which is already on the page adds nothing to remove.
    /// </remarks>
    private static bool SaysNothingNew(
        IReadOnlyList<string> lines,
        int bodyLength,
        IReadOnlyList<string> candidate,
        int candidateStart,
        int candidateEnd)
    {
        for (var index = candidateStart; index < candidateEnd; index++)
        {
            var said = false;
            for (var earlier = 0; !said && earlier < bodyLength; earlier++)
                said = string.Equals(lines[earlier], candidate[index], StringComparison.Ordinal);
            if (!said) return false;
        }
        return true;
    }

    private static void AppendSources(
        IEnumerable<ITooltipable>? tooltips,
        List<string> lines,
        WalkBounds bounds,
        ref bool truncated)
    {
        if (tooltips is null) return;
        foreach (var tooltip in tooltips)
            if (tooltip is not null) AppendSource(tooltip, lines, bounds, ref truncated);
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
        WalkBounds bounds,
        ref bool truncated)
    {
        if (tooltips is null) return;
        foreach (var tooltip in tooltips)
            if (tooltip is not null) AppendTooltip(tooltip, lines, bounds, ref truncated);
    }

    private static void AppendTooltip(
        ITooltipable tooltip,
        List<string> lines,
        WalkBounds bounds,
        ref bool truncated)
    {
        if (lines.Count >= MaximumLines)
        {
            truncated = true;
            return;
        }
        if (!bounds.Spend()) return;
        if (!bounds.Remember(tooltip)) return;
        if (!bounds.Descend()) return;
        AppendLine(lines, tooltip.GetName(), ref truncated);
        AppendLine(lines, tooltip.GetDisplayType(), ref truncated);
        AppendLine(lines, tooltip.GetDescription(), ref truncated);
        AppendNodes(tooltip.GetTooltipNodes(), lines, bounds, ref truncated);
        if (tooltip.HasAltTooltips()) AppendAlternate(tooltip, lines, bounds, ref truncated);
        bounds.Ascend();
    }

    /// <summary>
    /// The alt block is one more block, judged by the one rule.
    /// </summary>
    /// <remarks>
    /// Comparing it to the primary block as a whole sequence was too narrow: a resource pill's alt
    /// paints the bare values a reader has already seen threaded through the nested statistic
    /// definitions, so the two sequences differ line for line while the alt says nothing new — and
    /// the whole body ended in the same five numbers twice with nothing to tell the copies apart.
    /// The repeated-tail pass could not reach it either, because the earlier copy was interleaved
    /// rather than adjacent.
    /// <para>
    /// The block is speculative, so what it visited is remembered only if it is kept — but it is
    /// judged against one shared record, rolled back to a saved mark, rather than against a copy of
    /// that record. A copy per level made live memory quadratic in depth for no behavioural gain,
    /// and it was the depth that put gigabytes behind one tooltip read. What the block spent of the
    /// visit budget is never rolled back: the walk really did that work.
    /// </para>
    /// </remarks>
    private static void AppendAlternate(
        ITooltipable tooltip,
        List<string> lines,
        WalkBounds bounds,
        ref bool truncated)
    {
        var alternate = new List<string>();
        var mark = bounds.Mark;
        var alternateTruncated = false;
        AppendNodes(tooltip.GetAltTooltipNodes(), alternate, bounds, ref alternateTruncated);
        if (SaysNothingNew(lines, lines.Count, alternate, 0, alternate.Count))
        {
            bounds.Forget(mark);
        }
        else
        {
            for (var index = 0; index < alternate.Count; index++)
                AppendLine(lines, alternate[index], ref truncated);
        }
        if (alternateTruncated) truncated = true;
    }

    private static void AppendNodes(
        IReadOnlyList<TooltipNode>? nodes,
        List<string> lines,
        WalkBounds bounds,
        ref bool truncated)
    {
        if (nodes is null) return;
        if (!bounds.Descend()) return;
        for (var index = 0; index < nodes.Count; index++)
        {
            if (lines.Count >= MaximumLines)
            {
                truncated = true;
                break;
            }
            var node = nodes[index];
            if (node is null) continue;
            if (!bounds.Spend()) break;
            try
            {
                AppendLine(lines, node.textFn is null ? node.text : node.textFn(), ref truncated);
            }
            catch (Exception exception)
            {
                AppendLine(
                    lines,
                    "Tooltip text unavailable." +
                        GameActionFaultLog.Record(exception, "tooltip text"),
                    ref truncated);
            }
            if (node.tooltipable is not null)
                AppendTooltip(node.tooltipable, lines, bounds, ref truncated);
            AppendTooltips(node.subTooltips, lines, bounds, ref truncated);
            AppendNodes(node.children, lines, bounds, ref truncated);
        }
        bounds.Ascend();
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

    /// <summary>
    /// What the walk has seen, how deep it is, and how much of its budget is left.
    /// </summary>
    /// <remarks>
    /// Reference identity recognises a graph that leads back to the same object, which is a real
    /// shape and the one the cycle test covers. It is not a bound, and the game offers nothing
    /// better: <c>ITooltipable</c> declares nine presentation members and no identity;
    /// <c>BasicTooltip</c> keeps no reference to whatever it was built from — its
    /// <c>.ctor(ITooltipable)</c> copies the name, description, display type, icon and node list and
    /// drops the argument — and its <c>GetObservableId()</c> reads a field no constructor sets;
    /// <c>AbstractRefInstance&lt;T&gt;</c>'s interface-reachable <c>GetGuidReference()</c> answers
    /// the guid of the asset it points at rather than its own, so two instances a player can tell
    /// apart share it and keying on it would drop a tooltip the screen is showing. So the wrappers
    /// win the identity argument, and <see cref="MaximumDepth"/> and <see cref="MaximumVisits"/> are
    /// what actually stops the walk.
    /// </remarks>
    private sealed class WalkBounds
    {
        private readonly HashSet<ITooltipable> _seen = new(ReferenceComparer.Instance);
        private readonly List<ITooltipable> _order = new();
        private int _depth;
        private int _visits;

        /// <summary>Whether the walk gave up rather than finished.</summary>
        internal bool Exceeded { get; private set; }

        /// <summary>The point a speculative block's visits can be rolled back to.</summary>
        internal int Mark => _order.Count;

        internal bool Spend()
        {
            if (Exceeded) return false;
            if (_visits >= MaximumVisits)
            {
                Exceeded = true;
                return false;
            }
            _visits++;
            return true;
        }

        internal bool Descend()
        {
            if (Exceeded) return false;
            if (_depth >= MaximumDepth)
            {
                Exceeded = true;
                return false;
            }
            _depth++;
            return true;
        }

        internal void Ascend() => _depth--;

        /// <summary>Whether this is the first time the walk has reached this exact object.</summary>
        internal bool Remember(ITooltipable tooltip)
        {
            if (!_seen.Add(tooltip)) return false;
            _order.Add(tooltip);
            return true;
        }

        internal void Forget(int mark)
        {
            for (var index = mark; index < _order.Count; index++) _seen.Remove(_order[index]);
            _order.RemoveRange(mark, _order.Count - mark);
        }
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
