using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

public sealed class WorldDruidryLevelEconomyTests : IDisposable
{
    private static readonly Guid Mana = new("c0000000-0000-0000-0000-000000000001");
    public WorldDruidryLevelEconomyTests() => OrbModding.GameStubs.GameStubState.Reset();
    public void Dispose() => OrbModding.GameStubs.GameStubState.Reset();

    [Fact]
    public void ShippingPlotReaderProjectsAuthoredLevelEffectAndAccountsActualThrottle()
    {
        var (_, instance, _) = Seed();
        instance.resourceDrain.necessaryRatio = new BigDouble(.25);
        instance.resourceDrain.usageRatio = new BigDouble(.5);

        var quote = Collect();

        Assert.True(quote.Complete, quote.Reason);
        Assert.True(quote.NextComplete, quote.NextReason);
        Assert.True(quote.HasActiveWork);
        Assert.Equal(5d, Assert.Single(quote.Current.AsSpan().ToArray()).Amount.ToDouble());
        Assert.Equal(30d, Assert.Single(quote.Next.AsSpan().ToArray()).Amount.ToDouble());
        Assert.Equal(2, Player.Current.druidryOutputLevel.AsInt());
        Assert.Equal(0, Player.Current.druidryOutputLevel.SetCalls);
        Assert.Equal(20d, instance.resourceDrain.current.costs[0].GetValue().ToDouble());
    }

    [Fact]
    public void AuthoredStackingCostProjectsTheNativeExponentialLevelBranch()
    {
        var (_, instance, _) = Seed(stackingCost: true);

        var quote = Collect();

        Assert.True(quote.Complete, quote.Reason);
        Assert.True(quote.NextComplete, quote.NextReason);
        Assert.Equal(40d, Assert.Single(quote.Current.AsSpan().ToArray()).Amount.ToDouble());
        Assert.Equal(160d, Assert.Single(quote.Next.AsSpan().ToArray()).Amount.ToDouble());
        Assert.Equal(40d, instance.resourceDrain.current.costs[0].GetValue().ToDouble());
        Assert.Equal(2, Player.Current.druidryOutputLevel.AsInt());
        Assert.Equal(0, Player.Current.druidryOutputLevel.SetCalls);
    }

    [Fact]
    public void EngagedFreeActionKeepsACompleteForecastWithoutAppliedDrain()
    {
        var (_, instance, _) = Seed();
        instance.resourceDrain.isDrainApplied = false;
        instance.resourceDrain.current.costs.Clear();
        instance.GetAction()!.actionDrain.costs.Clear();

        var quote = Collect();

        Assert.True(quote.Complete, quote.Reason);
        Assert.True(quote.NextComplete, quote.NextReason);
        Assert.True(quote.HasActiveWork);
        Assert.Empty(quote.Current.AsSpan().ToArray());
        Assert.Empty(quote.Next.AsSpan().ToArray());
        Assert.Equal(0, Player.Current.druidryOutputLevel.SetCalls);
    }

    [Fact]
    public void MissingForecastTypePreservesRealDrainAndAllowsCurrentDangerReduction()
    {
        Seed();
        var quote = Collect(name => name == "PlotNodeActionSO+SizeConverter" ? null : Resolve(name));

        Assert.True(quote.Complete, quote.Reason);
        Assert.Equal(20d, quote.Current[0].Amount.ToDouble());
        Assert.False(quote.NextComplete);
        Assert.Contains("capture contracts", quote.NextReason);
        Assert.Equal(AutoLevelsDecisionKind.Lower, Decision(quote, totalDrain: 25).Kind);
    }

    [Fact]
    public void FirstUnsupportedForecastCannotHideLaterDominantActualAction()
    {
        var (_, _, type) = Seed();
        // The first action's native current is known, but its next effect contribution is stale.
        foreach (var key in new List<Guid>(type.costMod.passiveModifiers.Keys))
            type.costMod.passiveModifiers[key] = new ValueModifier(ValueModifier.ValueModifierType.MultiDiminishing, new BigDouble(.5));
        var second = Action(60d, 60d);
        PlotNodeSO.All[0].availableActions.Add(second.Action);
        PlotNodeSO.All[0].GetActionInstances().Add(second.Instance);

        var quote = Collect();

        Assert.True(quote.Complete, quote.Reason);
        Assert.False(quote.NextComplete);
        Assert.Equal(80d, Assert.Single(quote.Current.AsSpan().ToArray()).Amount.ToDouble());
        Assert.Empty(quote.Next.AsSpan().ToArray());
        Assert.Equal(AutoLevelsDecisionKind.Lower, Decision(quote, totalDrain: 100).Kind);
    }

    [Fact]
    public void ADistributorReachingPlotTimingCannotReuseTheCurrentTimingAsANextQuote()
    {
        var (plot, instance, type) = Seed();
        instance.GetAction()!.useParentSize = true;
        type.costMod.records.Add(plot.sizeMod);

        var quote = Collect();

        Assert.True(quote.Complete, quote.Reason);
        Assert.Equal(20d, quote.Current[0].Amount.ToDouble());
        Assert.False(quote.NextComplete);
        Assert.Contains("capturing an engaged Agromancy action's prospective drain program threw", quote.NextReason);
    }

    [Fact]
    public void UnsettledNativeCurrentDisablesOnlyForecastAndKeepsExactAttribution()
    {
        var (_, instance, _) = Seed();
        instance.resourceDrain.current.costs[0] = new ResourceTuple(
            instance.resourceDrain.current.costs[0].resource, new BigDouble(19));
        var quote = Collect();
        Assert.True(quote.Complete, quote.Reason);
        Assert.Equal(19d, quote.Current[0].Amount.ToDouble());
        Assert.False(quote.NextComplete);
        Assert.Contains("has not applied", quote.NextReason);
    }

    private static (PlotNodeSO Plot, PlotNodeActionInstance Instance, HarvestActionTypeSO Type) Seed(bool stackingCost = false)
    {
        Player.Current.druidryOutputLevel.Value = 2;
        var type = new HarvestActionTypeSO();
        var currentFactor = stackingCost ? 4d : 2d;
        var action = Action(10d, 10d * currentFactor);
        action.Action.costMod = new ValueModifierRecord(new BigDouble(100)).WithMemo(new BigDouble(100d * currentFactor));
        var generated = Guid.NewGuid();
        action.Action.costMod.passiveModifiers[generated] = new ValueModifier(
            ValueModifier.ValueModifierType.MultiStacking, new BigDouble(currentFactor));
        var authored = new ValueModifier(stackingCost ? ValueModifier.ValueModifierType.MultiStacking :
            ValueModifier.ValueModifierType.MultiDiminishing, stackingCost ? new BigDouble(4) : BigDouble.One);
        type.costMod.passiveModifiers[authored.gc.guid] = authored;
        type.costMod.orderGuid[0] = generated;
        type.costMod.records.Add(action.Action.costMod);
        action.Action.actionTypes.Add(type);
        var weight = new ScalingWeightSO();
        var level = new ValueModifierList();
        level.modifiers.Add(new ValueModifier(ValueModifier.ValueModifierType.MultiDiminishing, BigDouble.One));
        weight.scalingEntries.Set((ScalingType)0, level);
        var power = new ValueModifierList();
        power.modifiers.Add(new ValueModifier(ValueModifier.ValueModifierType.MultiDiminishing, new BigDouble(.01)));
        weight.scalingEntries.Set((ScalingType)2, power);
        var block = new PersistentEffectBlock();
        block.effectMods.Add(new ScalingWeightEffectMod { scalingWeightRef = new ScalingWeightRef { scalingWeight = weight } });
        block.effectScripts.Add(new UpgradeableObject.UpgradeEffectModifier
        { upgradeableObject = type, propertyType = "Cost", modifier = authored });
        Player.Current.druidryOutputLevel.persistentEffectBlocks.Add(block);
        var plot = new PlotNodeSO
        {
            yieldMod = Percent(), specialMod = Percent(), actionSpeed = Percent(), actionCostMod = Percent(),
            sizeMod = Percent(), qualityMod = Percent(),
        };
        plot.availableActions.Add(action.Action);
        plot.GetActionInstances().Add(action.Instance);
        PlotNodeSO.All.Add(plot);
        return (plot, action.Instance, type);
    }

    private static (PlotNodeActionSO Action, PlotNodeActionInstance Instance) Action(double authored, double actual)
    {
        var resource = new ResourceSO { uuid = Mana.ToString() };
        var action = new PlotNodeActionSO
        { power = Percent(), speed = Percent(), costMod = Percent(), growthSizeMod = Percent(), baseTime = 1d };
        action.actionDrain.costs.Add(new ResourceTuple(resource, new BigDouble(authored)));
        var instance = new PlotNodeActionInstance(action) { quantity = 1, Engaged = true };
        instance.resourceDrain.current.costs.Add(new ResourceTuple(resource, new BigDouble(actual)));
        return (action, instance);
    }

    private static ValueModifierRecord Percent() => new(new BigDouble(100));
    private static WorldLevelDomainQuote Collect(Func<string, Type?>? resolve = null)
    {
        var frame = new GameWorldCycleFrame();
        var report = new WorldPlotActionReader(typeof(PlotNodeSO), resolve ?? Resolve).Collect(new HashSet<Guid>(), frame);
        Assert.Equal(WorldCategoryOutcome.Collected, report.Outcome);
        Assert.Equal(0, report.Skipped);
        return frame.LevelEconomy.Build(PublicationTable<WorldResource>.Empty,
            new WorldSpellWorkbench(0, 0, false, druidryLevel: 2, maximumDruidryLevel: 5),
            PublicationTable<WorldSpellTypeResonance>.Empty).Druidry;
    }
    private static AutoLevelsDecision Decision(WorldLevelDomainQuote quote, double totalDrain)
    {
        var reading = WorldSamples.Resource(Mana, new BigDouble(100), new BigDouble(1000),
            quality: new BigDouble(100), drain: new BigDouble(totalDrain));
        var resource = new WorldResource(in reading, true, new BigDouble(900), .1, false,
            new BigDouble(100), new BigDouble(-10), new BigDouble(-10));
        var world = new GameWorldState
        {
            Resources = WorldTable.Create(new[] { resource }),
            Views = WorldTable.Create(new[] { new WorldView(KnownEntities.WorldAgromancyDruidryLvs.Uuid, false, false, true) }),
            SpellWorkbench = new WorldSpellWorkbench(0, 0, false, druidryLevel: 2, maximumDruidryLevel: 5),
            LevelEconomy = new WorldLevelEconomy(WorldLevelEconomyDefaults.Empty.Output, quote, WorldLevelEconomyDefaults.Empty.Alchemy),
        };
        return AutoLevelsPolicy.Evaluate(world, LevelDial.Druidry,
            new AutoLevelsConfiguration { Mode = AutoLevelsOperationMode.Active });
    }
    private static Type? Resolve(string name) => typeof(PlotNodeSO).Assembly.GetType(name, false);
}
