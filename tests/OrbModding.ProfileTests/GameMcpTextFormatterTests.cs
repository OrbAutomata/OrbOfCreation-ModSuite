using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// What the player reads, without the layout the game wrapped it in.
/// </summary>
/// <remarks>
/// The list of tags is closed so that prose holding an angle bracket survives, which means it has to
/// hold every word the pinned build authors. A census of every lowercase tag in
/// <c>data/game-data.json</c> found eight, and four of them were missing — <c>lore</c> with 332
/// uses, <c>emph2</c> with 168, <c>negative</c> with 4 and <c>positive</c> with 2 — so 506 authored
/// spans shipped their markup to a caller reading what the tooltip verbs advertise as plain screen
/// text.
/// </remarks>
public sealed class GameMcpTextFormatterTests
{
    [Theory]
    [InlineData("emph")]
    [InlineData("emph2")]
    [InlineData("deemph")]
    [InlineData("warn")]
    [InlineData("lore")]
    [InlineData("negative")]
    [InlineData("positive")]
    [InlineData("color")]
    public void Every_tag_the_pinned_build_authors_is_layout_and_not_words(string tag)
    {
        Assert.Equal(
            "the bonus resources this makes",
            GameMcpTextFormatter.Plain(
                "the bonus resources <" + tag + ">this</" + tag + "> makes"));
    }

    [Fact]
    public void An_authored_colour_and_a_bracket_that_is_prose_are_told_apart()
    {
        Assert.Equal("Psi", GameMcpTextFormatter.Plain("<#ef86cf>Psi</color>"));
        Assert.Equal("held <3 of them", GameMcpTextFormatter.Plain("held <3 of them"));
        Assert.Equal("a <thing> of ours", GameMcpTextFormatter.Plain("a <thing> of ours"));
    }
}
