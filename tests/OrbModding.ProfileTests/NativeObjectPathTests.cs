using OrbModConfig;
using UnityEngine;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// A screen's tooltip elements hang off a handful of panels, and the catalog pages by panel: one
/// stretch of consecutive siblings under one parent is one unit of a page.
/// </summary>
/// <remarks>
/// The paths are derived from the live screen on both the catalog and the read side, so an address
/// a row was handed resolves back to the element it names — matched at a segment boundary, so a
/// longer sibling name never answers for a shorter one.
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
    }

    [Fact]
    public void An_empty_screen_has_no_panels() =>
        Assert.Empty(NativeObjectPath.Runs(System.Array.Empty<string>()));

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
