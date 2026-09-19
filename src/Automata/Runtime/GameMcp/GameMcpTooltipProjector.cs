#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using OrbModding.Common;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;

namespace OrbAutomata.GameMcp;

/// <summary>Renders the one tooltip container the screen drew, and names the links it offers.</summary>
/// <remarks>
/// <para>
/// One hovered element puts up one <c>UITooltipContainer</c>, and that container draws three things:
/// the core panel (<c>UITooltip.Render</c> on <c>container.item</c>), one row of sibling sub-panels
/// (<c>RenderChildren</c> over <c>container.subTooltips</c>, which carry no sub-panels of their own),
/// and, inside each panel, one node list walked through <c>TooltipNode.children</c>.
/// <c>UITooltipNode.Render</c> reads nothing else.
/// </para>
/// <para>
/// <c>TooltipNode.tooltipable</c> is not text at all: <c>UITooltipNode.Setup</c> hands it to
/// <c>HoverTooltip.Setup</c> as the target of the player's <em>next</em> hover. Following it was how
/// this reader turned one screen panel into the transitive closure of the entity graph behind it —
/// a cantrip that fits one panel printed ninety lines of glossary, and three round-15 reads refused
/// outright. A link is published as the one line the screen offers: its name, and the call that
/// reads it.
/// </para>
/// </remarks>
internal static class GameMcpTooltipProjector
{
    /// <summary>
    /// The most lines an answer carries, and a guard rather than a shape the game reaches.
    /// </summary>
    /// <remarks>
    /// A panel's body is its own name, type and description plus the nodes <c>UITooltipNodeList</c>
    /// lays out on screen, so two hundred lines is more than the game fits in a panel and more than
    /// the sub-panel row beside it. No authored panel has been seen to reach it; it stays because
    /// the node list is the game's and a budget the suite owns is what keeps one answer bounded.
    /// </remarks>
    private const int MaximumLines = 200;

    /// <summary>
    /// How deep this walk goes and how much of the game it touches before it gives up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The walk is the container's own shape — one panel, its authored <c>children</c> nesting, and
    /// one non-recursive row of sub-panels — so its depth is what the game itself lays out and its
    /// visit count is the number of node objects in one on-screen panel. The game's own answer to
    /// how deep tooltips go is <c>UITooltipContainer.MaxTooltipDepth</c>, five stacked panels; sixty
    /// four levels and two thousand nodes are far past anything it can draw.
    /// </para>
    /// <para>
    /// So these are a guard and not a policy a caller meets: the only way back to them is an
    /// authored <c>children</c> tree the game could not render either. They stay because the node
    /// list is the game's data, and a reader with no bound took the Unity main thread and several
    /// gigabytes with it once already. Neither number is a setting.
    /// </para>
    /// </remarks>
    private const int MaximumDepth = 64;
    private const int MaximumVisits = 2_000;

    /// <summary>
    /// The container's words, or <c>false</c> when the walk hit <see cref="MaximumDepth"/> or
    /// <see cref="MaximumVisits"/> and was abandoned.
    /// </summary>
    /// <param name="primary">The core panel's item — <c>UITooltipContainer.item</c>.</param>
    /// <param name="subTooltips">The sibling panels beside it, drawn once and never recursed.</param>
    /// <param name="usingAlt">
    /// Whether the screen is holding the more-info key. <c>UITooltip.Render</c> sets
    /// <c>renderedAlt = IsUsingAltTooltip()</c> and then renders <c>GetAltTooltipNodes()</c>
    /// <em>instead of</em> <c>GetTooltipNodes()</c>, so the two lists are a toggle and a panel draws
    /// exactly one of them.
    /// </param>
    /// <param name="identify">
    /// The stable id of a linked tooltipable, or <see cref="Guid.Empty"/> when the link is about no
    /// entity. A link with an id publishes the call that reads it; one without publishes its name,
    /// because nothing addresses it.
    /// </param>
    /// <param name="details">The one <c>text</c> field, or an empty object when the walk gave up.</param>
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
        IEnumerable<ITooltipable>? subTooltips,
        bool usingAlt,
        Func<ITooltipable, Guid>? identify,
        out JObject details)
    {
        if (primary is null) throw new ArgumentNullException(nameof(primary));
        var body = new Body(identify);
        AppendPanel(primary, usingAlt, body);
        if (subTooltips is not null)
        {
            foreach (var panel in subTooltips)
                if (panel is not null) AppendPanel(panel, usingAlt, body);
        }
        details = new JObject();
        if (body.Bounds.Exceeded) return false;
        DropRepeatedTail(body.Lines);
        if (body.Truncated) body.Lines.Add("Tooltip truncated after 200 lines.");
        details["text"] = string.Join("\n", body.Lines);
        return true;
    }

    /// <summary>One panel, exactly as <c>UITooltip.Render</c> paints it.</summary>
    private static void AppendPanel(ITooltipable panel, bool usingAlt, Body body)
    {
        if (!body.Bounds.Spend()) return;
        if (!body.Bounds.Remember(panel)) return;
        if (!body.Bounds.Descend()) return;
        body.Add(panel.GetName());
        body.Add(panel.GetDisplayType());
        body.Add(panel.GetDescription());
        AppendNodes(
            usingAlt && panel.HasAltTooltips()
                ? panel.GetAltTooltipNodes()
                : panel.GetTooltipNodes(),
            body);
        body.Bounds.Ascend();
    }

    private static void AppendNodes(IReadOnlyList<TooltipNode>? nodes, Body body)
    {
        if (nodes is null) return;
        if (!body.Bounds.Descend()) return;
        for (var index = 0; index < nodes.Count; index++)
        {
            if (body.Full)
            {
                body.Truncate();
                break;
            }
            var node = nodes[index];
            if (node is null) continue;
            if (!body.Bounds.Spend()) break;
            try
            {
                body.Add(node.textFn is null ? node.text : node.textFn());
            }
            catch (Exception exception)
            {
                body.Add(
                    "Tooltip text unavailable." +
                        GameActionFaultLog.Record(exception, "tooltip text"));
            }
            AppendPointer(node.tooltipable, body);
            AppendNodes(node.children, body);
        }
        body.Bounds.Ascend();
    }

    /// <summary>
    /// The next hover, named rather than followed.
    /// </summary>
    /// <remarks>
    /// One panel links the same definition from many rows — every statistic points at the record
    /// that explains it — so a pointer the body already carries says nothing by being said again. A
    /// link the suite cannot address carries its name alone: minting a screen path for a thing the
    /// screen is not drawing would be a guess.
    /// </remarks>
    private static void AppendPointer(ITooltipable? link, Body body)
    {
        if (link is null) return;
        var name = GameMcpTextFormatter.Plain(link.GetName() ?? string.Empty).Trim();
        if (name.Length == 0) return;
        var id = body.Identify(link);
        body.AddPointer(
            id == Guid.Empty
                ? "→ " + name
                : "→ " + name + ": game_tooltip uuid=" + id.ToString("D"));
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

    /// <summary>The answer being built, and everything the walk carries while it builds it.</summary>
    private sealed class Body
    {
        private readonly Func<ITooltipable, Guid>? _identify;
        private readonly HashSet<string> _pointers = new(StringComparer.Ordinal);

        internal Body(Func<ITooltipable, Guid>? identify) => _identify = identify;

        internal List<string> Lines { get; } = new();

        internal WalkBounds Bounds { get; } = new();

        internal bool Truncated { get; private set; }

        internal bool Full => Lines.Count >= MaximumLines;

        internal void Truncate() => Truncated = true;

        internal Guid Identify(ITooltipable link) =>
            _identify is null ? Guid.Empty : _identify(link);

        internal void Add(string? text)
        {
            var plain = GameMcpTextFormatter.Plain(text ?? string.Empty).Trim();
            if (plain.Length == 0) return;
            if (Lines.Count > 0 && string.Equals(Lines[^1], plain, StringComparison.Ordinal)) return;
            if (Full)
            {
                Truncated = true;
                return;
            }
            Lines.Add(plain);
        }

        internal void AddPointer(string pointer)
        {
            if (!_pointers.Add(pointer)) return;
            if (Full)
            {
                Truncated = true;
                return;
            }
            Lines.Add(pointer);
        }
    }

    /// <summary>
    /// What the walk has seen, how deep it is, and how much of its budget is left.
    /// </summary>
    /// <remarks>
    /// Reference identity recognises a sub-panel that paints the same object as the core panel, and
    /// the game offers nothing better: <c>ITooltipable</c> declares nine presentation members and no
    /// identity. It is not a bound, which is why <see cref="MaximumDepth"/> and
    /// <see cref="MaximumVisits"/> are still here as one.
    /// </remarks>
    private sealed class WalkBounds
    {
        private readonly HashSet<ITooltipable> _seen = new(ReferenceComparer.Instance);
        private int _depth;
        private int _visits;

        /// <summary>Whether the walk gave up rather than finished.</summary>
        internal bool Exceeded { get; private set; }

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
        internal bool Remember(ITooltipable tooltip) => _seen.Add(tooltip);
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
