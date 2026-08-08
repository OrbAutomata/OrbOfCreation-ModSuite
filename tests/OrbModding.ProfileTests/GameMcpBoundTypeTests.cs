using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using Xunit;
using static OrbModding.ProfileTests.GameMcpTestHarness;

namespace OrbModding.ProfileTests;

/// <summary>
/// One quantity, one JSON type. A key that can carry a <c>BigDouble</c> ships the game's
/// Scientific string everywhere; a key that can only carry a bounded cardinal ships a JSON number
/// everywhere; and a ceiling ships in the type of the value it caps, so a range never states its
/// two ends two ways.
/// </summary>
public sealed class GameMcpBoundTypeTests
{
    /// <summary>
    /// The refusal a player actually saw: <c>minimumAmount: 1</c> beside
    /// <c>maximumAmount: "240"</c>, one range in two types, because the ceiling was listed as a
    /// player magnitude while the floor was not.
    /// </summary>
    [Fact]
    public void A_range_states_both_of_its_ends_in_one_type()
    {
        var refusal = Json(new GameMcpObjectBuilder
        {
            ["status"] = "refused",
            ["reasonCode"] = "level_out_of_range",
            ["minimumAmount"] = 1,
            ["maximumAmount"] = 240,
        });

        Assert.Equal(JTokenType.Integer, refusal["minimumAmount"]!.Type);
        Assert.Equal(JTokenType.Integer, refusal["maximumAmount"]!.Type);
    }

    /// <summary>
    /// Every bound on an argument a caller sends back is a bounded cardinal, whichever tool
    /// produced it — the schema field it feeds accepts an integer and nothing else.
    /// </summary>
    [Theory]
    [InlineData("minimumAmount")]
    [InlineData("maximumAmount")]
    [InlineData("minimumSlot")]
    [InlineData("maximumSlot")]
    [InlineData("maximumDestination")]
    [InlineData("maximumAdditional")]
    [InlineData("maximumBatch")]
    [InlineData("minimum")]
    [InlineData("maximum")]
    public void An_argument_bound_is_a_json_number(string field)
    {
        var response = Json(new GameMcpObjectBuilder { [field] = 8 });

        Assert.Equal(JTokenType.Integer, response[field]!.Type);
    }

    /// <summary>
    /// The counterpart rule: a quantity of stuff keeps the game's own display shape even when one
    /// category happens to hold it in an <c>int</c>, so a caller never has to know which category
    /// it is reading to know what <c>amount</c> looks like — and a stock's ceiling matches it.
    /// </summary>
    [Fact]
    public void A_player_magnitude_and_its_ceiling_stay_in_the_games_own_shape()
    {
        var row = Json(new GameMcpObjectBuilder
        {
            ["amount"] = 16,
            ["maximumCarry"] = 16,
        });

        Assert.Equal(JTokenType.String, row["amount"]!.Type);
        Assert.Equal(JTokenType.String, row["maximumCarry"]!.Type);
        Assert.Equal("16", (string?)row["amount"]);
    }

    /// <summary>
    /// A magnitude big enough to need the exponent still gets it. The rule is about which
    /// vocabulary a key speaks, never about shortening the number.
    /// </summary>
    [Fact]
    public void A_magnitude_past_the_plain_range_still_ships_scientific()
    {
        var row = Json(new GameMcpObjectBuilder { ["cost"] = 1400000d });

        Assert.Equal("1.4e6", (string?)row["cost"]);
    }
}
