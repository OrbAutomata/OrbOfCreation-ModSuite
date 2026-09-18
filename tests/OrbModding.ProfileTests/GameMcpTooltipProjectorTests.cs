using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpTooltipProjectorTests
{
    [Fact]
    public void ProseIncludesNestedComputedAndInspectedScreenText()
    {
        var computations = 0;
        var linked = new FakeTooltip("Linked", new TooltipNode("linked row"));
        var computed = new TooltipNode(string.Empty)
        {
            nodeType = TooltipNode.NodeType.IconText,
            textFn = () =>
            {
                computations++;
                return "live value 42";
            },
            tooltipable = linked,
        };
        var root = new TooltipNode("section")
        {
            nodeType = TooltipNode.NodeType.Parent,
            parentType = TooltipNode.ParentType.Boxed,
            children = new List<TooltipNode>
            {
                new("authored child"),
                computed,
            },
        };
        var primary = new FakeTooltip("Primary", root);
        var nested = new FakeTooltip("Nested", new TooltipNode("nested row"));
        var inspected = new FakeTooltip("Inspected", new TooltipNode("panel row"));

        var result = Projected(primary, new[] { nested }, new[] { inspected });

        Assert.Single(result.Properties());
        var text = (string?)result["text"];
        Assert.NotNull(text);
        Assert.Contains("Primary\nFixture\nPrimary description", text, StringComparison.Ordinal);
        Assert.Contains("section\nauthored child\nlive value 42", text, StringComparison.Ordinal);
        Assert.Contains("Linked\nFixture\nLinked description\nlinked row", text, StringComparison.Ordinal);
        Assert.Contains("Nested\nFixture\nNested description\nnested row", text, StringComparison.Ordinal);
        Assert.Contains("Inspected\nFixture\nInspected description\npanel row", text, StringComparison.Ordinal);
        Assert.Equal(1, computations);
    }

    /// <summary>
    /// A computed row whose value throws is answered in the suite's own words and carries the
    /// reference the exception is filed under; the rest of the tooltip still reads.
    /// </summary>
    /// <remarks>
    /// This body is tooltip words a player reads, so the game's own exception text landing in it
    /// reached the wire as surely as any refusal did. The row it failed on is the caller's fact;
    /// the type and member that threw are the maintainer's.
    /// </remarks>
    [Fact]
    public void AComputedRowThatThrowsCarriesAReferenceInsteadOfTheGamesExceptionText()
    {
        var logged = new List<string>();
        GameActionFaultLog.ConfigureLog(logged.Add);
        try
        {
            var wedged = new TooltipNode(string.Empty)
            {
                nodeType = TooltipNode.NodeType.IconText,
                textFn = () => throw new InvalidOperationException(
                    "EffectResultInfo.GetValue found no result"),
            };
            var root = new TooltipNode("section")
            {
                nodeType = TooltipNode.NodeType.Parent,
                parentType = TooltipNode.ParentType.Boxed,
                children = new List<TooltipNode> { new("authored child"), wedged },
            };

            var result = Projected(new FakeTooltip("Primary", root));

            var text = (string?)result["text"] ?? string.Empty;
            Assert.Contains("authored child", text, StringComparison.Ordinal);
            Assert.Contains("Tooltip text unavailable.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("EffectResultInfo.GetValue found no result", text);

            var reference = Assert.Single(
                Regex.Matches(text, "MCP-[0-9A-F]{8}").Select(match => match.Value));
            var line = Assert.Single(
                logged, entry => entry.Contains(reference, StringComparison.Ordinal));
            Assert.Contains("EffectResultInfo.GetValue found no result", line);
        }
        finally
        {
            GameActionFaultLog.ConfigureLog(null);
        }
    }

    [Fact]
    public void TooltipCyclesStopAfterTheFirstScreenTextCopy()
    {
        var tooltip = new FakeTooltip("Cycle");
        var node = new TooltipNode("cycle") { tooltipable = tooltip };
        tooltip.Nodes.Add(node);

        var result = Projected(tooltip);

        Assert.Equal("Cycle\nFixture\nCycle description\ncycle", (string?)result["text"]);
        Assert.Single(result.Properties());
    }

    [Fact]
    public void IdenticalAlternateTreeAndRichTextCeremonyAreRemoved()
    {
        var primaryNode = new TooltipNode("<emph>Quantity:</emph>");
        var altNode = new TooltipNode("<emph>Quantity:</emph>");
        var tooltip = new FakeTooltip("Resource", primaryNode)
        {
            DisplayType = "<#BBACE2FF>Essence</color> Resource",
            Description = "<deemph>Spendable List<T> supply.</deemph>",
        };
        tooltip.AltNodes.Add(altNode);

        var result = Projected(tooltip);

        Assert.Equal(
            "Resource\nEssence Resource\nSpendable List<T> supply.\nQuantity:",
            (string?)result["text"]);
        Assert.True(result.ToString(Newtonsoft.Json.Formatting.None).Length < 500);
    }

    /// <summary>
    /// The shape the whole-source rule could not reach. A resource pill threads its values through
    /// the nested statistic definitions that explain them, and its alt tree paints the same values
    /// bare. The two sequences differ line for line, so comparing them as sequences kept the alt,
    /// and the earlier copy was interleaved rather than adjacent, so the repeated-tail pass could
    /// not see it either — the body ended in the same numbers twice with nothing to tell the copies
    /// apart. The rule is the same one every other block is judged by: a block whose every line is
    /// already on the page adds nothing.
    /// </summary>
    [Fact]
    public void An_alternate_tree_that_repeats_values_already_threaded_through_the_body_is_dropped()
    {
        var tooltip = new FakeTooltip(
            "Glyph Upgrades",
            new TooltipNode("Quantity:"),
            new TooltipNode("How many you are holding."),
            new TooltipNode("58/193"),
            new TooltipNode("Capacity:"),
            new TooltipNode("The most you can hold."),
            new TooltipNode("193"),
            new TooltipNode("(+193, x1)"))
        {
            DisplayType = "Advancement Resource",
            Description = "Spent on advancements.",
        };
        tooltip.AltNodes.Add(new TooltipNode("Quantity:"));
        tooltip.AltNodes.Add(new TooltipNode("58/193"));
        tooltip.AltNodes.Add(new TooltipNode("Capacity:"));
        tooltip.AltNodes.Add(new TooltipNode("193"));
        tooltip.AltNodes.Add(new TooltipNode("(+193, x1)"));

        var result = Projected(tooltip);

        Assert.Equal(
            "Glyph Upgrades\nAdvancement Resource\nSpent on advancements.\n" +
            "Quantity:\nHow many you are holding.\n58/193\n" +
            "Capacity:\nThe most you can hold.\n193\n(+193, x1)",
            (string?)result["text"]);
    }

    /// <summary>
    /// An alt tree that says one new thing is kept whole, including the lines it shares with the
    /// body — dropping those would leave the new line under a label that is no longer there.
    /// </summary>
    [Fact]
    public void An_alternate_tree_with_one_new_line_is_kept_whole()
    {
        var tooltip = new FakeTooltip("Ward", new TooltipNode("Shield:"), new TooltipNode("4"))
        {
            DisplayType = "Effect",
            Description = "Absorbs damage.",
        };
        tooltip.AltNodes.Add(new TooltipNode("Shield:"));
        tooltip.AltNodes.Add(new TooltipNode("4"));
        tooltip.AltNodes.Add(new TooltipNode("Next tier: 9"));

        var result = Projected(tooltip);

        Assert.Equal(
            "Ward\nEffect\nAbsorbs damage.\nShield:\n4\nShield:\n4\nNext tier: 9",
            (string?)result["text"]);
    }

    /// <summary>
    /// Adjacent duplicate lines were suppressed one at a time, which never caught a panel painting
    /// its whole last block twice — half a body saying nothing the half above it had not.
    /// </summary>
    [Fact]
    public void A_body_that_paints_its_last_block_twice_says_it_once()
    {
        var tooltip = new FakeTooltip(
            "Mana",
            new TooltipNode("Quantity: 6/13"),
            new TooltipNode("Capacity: 13"),
            new TooltipNode("(+13, x1)"),
            new TooltipNode("Quantity: 6/13"),
            new TooltipNode("Capacity: 13"),
            new TooltipNode("(+13, x1)"))
        {
            DisplayType = "Resource",
            Description = "Raw magic.",
        };

        var result = Projected(tooltip);

        Assert.Equal(
            "Mana\nResource\nRaw magic.\nQuantity: 6/13\nCapacity: 13\n(+13, x1)",
            (string?)result["text"]);
    }

    /// <summary>
    /// A tail that repeats at two scales loses the smallest repeat, not the largest. Taking the
    /// largest match deleted half a body where one block was all that had been said twice.
    /// </summary>
    [Fact]
    public void A_tail_that_repeats_at_two_scales_loses_only_the_smallest_repeat()
    {
        var tooltip = new FakeTooltip(
            "Ward",
            new TooltipNode("Shield: 4"),
            new TooltipNode("Decay: 2"),
            new TooltipNode("Shield: 4"),
            new TooltipNode("Decay: 2"),
            new TooltipNode("Shield: 4"),
            new TooltipNode("Decay: 2"),
            new TooltipNode("Shield: 4"),
            new TooltipNode("Decay: 2"))
        {
            DisplayType = "Effect",
            Description = "Absorbs damage.",
        };

        var result = Projected(tooltip);

        Assert.Equal(
            "Ward\nEffect\nAbsorbs damage.\n" +
            "Shield: 4\nDecay: 2\nShield: 4\nDecay: 2\nShield: 4\nDecay: 2",
            (string?)result["text"]);
    }

    [Fact]
    public void FortyNodeDuplicateTooltipFitsTheCriticSignalBudget()
    {
        var tooltip = new FakeTooltip("Dense");
        for (var index = 0; index < 40; index++)
        {
            var text = "Fact " + index + ": <emph>1.23e24</emph>";
            tooltip.Nodes.Add(new TooltipNode(text));
            tooltip.AltNodes.Add(new TooltipNode(text));
        }

        var result = Projected(tooltip);
        var encoded = result.ToString(Newtonsoft.Json.Formatting.None);

        Assert.Single(result.Properties());
        Assert.Equal(1, encoded.Split("Fact 0:", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("<emph>", encoded, StringComparison.Ordinal);
        Assert.Equal(754, System.Text.Encoding.UTF8.GetByteCount(encoded));
    }

    [Fact]
    public void DeepTooltipNamesTheTwoHundredLineTruncation()
    {
        var tooltip = new FakeTooltip("Deep");
        for (var index = 0; index < 205; index++)
            tooltip.Nodes.Add(new TooltipNode("Fact " + index));

        var result = Projected(tooltip);
        var lines = ((string)result["text"]!).Split('\n');

        Assert.Equal(201, lines.Length);
        Assert.Equal("Tooltip truncated after 200 lines.", lines[^1]);
        Assert.DoesNotContain("Fact 204", lines);
    }

    /// <summary>
    /// The shape that took the game's main thread down. Every level hands out a brand-new wrapper
    /// object, so reference identity recognises none of them, and no level says anything, so the
    /// line budget is never spent. Nothing about this graph ends: without a bound of its own the
    /// walk does not come back, and neither does this test.
    /// </summary>
    [Fact]
    public void A_graph_that_never_ends_is_answered_as_a_bound_rather_than_a_tooltip()
    {
        var minted = new int[1];

        Assert.False(GameMcpTooltipProjector.TryProject(
            new EndlessWrapperTooltip(minted), null, null, out var details));

        Assert.Empty(GameMcpTestHarness.Json(details).Properties());
        Assert.True(minted[0] < 1_000, "one tooltip read built " + minted[0] + " wrapper objects");
    }

    /// <summary>
    /// The second bound on its own: a graph one hop deep and far wider than the walk will read. The
    /// visit budget stops it before the last row, which is what this asserts — a graph that says
    /// nothing cannot buy more walking by being flat instead of deep.
    /// </summary>
    [Fact]
    public void A_graph_too_wide_to_walk_stops_before_its_last_row()
    {
        var touched = new int[1];
        var root = new FakeTooltip("Wide")
        {
            DisplayType = string.Empty,
            Description = string.Empty,
        };
        for (var index = 0; index < 4096; index++)
        {
            root.Nodes.Add(new TooltipNode(string.Empty)
            {
                tooltipable = new SilentTooltip(touched),
            });
        }

        Assert.False(GameMcpTooltipProjector.TryProject(root, null, null, out var details));

        Assert.Empty(GameMcpTestHarness.Json(details).Properties());
        Assert.True(touched[0] < 4096, "the walk read " + touched[0] + " of 4096 rows");
    }

    /// <summary>
    /// A genuine tooltip is not cut off by the bound. Thirty-one nested tooltip links is far more
    /// nesting than the game itself draws — it renders one tooltipable's own nodes and hands the
    /// links to the next hover — and it still reads in full.
    /// </summary>
    [Fact]
    public void A_deep_tooltip_inside_the_bound_still_reads_in_full()
    {
        var tooltip = new FakeTooltip("Link 30", new TooltipNode("row 30"));
        for (var level = 29; level >= 0; level--)
        {
            var parent = new FakeTooltip("Link " + level, new TooltipNode("row " + level));
            parent.Nodes[0].tooltipable = tooltip;
            tooltip = parent;
        }

        var text = (string?)Projected(tooltip)["text"] ?? string.Empty;

        for (var level = 0; level <= 30; level++)
            Assert.Contains("Link " + level, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The projection of a tooltip whose walk finishes. Every fixture below is one, so each of them
    /// asserts the walk came back on its own rather than leaving that to the two tests that cross
    /// the bound deliberately.
    /// </summary>
    private static JObject Projected(
        ITooltipable primary,
        IEnumerable<ITooltipable>? nested = null,
        IEnumerable<ITooltipable>? inspected = null)
    {
        Assert.True(
            GameMcpTooltipProjector.TryProject(primary, nested, inspected, out var details),
            "the walk gave up on a fixture that is inside its bound");
        return GameMcpTestHarness.Json(details);
    }

    private sealed class FakeTooltip : ITooltipable
    {
        internal FakeTooltip(string name, params TooltipNode[] nodes)
        {
            Name = name;
            Nodes.AddRange(nodes);
        }

        private string Name { get; }
        internal string DisplayType { get; set; } = "Fixture";
        internal string? Description { get; set; }
        internal List<TooltipNode> Nodes { get; } = new();
        internal List<TooltipNode> AltNodes { get; } = new();
        public string GetName() => Name;
        public string GetDisplayType() => DisplayType;
        public UnityEngine.Sprite GetIcon() => new();
        public UnityEngine.Color GetColor() => UnityEngine.Color.white;
        public bool IsColoredIcon() => false;
        public bool HasAltTooltips() => AltNodes.Count > 0;
        public string GetDescription() => Description ?? Name + " description";
        public List<TooltipNode> GetTooltipNodes() => Nodes;
        public List<TooltipNode> GetAltTooltipNodes() => AltNodes;
    }

    /// <summary>
    /// A graph with no end and nothing to say: every level is a fresh object, and every line is
    /// empty, so neither of the two stop conditions the walk had before could ever fire.
    /// </summary>
    private sealed class EndlessWrapperTooltip : ITooltipable
    {
        private readonly int[] _minted;

        internal EndlessWrapperTooltip(int[] minted)
        {
            _minted = minted;
            _minted[0]++;
        }

        public string GetName() => string.Empty;
        public string GetDisplayType() => string.Empty;
        public string GetDescription() => string.Empty;
        public UnityEngine.Sprite GetIcon() => new();
        public UnityEngine.Color GetColor() => UnityEngine.Color.white;
        public bool IsColoredIcon() => false;
        public bool HasAltTooltips() => false;
        public List<TooltipNode> GetAltTooltipNodes() => new();

        public List<TooltipNode> GetTooltipNodes() => new()
        {
            new TooltipNode(string.Empty)
            {
                tooltipable = new EndlessWrapperTooltip(_minted),
            },
        };
    }

    /// <summary>One row that says nothing and counts having been read.</summary>
    private sealed class SilentTooltip : ITooltipable
    {
        private readonly int[] _touched;

        internal SilentTooltip(int[] touched) => _touched = touched;

        public string GetName()
        {
            _touched[0]++;
            return string.Empty;
        }

        public string GetDisplayType() => string.Empty;
        public string GetDescription() => string.Empty;
        public UnityEngine.Sprite GetIcon() => new();
        public UnityEngine.Color GetColor() => UnityEngine.Color.white;
        public bool IsColoredIcon() => false;
        public bool HasAltTooltips() => false;
        public List<TooltipNode> GetTooltipNodes() => new();
        public List<TooltipNode> GetAltTooltipNodes() => new();
    }
}
