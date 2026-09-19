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
    /// <summary>
    /// The whole body is the container the screen drew: the core panel, its node tree walked through
    /// <c>children</c>, and the sibling sub-panel row beside it.
    /// </summary>
    [Fact]
    public void The_body_is_the_core_panel_its_children_and_the_sub_panel_row()
    {
        var computations = 0;
        var computed = new TooltipNode(string.Empty)
        {
            nodeType = TooltipNode.NodeType.IconText,
            textFn = () =>
            {
                computations++;
                return "live value 42";
            },
        };
        var root = new TooltipNode("section")
        {
            nodeType = TooltipNode.NodeType.Parent,
            parentType = TooltipNode.ParentType.Boxed,
            children = new List<TooltipNode> { new("authored child"), computed },
        };
        var primary = new FakeTooltip("Primary", root);
        var sub = new FakeTooltip("Sub", new TooltipNode("sub row"));

        var result = Projected(primary, new[] { sub });

        Assert.Single(result.Properties());
        Assert.Equal(
            "Primary\nFixture\nPrimary description\nsection\nauthored child\nlive value 42\n" +
            "Sub\nFixture\nSub description\nsub row",
            (string?)result["text"]);
        Assert.Equal(1, computations);
    }

    /// <summary>
    /// A link is the player's next hover, so it is published as the call that reads it and never
    /// unfurled. Following it was what turned one screen panel into the entity graph behind it.
    /// </summary>
    [Fact]
    public void A_linked_tooltipable_is_a_pointer_rather_than_a_panel()
    {
        var linked = new CountingTooltip("Cooldown Speed", new TooltipNode("linked row"));
        var id = Guid.Parse("2f1c6d0a-7b43-4c19-9f0e-5a2d8c3b6e71");
        var primary = new FakeTooltip("Primary", new TooltipNode("Cooldown Speed: 147%")
        {
            tooltipable = linked,
        });

        var text = (string?)Projected(primary, identity: Identity(linked, id))["text"];

        Assert.Equal(
            "Primary\nFixture\nPrimary description\nCooldown Speed: 147%\n" +
            "→ Cooldown Speed: game_tooltip uuid=2f1c6d0a-7b43-4c19-9f0e-5a2d8c3b6e71",
            text);
        Assert.Equal(0, linked.NodeReads);
        Assert.Equal(0, linked.DescriptionReads);
    }

    /// <summary>
    /// A link about no entity has no address anywhere in the suite, so its pointer is its name. A
    /// screen path would be a guess: the screen is not drawing the thing the link points at.
    /// </summary>
    [Fact]
    public void A_link_the_suite_cannot_address_carries_its_name_alone()
    {
        var linked = new CountingTooltip("Enhancement", new TooltipNode("linked row"));
        var primary = new FakeTooltip("Primary", new TooltipNode("Enhanced")
        {
            tooltipable = linked,
        });

        var text = (string?)Projected(primary)["text"];

        Assert.Equal("Primary\nFixture\nPrimary description\nEnhanced\n→ Enhancement", text);
        Assert.Equal(0, linked.NodeReads);
    }

    /// <summary>
    /// Every statistic row of one panel points at the record that explains it, and several rows
    /// share a record. The address is the same line, and a line the body already carries says
    /// nothing by being carried again.
    /// </summary>
    [Fact]
    public void One_definition_linked_from_several_rows_is_pointed_at_once()
    {
        var definition = new CountingTooltip("Mana");
        var primary = new FakeTooltip(
            "Primary",
            new TooltipNode("Cost: 12") { tooltipable = definition },
            new TooltipNode("Upkeep: 3") { tooltipable = definition });

        var text = (string?)Projected(primary)["text"];

        Assert.Equal(
            "Primary\nFixture\nPrimary description\nCost: 12\n→ Mana\nUpkeep: 3",
            text);
    }

    /// <summary>
    /// The sub-panel row is drawn once and stops, exactly as <c>UITooltip</c> stops: it has no
    /// <c>subTooltips</c> field and no <c>RenderChildren</c>, so a sub-panel's own links are the
    /// next hover just like the core panel's.
    /// </summary>
    [Fact]
    public void A_sub_panel_draws_its_own_words_and_points_at_its_links()
    {
        var onward = new CountingTooltip("Onward", new TooltipNode("onward row"));
        var sub = new FakeTooltip("Sub", new TooltipNode("sub row") { tooltipable = onward });
        var primary = new FakeTooltip("Primary", new TooltipNode("primary row"));

        var text = (string?)Projected(primary, new[] { sub })["text"];

        Assert.Equal(
            "Primary\nFixture\nPrimary description\nprimary row\n" +
            "Sub\nFixture\nSub description\nsub row\n→ Onward",
            text);
        Assert.Equal(0, onward.NodeReads);
    }

    /// <summary>
    /// <c>UITooltip.Render</c> sets <c>renderedAlt</c> and then renders one list or the other. The
    /// two were appended together here, which put the alt block's values on the page beside the
    /// same values threaded through the body.
    /// </summary>
    [Fact]
    public void The_alt_list_replaces_the_main_list_rather_than_joining_it()
    {
        var tooltip = new FakeTooltip("Glyph Upgrades", new TooltipNode("Quantity: 58/193"))
        {
            DisplayType = "Advancement Resource",
            Description = "Spent on advancements.",
        };
        tooltip.AltNodes.Add(new TooltipNode("58/193"));
        tooltip.AltNodes.Add(new TooltipNode("(+193, x1)"));

        Assert.Equal(
            "Glyph Upgrades\nAdvancement Resource\nSpent on advancements.\nQuantity: 58/193",
            (string?)Projected(tooltip)["text"]);
        Assert.Equal(
            "Glyph Upgrades\nAdvancement Resource\nSpent on advancements.\n58/193\n(+193, x1)",
            (string?)Projected(tooltip, usingAlt: true)["text"]);
    }

    /// <summary>
    /// <c>IsUsingAltTooltip()</c> is <c>item.HasAltTooltips() &amp;&amp; ShowMoreInfo()</c>, so a
    /// panel with nothing behind the key draws its main list whatever the key is doing.
    /// </summary>
    [Fact]
    public void A_panel_with_no_alt_list_draws_its_main_list_under_the_more_info_key()
    {
        var tooltip = new FakeTooltip("Ward", new TooltipNode("Shield: 4"))
        {
            DisplayType = "Effect",
            Description = "Absorbs damage.",
        };

        Assert.Equal(
            "Ward\nEffect\nAbsorbs damage.\nShield: 4",
            (string?)Projected(tooltip, usingAlt: true)["text"]);
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
    public void UnityRichTextCeremonyIsStripped()
    {
        var tooltip = new FakeTooltip("Resource", new TooltipNode("<emph>Quantity:</emph>"))
        {
            DisplayType = "<#BBACE2FF>Essence</color> Resource",
            Description = "<deemph>Spendable List<T> supply.</deemph>",
        };

        var result = Projected(tooltip);

        Assert.Equal(
            "Resource\nEssence Resource\nSpendable List<T> supply.\nQuantity:",
            (string?)result["text"]);
        Assert.True(result.ToString(Newtonsoft.Json.Formatting.None).Length < 500);
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
    /// The three reads round fifteen could not get an answer out of. Every one of them refused on
    /// the closure behind its panel — statistic definitions linking statistic definitions, through
    /// per-call wrappers reference identity cannot recognise — while the panel itself is what the
    /// player reads off the screen without trouble. Under the container's own shape they read in
    /// full, and what they used to unfurl is one pointer line each.
    /// </summary>
    [Fact]
    public void The_round_fifteen_refusals_read_in_full_with_pointers()
    {
        var minted = new int[1];
        var glossary = new CountingTooltip("Cooldown Speed");
        var primary = new FakeTooltip(
            "Whirling Sorcery",
            new TooltipNode("Cooldown Time: 27.5") { tooltipable = glossary },
            new TooltipNode("Recharge: 18.7") { tooltipable = glossary },
            new TooltipNode(string.Empty)
            {
                nodeType = TooltipNode.NodeType.IconText,
                textFn = () => "Enhancement: Storm",
                tooltipable = new EndlessWrapperTooltip(minted),
            })
        {
            DisplayType = "Spell",
            Description = "Whirls.",
        };

        var text = (string?)Projected(primary)["text"];

        Assert.Equal(
            "Whirling Sorcery\nSpell\nWhirls.\n" +
            "Cooldown Time: 27.5\n→ Cooldown Speed\nRecharge: 18.7\nEnhancement: Storm",
            text);
        Assert.Equal(0, glossary.NodeReads);
        Assert.Equal(1, minted[0]);
    }

    /// <summary>
    /// The shape that took the game's main thread down, now only reachable through an authored
    /// <c>children</c> tree the game could not draw either: every level hands out a brand-new node,
    /// and no level says anything, so the line budget is never spent. Nothing about this tree ends —
    /// without a bound of its own the walk does not come back, and neither does this test.
    /// </summary>
    [Fact]
    public void A_children_tree_that_never_ends_is_answered_as_a_bound_rather_than_a_tooltip()
    {
        Assert.False(GameMcpTooltipProjector.TryProject(
            new EndlessChildrenTooltip(), null, false, null, out var details));

        Assert.Empty(GameMcpTestHarness.Json(details).Properties());
    }

    /// <summary>
    /// The second bound on its own: one panel far wider than the walk will read. The visit budget
    /// stops it before the last row, which is what this asserts — a node list that says nothing
    /// cannot buy more walking by being flat instead of deep.
    /// </summary>
    [Fact]
    public void A_panel_too_wide_to_walk_stops_before_its_last_row()
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
                textFn = () =>
                {
                    touched[0]++;
                    return string.Empty;
                },
            });
        }

        Assert.False(GameMcpTooltipProjector.TryProject(root, null, false, null, out var details));

        Assert.Empty(GameMcpTestHarness.Json(details).Properties());
        Assert.True(touched[0] < 4096, "the walk read " + touched[0] + " of 4096 rows");
    }

    /// <summary>
    /// An authored panel nested far past anything the game lays out still reads in full: the guard
    /// is a guard, not a budget a caller has to meet.
    /// </summary>
    [Fact]
    public void A_deeply_nested_authored_panel_still_reads_in_full()
    {
        var node = new TooltipNode("row 30");
        for (var level = 29; level >= 0; level--)
            node = new TooltipNode("row " + level) { children = new List<TooltipNode> { node } };

        var text = (string?)Projected(new FakeTooltip("Nested", node))["text"] ?? string.Empty;

        for (var level = 0; level <= 30; level++)
            Assert.Contains("row " + level, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The projection of a tooltip whose walk finishes. Every fixture below is one, so each of them
    /// asserts the walk came back on its own rather than leaving that to the tests that cross the
    /// bound deliberately.
    /// </summary>
    private static JObject Projected(
        ITooltipable primary,
        IEnumerable<ITooltipable>? subTooltips = null,
        bool usingAlt = false,
        Func<ITooltipable, Guid>? identity = null)
    {
        Assert.True(
            GameMcpTooltipProjector.TryProject(
                primary, subTooltips, usingAlt, identity, out var details),
            "the walk gave up on a fixture that is inside its bound");
        return GameMcpTestHarness.Json(details);
    }

    /// <summary>The one binding the suite's own identity reader would have taken for this link.</summary>
    private static Func<ITooltipable, Guid> Identity(ITooltipable link, Guid id) =>
        candidate => ReferenceEquals(candidate, link) ? id : Guid.Empty;

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
    /// A link that counts what the walk asked it for. A pointer costs the link its name and nothing
    /// else; a panel would have cost it its description and its whole node tree.
    /// </summary>
    private sealed class CountingTooltip : ITooltipable
    {
        private readonly List<TooltipNode> _nodes = new();

        internal CountingTooltip(string name, params TooltipNode[] nodes)
        {
            Name = name;
            _nodes.AddRange(nodes);
        }

        private string Name { get; }
        internal int NodeReads { get; private set; }
        internal int DescriptionReads { get; private set; }

        public string GetName() => Name;
        public string GetDisplayType() => "Fixture";
        public UnityEngine.Sprite GetIcon() => new();
        public UnityEngine.Color GetColor() => UnityEngine.Color.white;
        public bool IsColoredIcon() => false;
        public bool HasAltTooltips() => false;

        public string GetDescription()
        {
            DescriptionReads++;
            return Name + " description";
        }

        public List<TooltipNode> GetTooltipNodes()
        {
            NodeReads++;
            return _nodes;
        }

        public List<TooltipNode> GetAltTooltipNodes()
        {
            NodeReads++;
            return new List<TooltipNode>();
        }
    }

    /// <summary>A link that mints a fresh object every time the game builds the row it hangs on.</summary>
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
            new TooltipNode(string.Empty) { tooltipable = new EndlessWrapperTooltip(_minted) },
        };
    }

    /// <summary>A node tree with no end and nothing to say.</summary>
    private sealed class EndlessChildrenTooltip : ITooltipable
    {
        public string GetName() => string.Empty;
        public string GetDisplayType() => string.Empty;
        public string GetDescription() => string.Empty;
        public UnityEngine.Sprite GetIcon() => new();
        public UnityEngine.Color GetColor() => UnityEngine.Color.white;
        public bool IsColoredIcon() => false;
        public bool HasAltTooltips() => false;
        public List<TooltipNode> GetAltTooltipNodes() => new();

        public List<TooltipNode> GetTooltipNodes()
        {
            var node = new TooltipNode(string.Empty);
            node.children.Add(node);
            return new List<TooltipNode> { node };
        }
    }
}
