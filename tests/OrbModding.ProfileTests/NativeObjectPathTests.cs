using OrbModConfig;
using UnityEngine;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// A screen's tooltip elements hang off a handful of panels, so the catalog says each panel's
/// ancestry once and its elements carry only their own last segment.
/// </summary>
/// <remarks>
/// The tooltip catalog was the round's largest text payload, and most of it was the same Unity
/// hierarchy path repeated verbatim on consecutive rows. One prefix over a mixed page is only as
/// deep as its most distant pair of rows, so a page spanning three panels factored out a canvas
/// name and left every row holding its own panel's ancestry in full. The paths are derived from the
/// live screen on both the catalog and the read side, so a row's path resolves as handed out.
/// </remarks>
public sealed class NativeObjectPathTests
{
    [Fact]
    public void Each_stretch_of_siblings_says_its_own_parent_once()
    {
        var paths = new[]
        {
            "Canvas[0]/HUD[1]/Glyphs[0]/Item[0]",
            "Canvas[0]/HUD[1]/Glyphs[0]/Item[1]",
            "Canvas[0]/HUD[1]/Spells[2]/Button[0]",
        };

        var runs = NativeObjectPath.Runs(paths);

        Assert.Equal(2, runs.Count);
        Assert.Equal("Canvas[0]/HUD[1]/Glyphs[0]", runs[0].Prefix);
        Assert.Equal(0, runs[0].Start);
        Assert.Equal(2, runs[0].Count);
        Assert.Equal("Canvas[0]/HUD[1]/Spells[2]", runs[1].Prefix);
        Assert.Equal(2, runs[1].Start);
        Assert.Equal(1, runs[1].Count);
        Assert.Equal("Item[1]", NativeObjectPath.Relative(paths[1], runs[0].Prefix));
        Assert.Equal("Button[0]", NativeObjectPath.Relative(paths[2], runs[1].Prefix));
    }

    /// <remarks>
    /// Sibling indices are what make repeated clone rows addressable, so a stretch is bounded by
    /// whole segments: two panels spelled alike are two panels, and one panel's rows never join the
    /// next panel's because their leaf names happen to match.
    /// </remarks>
    [Fact]
    public void A_panel_and_the_panel_beside_it_are_two_stretches()
    {
        var runs = NativeObjectPath.Runs(new[]
        {
            "Canvas[0]/List[0]/Item(Clone)[0]",
            "Canvas[0]/List[1]/Item(Clone)[0]",
        });

        Assert.Equal(2, runs.Count);
        Assert.Equal("Canvas[0]/List[0]", runs[0].Prefix);
        Assert.Equal("Canvas[0]/List[1]", runs[1].Prefix);
    }

    /// <remarks>
    /// A prefix that swallowed a whole path would hand a caller an empty handle, so an element with
    /// no ancestry left to factor keeps the whole of the path it was found at.
    /// </remarks>
    [Fact]
    public void A_top_level_element_keeps_its_whole_path()
    {
        var runs = NativeObjectPath.Runs(new[] { "Canvas[0]", "Overlay[3]" });

        Assert.Single(runs);
        Assert.Equal(string.Empty, runs[0].Prefix);
        Assert.Equal(2, runs[0].Count);
        Assert.Equal("Canvas[0]", NativeObjectPath.Relative("Canvas[0]", runs[0].Prefix));
    }

    [Fact]
    public void An_empty_screen_has_no_panels() =>
        Assert.Empty(NativeObjectPath.Runs(System.Array.Empty<string>()));

    /// <summary>
    /// One page, one root: the ancestry every panel on the page hangs off is said once at the top,
    /// each panel says only what the root did not, and every absolute path is those two and the
    /// element's own tail joined in that order. Nothing is lost, and nothing is said twice.
    /// </summary>
    /// <remarks>
    /// A round measured a third of the whole tooltip surface as address rather than content, and
    /// half of that was this one repetition: every panel prefix on a screen opens with the same
    /// canvas and content area, and on a list screen it goes far deeper than that. The rule is
    /// pinned by rebuilding the absolute paths from what the wire carries and comparing them to the
    /// paths the screen was read at, because a page that saves bytes by losing an address is not a
    /// saving — it is a catalog whose rows the read verb cannot resolve.
    /// </remarks>
    [Fact]
    public void A_page_says_its_root_once_and_every_row_still_rebuilds_its_whole_path()
    {
        var paths = new[]
        {
            "Canvas[0]/Content[2]/Magic[0]/Spells[1]/Row[0]",
            "Canvas[0]/Content[2]/Magic[0]/Spells[1]/Row[1]",
            "Canvas[0]/Content[2]/Magic[0]/Glyphs[3]/Row[0]",
            "Canvas[0]/Content[2]/Header[4]/Title[0]",
        };

        var panels = NativeObjectPath.Runs(paths);
        var prefixes = new System.Collections.Generic.List<string>();
        for (var index = 0; index < panels.Count; index++) prefixes.Add(panels[index].Prefix);
        var root = NativeObjectPath.CommonPrefix(prefixes);

        Assert.Equal("Canvas[0]/Content[2]", root);

        // What the wire carries: the root once, then per panel the part of its own prefix the root
        // did not say, then per element the part of its path its panel did not say.
        var rebuilt = new System.Collections.Generic.List<string>();
        for (var index = 0; index < panels.Count; index++)
        {
            var panel = panels[index];
            var prefix = NativeObjectPath.Relative(panel.Prefix, root);
            for (var member = panel.Start; member < panel.Start + panel.Count; member++)
            {
                rebuilt.Add(panel.Count == 1

                    // A panel holding one element has no ancestry to name apart from that element,
                    // so it says the element's own tail against the root and no prefix line at all.
                    ? Join(root, NativeObjectPath.Relative(paths[member], root))
                    : Join(Join(root, prefix), NativeObjectPath.Relative(paths[member], panel.Prefix)));
            }
        }

        Assert.Equal(paths, rebuilt);
        Assert.Equal(
            new[] { "Magic[0]/Spells[1]", "Magic[0]/Glyphs[3]", "Header[4]" },
            new[]
            {
                NativeObjectPath.Relative(panels[0].Prefix, root),
                NativeObjectPath.Relative(panels[1].Prefix, root),
                NativeObjectPath.Relative(panels[2].Prefix, root),
            });

        // A page whose panels share nothing above them says no root, and the rows are unchanged.
        Assert.Equal(
            string.Empty,
            NativeObjectPath.CommonPrefix(new[] { "Canvas[0]/A[0]", "Overlay[1]/B[0]" }));
    }

    private static string Join(string head, string tail) =>
        head.Length == 0 ? tail : tail.Length == 0 ? head : head + "/" + tail;

    /// <summary>
    /// A catalog page factors the ancestry each panel's rows share, so a row is handed the tail its
    /// own panel did not already say. The read verb therefore resolves a row by that tail — at a
    /// segment boundary, so a longer sibling name never answers for a shorter one.
    /// </summary>
    [Fact]
    public void A_row_addresses_its_element_by_the_tail_the_page_handed_out()
    {
        const string path = "Canvas[0]/HUD[1]/Panel[0]/Row[1]";

        Assert.True(NativeObjectPath.Addresses(path, path));
        Assert.True(NativeObjectPath.Addresses(path, "Panel[0]/Row[1]"));
        Assert.True(NativeObjectPath.Addresses(path, "Row[1]"));
        Assert.False(NativeObjectPath.Addresses(path, "ow[1]"));
        Assert.False(NativeObjectPath.Addresses(path, "Row[0]"));
        Assert.False(NativeObjectPath.Addresses("Canvas[0]/NarrowRow[1]", "Row[1]"));
    }

    /// <summary>
    /// One walk of an element's ancestry answers both questions the tooltip catalog asks of it.
    /// </summary>
    /// <remarks>
    /// The catalog used to walk the same chain three times per element — once to sort, once to
    /// find the shared prefix, once to print the row. This pins that the single walk still returns
    /// the selector <see cref="NativeObjectPath.BuildIndexed"/> returns, and an order key that
    /// sorts by sibling index rather than by name.
    /// </remarks>
    [Fact]
    public void One_walk_answers_both_the_selector_and_the_screen_order()
    {
        var canvas = new GameObject("Canvas");
        var zulu = Child(canvas, "Zulu");
        var alpha = Child(canvas, "Alpha");

        var first = NativeObjectPath.Locate(zulu);
        var second = NativeObjectPath.Locate(alpha);

        Assert.Equal(NativeObjectPath.BuildIndexed(zulu), first.Path);
        Assert.Equal(NativeObjectPath.BuildIndexed(alpha), second.Path);
        Assert.Equal("Canvas[0]/Zulu[0]", first.Path);
        Assert.Equal("Canvas[0]/Alpha[1]", second.Path);

        // Screen order is where the element sits, not how it is spelled: Zulu is the first child
        // and sorts first, which an ordinal sort of the two paths would get backwards.
        Assert.True(
            string.CompareOrdinal(first.OrderKey, second.OrderKey) < 0,
            $"{first.OrderKey} should sort before {second.OrderKey}");
        Assert.True(string.CompareOrdinal(first.Path, second.Path) > 0);
    }

    private static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent.transform, worldPositionStays: false);
        return child;
    }
}
