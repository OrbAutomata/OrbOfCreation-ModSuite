using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

public sealed class GameWorldCollectorKeywordTests : GameWorldCollectorTestBase
{

    /// <summary>
    /// A keyword on an entity that throws unexpectedly faults the pass rather than costing the
    /// entity's keyword rows.
    /// </summary>
    [Fact]
    public void AnUnexpectedThrowReadingAnEntityKeywordFaultsThePass()
    {
        var failure = new NullReferenceException("keyword was destroyed");
        StructureSO.All.Add(new StructureSO
        {
            structureSubTypes = { new StructureTypeSO { GetGuidFailure = failure } },
        });

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// A structure publishes every word its tooltip shows, not only its primary type.
    /// </summary>
    /// <remarks>
    /// This is the first of the two bindings the keyword census found <em>lossy</em> rather than
    /// merely narrow: the tooltip prints <c>GetAllTypes()</c>, which is the subtypes prepended with
    /// the primary type, while the suite bound the primary type alone. On the pinned build that cost
    /// 138 of 180 structures one or two of their displayed words.
    /// </remarks>
    [Fact]
    public void AStructurePublishesItsPrimaryTypeAndEverySubtype()
    {
        var primary = new StructureTypeSO();
        var firstSub = new StructureTypeSO();
        var secondSub = new StructureTypeSO();
        var structure = new StructureSO
        {
            structureType = primary,
            structureSubTypes = { firstSub, secondSub },
            Available = true,
        };
        StructureSO.All.Add(structure);

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldEntityKeywordLookup.TryFind(
            world.EntityKeywords, structure.Identity, out var start, out var count));
        Assert.Equal(3, count);
        var rows = new List<(WorldKeywordSource Source, int Ordinal, Guid Keyword)>();
        for (var index = start; index < start + count; index++)
        {
            var row = world.EntityKeywords[index];
            Assert.Equal(WorldKeywordOwnerKind.Structure, row.OwnerKind);
            rows.Add((row.Source, row.Ordinal, row.KeywordId));
        }

        Assert.Equal(
            new[]
            {
                (WorldKeywordSource.PrimaryType, 0, primary.Identity),
                (WorldKeywordSource.TypeList, 0, firstSub.Identity),
                (WorldKeywordSource.TypeList, 1, secondSub.Identity),
            },
            rows);
    }

    /// <summary>
    /// An alchemy recipe publishes both of its authored types, not only the core one.
    /// </summary>
    /// <remarks>
    /// The second lossy binding. <c>GetCoreType()</c> is <c>alchemyTypes.Last()</c>, and every recipe
    /// on the pinned build authors exactly two types, so binding the core type alone dropped one word
    /// from all 125 of them. The core-type binding stays where it is — it is a real game concept with
    /// its own consumers — and the full list is published alongside it.
    /// </remarks>
    [Fact]
    public void AnAlchemyRecipePublishesEveryAuthoredTypeNotOnlyItsCore()
    {
        var first = new AlchemyTypeSO();
        var core = new AlchemyTypeSO();
        AlchemyTypeSO.All.AddRange(new[] { first, core });
        var recipe = new AlchemyRecipeSO
        {
            alchemyTypes = { first, core },
            coreType = core,
        };
        AlchemyRecipeSO.All.Add(recipe);

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldEntityKeywordLookup.TryFind(
            world.EntityKeywords, recipe.Identity, out var start, out var count));
        Assert.Equal(2, count);
        Assert.Equal(first.Identity, world.EntityKeywords[start].KeywordId);
        Assert.Equal(core.Identity, world.EntityKeywords[start + 1].KeywordId);
        Assert.True(WorldLookup.TryFind(world.AlchemyRecipes, recipe.Identity, out var published));
        Assert.Equal(core.Identity, published.CoreTypeId);
    }

    /// <summary>
    /// A class whose type list nothing bound before is published whole.
    /// </summary>
    /// <remarks>
    /// Rituals stand in for the eleven classes the suite could not see any keyword of at all. The
    /// prior census reported that rituals carry no types; they carry <c>ritualTypes</c>, and every
    /// ritual on the pinned build has one.
    /// </remarks>
    [Fact]
    public void APreviouslyUnboundClassPublishesItsAuthoredKeywords()
    {
        var strength = new RitualTypeSO();
        var ritual = new RitualSO { ritualTypes = { strength } };
        RitualSO.All.Add(ritual);

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldEntityKeywordLookup.TryFind(
            world.EntityKeywords, ritual.Identity, out var start, out var count));
        Assert.Equal(1, count);
        Assert.Equal(WorldKeywordOwnerKind.Ritual, world.EntityKeywords[start].OwnerKind);
        Assert.Equal(strength.Identity, world.EntityKeywords[start].KeywordId);
    }

    /// <summary>
    /// One unreadable member withholds the whole keyword category and names what failed.
    /// </summary>
    /// <remarks>
    /// A keyword table that is silently short one class reads exactly like a table whose entities
    /// genuinely have no keywords, and the census proved that telling those two apart is the whole
    /// question. So the category fails closed rather than publishing a partial vocabulary.
    /// </remarks>
    [Fact]
    public void OneUnreadableTypeListWithholdsEveryKeyword()
    {
        var collector = Collector(("RitualSO", typeof(RitualMissingKeywords)));

        var report = collector.Collect();

        var keywords = report.For("entity keywords");
        Assert.Equal(WorldCategoryOutcome.Unavailable, keywords.Outcome);
        Assert.Contains("ritualTypes", keywords.FirstFailure);
        Assert.Empty(collector.Build().EntityKeywords.AsSpan().ToArray());
    }
}
