using System;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime.Configuration;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

public sealed class WorldPipelineRegressionTests : IDisposable
{
    public WorldPipelineRegressionTests() => Reset();
    public void Dispose() => Reset();

    [Fact]
    public void ThirdLevelOrGateWithOnlyCHeldIsMetThroughCollectorAndEvaluator()
    {
        var owner = Upgrade(0);
        var resource = new global::ResourceSO();
        global::ResourceSO.All.Add(resource);
        owner.resourceCost.costs.Add(new global::ResourceTuple(resource, new BigDouble(1d)));
        var a = Upgrade(0);
        var b = Upgrade(0);
        var c = Upgrade(1);
        var d = Upgrade(0);
        // B requires zero levels: only C is held, but B is independently satisfied.
        owner.prerequisitesPerLevel.prerequisites.Add(new Requirements.OrRequirement
        {
            orConditions =
            {
                AtLeast(a, 1),
                new Requirements.AndRequirement
                {
                    andConditions =
                    {
                        AtLeast(b, 0),
                        new Requirements.OrRequirement { orConditions = { AtLeast(c, 1), AtLeast(d, 1) } },
                    },
                },
            },
        });

        var world = Collect(out _);
        Assert.Equal(WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(world, owner.GetGuid(), 1));
        Assert.True(ProjectPurchase(world).MeetsNextLevelRequirements);

        c.level = 0;
        world = Collect(out _);
        Assert.Equal(WorldRequirementVerdict.Unmet,
            WorldRequirementEvaluator.Evaluate(world, owner.GetGuid(), 1));
        Assert.False(ProjectPurchase(world).MeetsNextLevelRequirements);
    }

    [Fact]
    public void LevelZeroConsumableCountIsReportedAsSkippedWithoutDroppingValidCounts()
    {
        var carry = new global::IntVariable();
        global::IntVariable.All.Add(carry);
        var type = new global::ConsumableTypeSO { maximumCarryLoad = carry };
        var globalTypeId = new Guid("315471ca-0d15-455d-92da-f9d5f95a3c33");
        type.SetGuid(globalTypeId);
        global::IdScriptableObject.RuntimeLookup[globalTypeId] = type;
        var consumable = new global::ConsumableSO();
        consumable.consumableCounts.Add(new global::ConsumableCount { Level = 0, Quantity = 2 });
        consumable.consumableCounts.Add(new global::ConsumableCount { Level = 3, Quantity = 1 });
        global::ConsumableSO.All.Add(consumable);

        var world = Collect(out var report);

        var category = report.For("consumables");
        Assert.Equal(1, category.Skipped);
        Assert.Contains("count", category.FirstFailure, StringComparison.Ordinal);
        Assert.Equal(1, world.ConsumableCounts.Count);
        Assert.Equal(3, world.ConsumableCounts[0].Level);
    }

    private static AutoBuyCandidateRow ProjectPurchase(GameWorldState world)
    {
        var config = new SuiteRuntimeConfiguration
        {
            AutoBuy = new AutoBuyConfiguration { IncludeStructures = false, IncludeUpgrades = true },
        };
        var frame = default(AutoBuyCycleFrame);
        AutoBuyFrameProjector.Project(ref frame, in config, world);
        return Assert.Single(frame.Candidates.ToArray());
    }

    private static global::UpgradeSO Upgrade(int level)
    {
        var value = new global::UpgradeSO { level = level, maxLevel = -1 };
        global::UpgradeSO.All.Add(value);
        return value;
    }

    private static Requirements.UpgradeRequirement AtLeast(global::UpgradeSO item, int level) => new()
    {
        item = item,
        reqType = Requirements.UpgradeRequirementType.AtLeast,
        value = new Requirements.LeveledValue { baseValue = level },
    };

    private static GameWorldState Collect(out WorldCollectionReport report)
    {
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
        report = new GameWorldCollector().Collect(frame);
        return GameWorldFrameDeriver.Build(frame);
    }

    private static void Reset()
    {
        OrbModding.GameStubs.GameStubState.Reset();
    }
}
