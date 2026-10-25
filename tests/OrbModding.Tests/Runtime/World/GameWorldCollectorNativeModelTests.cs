using System;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

public sealed class GameWorldCollectorNativeModelTests
{
    [Fact]
    public void GlyphLearningUsesTheGamesDiscoveredOrPrerequisiteAvailabilityContract()
    {
        var glyph = new GlyphSO
        {
            discoverable = true,
            discovered = false,
            NativeAvailable = true,
        };
        GlyphSO.All.Add(glyph);
        var collector = new GameWorldCollector();
        collector.Collect();
        Assert.True(WorldLookup.TryFind(collector.Build().AugmentGlyphs, glyph.GetGuid(), out var unlearned));
        Assert.False(unlearned.Learned);
        Assert.False(glyph.IsVisible());

        glyph.discovered = true;
        collector.Collect();
        Assert.True(WorldLookup.TryFind(collector.Build().AugmentGlyphs, glyph.GetGuid(), out var learned));
        Assert.True(learned.Learned);
        Assert.True(glyph.IsVisible());

        glyph.discoverable = false;
        glyph.discovered = false;
        glyph.NativeAvailable = false;
        glyph.prerequisites.NativeCheckResult = true;
        collector.Collect();
        Assert.True(WorldLookup.TryFind(collector.Build().AugmentGlyphs, glyph.GetGuid(), out var prerequisiteLearned));
        Assert.True(prerequisiteLearned.Learned);
    }
}
