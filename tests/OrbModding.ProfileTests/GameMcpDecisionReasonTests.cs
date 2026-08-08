using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// One sentence generator, and no surface that ships a code without one.
/// </summary>
/// <remarks>
/// Thirty-four blocked decision blocks published <c>reasonCode</c> alone against six that carried
/// prose. A caller that cannot read why a verb is shut has one way left to find out — fire it — and
/// that is the loop the pre-decision surface exists to spare an unattended strategist on a verb
/// that spends. The rule is enforced where every response already passes, so a block written
/// tomorrow cannot reintroduce it.
/// </remarks>
public sealed class GameMcpDecisionReasonTests
{
    [Fact]
    public void No_decision_ships_a_code_without_the_sentence_that_reads_it()
    {
        var encoded = GameMcpDocumentJsonEncoder.Encode(new GameMcpObjectBuilder
        {
            ["toggle"] = new GameMcpObjectBuilder
            {
                ["available"] = false,
                ["reasonCode"] = "not_available",
            },
            ["slots"] = new GameMcpArrayBuilder(
                new GameMcpObjectBuilder
                {
                    ["available"] = false,
                    ["reasonCode"] = "loadout_full",
                }.Freeze(),
                new GameMcpObjectBuilder
                {
                    ["remove"] = new GameMcpObjectBuilder
                    {
                        ["available"] = false,
                        ["reasonCode"] = "not_active",
                    },
                }.Freeze()),
        }.Freeze(), GameMcpTestHarness.EntityCatalog);

        var document = Assert.IsType<JObject>(encoded);
        var blocks = document.DescendantsAndSelf()
            .OfType<JObject>()
            .Where(item => item["reasonCode"] is not null)
            .ToArray();

        Assert.Equal(3, blocks.Length);
        Assert.All(blocks, block =>
            Assert.False(string.IsNullOrWhiteSpace((string?)block["reason"])));
        Assert.Equal(
            "The game has not unlocked this yet.",
            (string?)document["toggle"]!["reason"]);
        Assert.Equal(
            "Every slot in this loadout is in use.",
            (string?)document["slots"]![0]!["reason"]);
        Assert.Equal(
            "This is not active, so there is nothing to act on.",
            (string?)document["slots"]![1]!["remove"]!["reason"]);
    }

    /// <remarks>
    /// A site that holds the numbers writes the better sentence, and the fallback must not overwrite
    /// it — the shortfall names the resource, the price, and the holding, where the code alone can
    /// only say that something was short.
    /// </remarks>
    [Fact]
    public void A_sentence_written_from_the_numbers_survives_the_fallback()
    {
        var encoded = GameMcpDocumentJsonEncoder.Encode(new GameMcpObjectBuilder
        {
            ["available"] = false,
            ["reasonCode"] = "unaffordable",
            ["reason"] = "Needs 20 Arcana (have 1).",
        }.Freeze(), GameMcpTestHarness.EntityCatalog);

        Assert.Equal(
            "Needs 20 Arcana (have 1).",
            (string?)Assert.IsType<JObject>(encoded)["reason"]);

        // The ceiling codes are where producers most often hold the number, so the override has to
        // hold there too: the generic headroom sentence must not displace the live maximum.
        var bounded = GameMcpDocumentJsonEncoder.Encode(new GameMcpObjectBuilder
        {
            ["available"] = false,
            ["reasonCode"] = "amount_unavailable",
            ["reason"] = "The plot allows fewer than that.",
            ["maximumAmount"] = 3,
        }.Freeze(), GameMcpTestHarness.EntityCatalog);

        Assert.Equal(
            "The plot allows fewer than that.",
            (string?)Assert.IsType<JObject>(bounded)["reason"]);
    }

    /// <remarks>
    /// The world publishes a handful of reasons as its own free text rather than as a member of a
    /// closed set. Restating one is honest; dropping it puts a caller back in front of a bare code.
    /// </remarks>
    [Theory]
    [InlineData("not_available", "The game has not unlocked this yet.")]
    [InlineData("unaffordable", "The named resources fall short of the price.")]
    [InlineData(
        "amount_unavailable",
        "The game's own headroom for this is below what the call asked for.")]
    [InlineData("a_code_no_table_knows", "A code no table knows.")]
    [InlineData("", "The game does not admit this right now.")]
    public void Every_code_reads_as_a_sentence(string reasonCode, string expected) =>
        Assert.Equal(expected, GameMcpDecisionReason.For(reasonCode));

    [Fact]
    public void The_shortfall_names_every_resource_that_is_actually_short()
    {
        var sentence = GameMcpDecisionReason.Shortfall(new[]
        {
            ("Arcana", new BigDouble(20), new BigDouble(1)),
            ("Orb Advancement", new BigDouble(4), BigDouble.Zero),
        });

        Assert.Equal("Needs 20 Arcana (have 1); 4 Orb Advancement (have 0).", sentence);
    }
}
