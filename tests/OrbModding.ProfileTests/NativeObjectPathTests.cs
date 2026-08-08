using OrbModConfig;
using UnityEngine;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// One screen's tooltip paths descend from one canvas, so the catalog says the shared part once.
/// </summary>
/// <remarks>
/// The tooltip catalog was the round's largest text payload, and most of it was the same Unity
/// hierarchy prefix repeated verbatim on thirty consecutive rows. The prefix is derived from the
/// live screen on both the catalog and the read side, so a row's path resolves as handed out.
/// </remarks>
public sealed class NativeObjectPathTests
{
    [Fact]
    public void The_shared_leading_path_is_said_once()
    {
        var prefix = NativeObjectPath.CommonPrefix(new[]
        {
            "Canvas[0]/HUD[1]/Panel[0]/Row[0]/Button[0]",
            "Canvas[0]/HUD[1]/Panel[0]/Row[1]/Button[0]",
            "Canvas[0]/HUD[1]/Panel[0]/Header[2]",
        });

        Assert.Equal("Canvas[0]/HUD[1]/Panel[0]", prefix);
        Assert.Equal(
            "Row[1]/Button[0]",
            NativeObjectPath.Relative("Canvas[0]/HUD[1]/Panel[0]/Row[1]/Button[0]", prefix));
    }

    /// <remarks>
    /// Sibling indices are what make repeated clone rows addressable, so the prefix stops at whole
    /// segments — a shared spelling inside one segment is not a shared ancestor.
    /// </remarks>
    [Fact]
    public void A_shared_spelling_inside_one_segment_is_not_a_shared_ancestor()
    {
        var prefix = NativeObjectPath.CommonPrefix(new[]
        {
            "Canvas[0]/Slot[0]",
            "Canvas[0]/Slot[1]",
        });

        Assert.Equal("Canvas[0]", prefix);
    }

    /// <remarks>
    /// A prefix that swallowed a whole path would hand a caller an empty handle, so it always
    /// leaves the shortest listed path one segment of its own.
    /// </remarks>
    [Fact]
    public void Every_row_keeps_a_segment_of_its_own()
    {
        var prefix = NativeObjectPath.CommonPrefix(new[]
        {
            "Canvas[0]/HUD[1]",
            "Canvas[0]/HUD[1]/Panel[0]",
        });

        Assert.Equal("Canvas[0]", prefix);
        Assert.Equal("HUD[1]", NativeObjectPath.Relative("Canvas[0]/HUD[1]", prefix));
    }

    [Fact]
    public void Screens_that_share_no_ancestor_keep_their_whole_paths()
    {
        var prefix = NativeObjectPath.CommonPrefix(new[]
        {
            "Canvas[0]/HUD[1]/Panel[0]",
            "Overlay[3]/Modal[0]/Panel[0]",
        });

        Assert.Equal(string.Empty, prefix);
        Assert.Equal(
            "Canvas[0]/HUD[1]/Panel[0]",
            NativeObjectPath.Relative("Canvas[0]/HUD[1]/Panel[0]", prefix));
    }

    [Fact]
    public void An_empty_screen_has_no_prefix() =>
        Assert.Equal(string.Empty, NativeObjectPath.CommonPrefix(System.Array.Empty<string>()));

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
