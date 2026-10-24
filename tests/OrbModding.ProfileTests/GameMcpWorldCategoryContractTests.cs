using System;
using System.Collections.Generic;
using System.Linq;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

[Collection(NativeRegistryCollection.Name)]
public sealed class GameMcpWorldCategoryContractTests
{
    [Fact]
    public void IncompleteCategoryRefusesTheShippingServerStart()
    {
        // Exercise the actual shipping roster and listener gate. This collection excludes parallel
        // registry users, and the original entry is restored even if listener startup throws.
        var entries = Assert.IsType<GameMcpEntityCapabilityDescriptor[]>(
            GameMcpEntityCapabilityMap.Entries);
        var index = Array.FindIndex(entries, entry => entry.Category == "resources");
        Assert.True(index >= 0);
        var original = entries[index];
        var errors = new List<string>();
        try
        {
            entries[index] = new GameMcpEntityCapabilityDescriptor(
                original.Category, original.ExpectedNativeType, original.Capabilities.ToArray());
            using var server = GameMcpHttpServer.TryStart(
                new GameMcpFrameInbox(), GameMcpToolCatalog.Shipping,
                GameMcpCommandCatalog.Shipping, _ => { }, errors.Add,
                GameMcpTestHarness.FreeLoopbackPort());

            Assert.Null(server);
            Assert.Contains(errors, error =>
                error.Contains("refuses to start", StringComparison.Ordinal) &&
                error.Contains("category resources has no table", StringComparison.Ordinal));
        }
        finally
        {
            entries[index] = original;
        }
    }
}
