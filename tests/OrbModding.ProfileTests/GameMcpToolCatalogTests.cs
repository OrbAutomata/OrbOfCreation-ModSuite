using System.Collections.Generic;
using System.Linq;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpToolCatalogTests
{
    [Fact]
    public void MutatingToolClassifiedReadOnlyRefusesServerStart()
    {
        var shipping = GameMcpToolCatalog.Shipping;
        var purchase = shipping.Tools.Single(tool => tool.Name == "game_purchase");
        var tools = shipping.Tools
            .Select(tool => tool != purchase
                ? tool
                : new GameMcpToolDescriptor(
                    purchase.Name,
                    purchase.Bind,
                    GameMcpToolRoute.Command(
                        GameMcpOperationClass.ReadOnly,
                        GameMcpFrameData.World | GameMcpFrameData.Configuration,
                        GameMcpCommandKind.Purchase)))
            .ToArray();
        var errors = new List<string>();

        var server = GameMcpHttpServer.TryStart(
            new GameMcpFrameInbox(),
            new GameMcpToolCatalog(tools, shipping.Resources),
            _ => { },
            errors.Add,
            GameMcpTestHarness.FreeLoopbackPort());
        server?.Dispose();

        Assert.Null(server);
        var error = Assert.Single(errors);
        Assert.Contains("refuses to start", error);
        Assert.Contains("game_purchase declares Purchase ReadOnly", error);
    }

    [Fact]
    public void TheShippingCatalogPassesTheStartCheck() =>
        Assert.Empty(GameMcpToolCatalog.Shipping.Problems(
            GameMcpProtocolRouter.AdvertisedToolNames()));
}
