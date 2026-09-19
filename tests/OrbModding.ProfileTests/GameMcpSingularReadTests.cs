using System;
using System.Linq;
using Newtonsoft.Json;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// The singular read is the live read. <c>GetRow</c> answers one id and <c>GetRows</c> answers a
/// batch of them, and for one id they are the same block: the same category resolution, the same
/// fields, the same words. It used to be a second implementation of all of that, called only from
/// tests, so the shape those tests held to account was one no caller ever received.
/// </summary>
public sealed class GameMcpSingularReadTests
{
    /// <summary>Study Mind, a Concept round fifteen read off the Scholar screen.</summary>
    private static readonly Guid StudyMind =
        Guid.Parse("fa165240-42a6-447d-9d3b-f6fa2865dbf9");

    [Fact]
    public void One_id_reads_the_same_whether_it_is_asked_for_alone_or_in_a_batch()
    {
        var context = GameMcpTestHarness.Context(World(), generation: 5301);

        var single = GameMcpTestHarness.Json(
            GameMcpWorldQuery.GetRow(context, "concepts", StudyMind.ToString("D")));
        var batch = GameMcpTestHarness.Detail(context, "concepts", StudyMind);

        // The one difference the singular shape has, and the reason it has it: on the wire a block
        // that answered says nothing about having answered, because inside a batch that silence is
        // what separates it from the block beside it that refused. Alone there is nothing to
        // separate it from, so the word is said.
        Assert.Equal("available", (string?)single["status"]);
        Assert.Null(batch["status"]);

        single.Remove("status");
        Assert.Equal(
            batch.ToString(Formatting.None), single.ToString(Formatting.None));
        Assert.Equal("Study Mind", (string?)batch["name"]);
        Assert.Equal("concepts", (string?)batch["category"]);
    }

    private static GameWorldState World() => new()
    {
        CollectedAtEpoch = 53,
        CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        AlchemyRecipes = PublicationTable<WorldAlchemyRecipe>.Create(new[]
        {
            Recipe(StudyMind, KnownEntities.Reductive.Uuid),
        }),
        CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
        {
            new WorldCollectionCategoryStatus(
                "alchemy recipes", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
        }),
    };

    private static WorldAlchemyRecipe Recipe(Guid id, Guid coreTypeId) => new(
        id, coreTypeId, discovered: true, maxLevel: 1, advancementLevel: 0,
        discoveryRarityLevel: 0, masteryXp: BigDouble.Zero, masteryLevel: 0,
        recipeTime: BigDouble.One, isRequiredDiscovery: false,
        isCompletionRecipe: false, isAdvancementRecipe: false, completionTime: 0,
        isDebugAlchemy: false, power: BigDouble.Zero, speed: BigDouble.Zero,
        drainCostMod: BigDouble.Zero, special: BigDouble.Zero,
        timeReqMod: BigDouble.Zero, timeScalingMod: BigDouble.Zero,
        masteryXpRate: BigDouble.Zero, effectLevels: BigDouble.Zero,
        overdrivePower: BigDouble.Zero, overdriveSpeed: BigDouble.Zero,
        overdriveDrainCostMod: BigDouble.Zero, overdriveXpRate: BigDouble.Zero,
        freeUsageSlots: BigDouble.One, maxUsageSlots: new BigDouble(8),
        cachedCompletionTime: BigDouble.Zero, requiredExperience: BigDouble.One);
}
