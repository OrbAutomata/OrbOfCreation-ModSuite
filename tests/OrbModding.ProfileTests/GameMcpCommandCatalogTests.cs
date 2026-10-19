using System.Collections.Generic;
using System.Linq;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpCommandCatalogTests
{
    [Fact]
    public void GameplayKindWithoutPostStateRuleRefusesStart()
    {
        var shipping = GameMcpCommandCatalog.Shipping;
        var research = shipping.Commands.Single(
            command => command.Kind == GameMcpCommandKind.Research);
        var commands = shipping.Commands
            .Select(command => command != research
                ? command
                : new GameMcpCommandDescriptor(research.Kind, research.Execute, postState: null))
            .ToArray();
        var errors = new List<string>();

        var server = GameMcpHttpServer.TryStart(
            new GameMcpFrameInbox(),
            GameMcpToolCatalog.Shipping,
            new GameMcpCommandCatalog(commands),
            _ => { },
            errors.Add,
            GameMcpTestHarness.FreeLoopbackPort());
        server?.Dispose();

        Assert.Null(server);
        var error = Assert.Single(errors);
        Assert.Contains("refuses to start", error);
        Assert.Contains("command Research has no post-state rule", error);
    }

    [Fact]
    public void NonGameplayKindWithPostStateRuleRefusesStart()
    {
        var shipping = GameMcpCommandCatalog.Shipping;
        var screenshot = shipping.Commands.Single(
            command => command.Kind == GameMcpCommandKind.Screenshot);
        var commands = shipping.Commands
            .Select(command => command != screenshot
                ? command
                : new GameMcpCommandDescriptor(
                    screenshot.Kind, screenshot.ExecuteInFrame, GameMcpPostStateRule.FromPress))
            .ToArray();
        var errors = new List<string>();

        var server = GameMcpHttpServer.TryStart(
            new GameMcpFrameInbox(),
            GameMcpToolCatalog.Shipping,
            new GameMcpCommandCatalog(commands),
            _ => { },
            errors.Add,
            GameMcpTestHarness.FreeLoopbackPort());
        server?.Dispose();

        Assert.Null(server);
        var error = Assert.Single(errors);
        Assert.Contains("refuses to start", error);
        Assert.Contains(
            "command Screenshot is no gameplay action, but declares a post-state rule", error);
    }

    [Fact]
    public void TheShippingCommandCatalogPassesTheStartCheck() =>
        Assert.Empty(GameMcpCommandCatalog.Shipping.Problems());
}
