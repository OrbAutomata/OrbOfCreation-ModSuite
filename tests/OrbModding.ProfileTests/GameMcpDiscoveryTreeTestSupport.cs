using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbModding.ProfileTests;

internal static class GameMcpDiscoveryTreeTestSupport
{
    internal static GameWorldState DiscoveryWorld(
        WorldDiscoveryTree tree,
        WorldTimeRune[]? timeRunes = null,
        WorldGlyph[]? glyphs = null,
        EntityIdentityName[]? identities = null) =>
        new()
        {
            DiscoveryTrees = PublicationTable<WorldDiscoveryTree>.Create(new[] { tree }),
            EntityIdentities = identities is null
                ? EntityIdentityCatalogSnapshot.Unbound(1)
                : EntityIdentityCatalogSnapshot.Bound(1, identities),
            TimeRunes = timeRunes is null
                ? PublicationTable<WorldTimeRune>.Empty
                : PublicationTable<WorldTimeRune>.Create(timeRunes),
            AugmentGlyphs = glyphs is null
                ? PublicationTable<WorldGlyph>.Empty
                : PublicationTable<WorldGlyph>.Create(glyphs),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "discovery-trees",
                    WorldCategoryOutcome.Collected,
                    sampled: 1,
                    skipped: 0,
                    firstFailure: string.Empty),
                new WorldCollectionCategoryStatus(
                    "time runes",
                    WorldCategoryOutcome.Collected,
                    sampled: timeRunes?.Length ?? 0,
                    skipped: 0,
                    firstFailure: string.Empty),
                new WorldCollectionCategoryStatus(
                    "augment glyphs",
                    WorldCategoryOutcome.Collected,
                    sampled: glyphs?.Length ?? 0,
                    skipped: 0,
                    firstFailure: string.Empty),
            }),
            CollectedAtEpoch = 7,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
}
