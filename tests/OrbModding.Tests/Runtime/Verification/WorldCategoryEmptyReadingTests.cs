using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Runtime.Verification;

/// <summary>
/// What the check says about a bound collector that read nothing. Two of them count something other
/// than entities in the world, and calling those shortfalls sent two rounds hunting a defect that
/// was the build working as authored.
/// </summary>
public sealed class WorldCategoryEmptyReadingTests
{
    /// <summary>
    /// Targeting samples an open native request, and no prompt is open while a check runs. The
    /// sentence says which, so nobody reads zero as lost targets.
    /// </summary>
    [Fact]
    public void An_event_scoped_category_says_it_samples_only_while_a_request_is_open()
    {
        Assert.Equal(
            "samples only while a native targeting request is open, and none was",
            AutomataWorldCollectionCheck.EmptySampleIsExpected("targeting"));
    }

    /// <summary>
    /// The loud gap is the default, so a category nobody has ruled on is reported rather than
    /// excused — the direction that costs an investigation is the quiet one.
    /// </summary>
    [Theory]
    [InlineData("resources")]
    [InlineData("attributes")]
    [InlineData("spell recipes")]
    [InlineData("a category nobody has ruled on")]
    public void Every_other_category_keeps_the_loud_gap(string category)
    {
        Assert.Equal(string.Empty, AutomataWorldCollectionCheck.EmptySampleIsExpected(category));
    }
}
