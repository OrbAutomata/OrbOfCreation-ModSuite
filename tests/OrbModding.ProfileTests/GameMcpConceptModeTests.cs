using System;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// The concept verb's modes are the words the Concepts screen draws on its own buttons.
/// </summary>
/// <remarks>
/// The screen's button says Remove. <c>remove_owned</c> qualified it with a fact about the suite —
/// which quantity it thinks it owns — that the player pressing the button never sees, and the
/// neighbouring verb over the same screen's other list, <c>game_alchemy</c>, already spelled the
/// same press <c>remove</c>. There is no alias: the retired spelling is refused, and the refusal
/// names the one that works.
/// </remarks>
public sealed class GameMcpConceptModeTests
{
    private static readonly Guid RecipeId = Guid.Parse("f9000000-0000-0000-0000-000000000001");

    [Fact]
    public void The_screens_word_is_the_mode_and_the_retired_spelling_is_refused()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_concept");

        Assert.Equal(new[] { "add", "remove" },
            tool["inputSchema"]!["properties"]!["mode"]!["enum"]!.Values<string>());

        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var retired = router.Handle(GameMcpAcceptanceFixture.Request(1, "tools/call",
            new JObject
            {
                ["name"] = "game_concept",
                ["arguments"] = new JObject
                {
                    ["mode"] = "remove_owned",
                    ["uuid"] = RecipeId.ToString("D"),
                    ["amount"] = 1,
                },
            }));

        Assert.Equal(
            "refused (ERR_INPUT): mode must be one of: add, remove",
            GameMcpTestHarness.Page(retired));

        var landed = router.Handle(GameMcpAcceptanceFixture.Request(2, "tools/call",
            new JObject
            {
                ["name"] = "game_concept",
                ["arguments"] = new JObject
                {
                    ["mode"] = "remove",
                    ["uuid"] = RecipeId.ToString("D"),
                    ["amount"] = 1,
                },
            }));

        Assert.DoesNotContain(
            "mode must be one of", GameMcpTestHarness.Page(landed), StringComparison.Ordinal);
    }
}
