using System.Linq;
using Xunit;
using static OrbModding.GameContractTests.MethodBodyReferences;

namespace OrbModding.GameContractTests;

public sealed class DruidryLevelEconomyContractTests
{
    [GameAssemblyFact]
    public void ProspectivePlotDrainKeepsNativeAdjustmentClampAndPersistentScalingOrder()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var scaling = References(assembly, "PlotNodeActionInstance", "GetScalingInfo", "System.Int32");
        Assert.True(Offset(scaling, "PlotNodeActionInstance", "GetBaseScalingInfo") < Offset(scaling, "PlotNodeActionSO", "AdjustScalingInfo"));
        Assert.True(Offset(scaling, "PlotNodeActionSO", "AdjustScalingInfo") < Offset(scaling, "PlotNodeActionInstance", "GetMaxSpeedPercent"));
        Assert.True(Offset(scaling, "PlotNodeActionInstance", "GetMaxSpeedPercent") < Offset(scaling, "ScalingInfo", "Min"));

        var persistent = References(assembly, "PlotNodeActionInstance", "GetPersistentScalingInfo", "System.Int32");
        Assert.True(Offset(persistent, "PlotNodeActionInstance", "GetScalingInfo") < Offset(persistent, "ScalingInfo", "GetFullSpeedScaling"));
        var ratio = Assert.Single(persistent, reference => reference.MemberName == "SetRatio");
        Assert.True(Offset(persistent, "ScalingInfo", "GetFullSpeedScaling") < ratio.Offset);

        var drain = References(assembly, "PlotNodeActionInstance", "ReapplyDrain");
        var drainMod = Assert.Single(drain, reference => reference.MemberName == "GetDrainCostMod");
        var drainRatio = Assert.Single(drain, reference => reference.MemberName == "GetRatio");
        Assert.True(drainMod.Offset < Offset(drain, "BigDouble", "AsPercent"));
        Assert.True(Offset(drain, "BigDouble", "AsPercent") < drainRatio.Offset);
        Assert.True(drainRatio.Offset < Offset(drain, "ResourceDrain", "SetMultiplier"));
        Assert.True(Offset(drain, "ResourceDrain", "SetMultiplier") < Offset(drain, "ResourceDrain", "ApplyDrain"));
    }

    [GameAssemblyFact]
    public void TypeDistributorsReplaceGeneratedPassiveSnapshotsAfterFoldingSameOrderGroups()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var collect = References(assembly, "OrderedMultiplierRecord", "CollectOrderedMultipliers");
        Assert.True(Offset(collect, "ValueModifier", "CombineSameOrderLists") < Offset(collect, "ValueModifier", "AdjustWithRaw"));
        Assert.True(Offset(collect, "ValueModifier", "AdjustWithRaw") < Offset(collect, "BigDouble", "AsPercent"));
        Assert.True(Offset(collect, "BigDouble", "AsPercent") < Offset(collect, "ValueModifier", "StackingRaw"));
        Assert.True(Offset(collect, "ValueModifier", "StackingRaw") < Offset(collect, "ValueModifier", "SetOrder"));

        var apply = References(assembly, "OrderedMultiplierRecord", "ApplyModifiersTo");
        Assert.True(Offset(apply, "OrderedMultiplierRecord", "orderGuid") < Offset(apply, "ModifierRecord", "Remove"));
        Assert.True(Offset(apply, "ModifierRecord", "Remove") < Offset(apply, "ModifierRecord", "Add"));
    }

    [GameAssemblyFact]
    public void TransmutersUseSerializedEntryOrderRatherThanTheDerivedDictionary()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var adjust = References(assembly, "UpgradeableObject+ValueTransmuterMap", "Adjust");
        Assert.Single(adjust, reference => reference.MemberName == "GetEntries");
        var entries = References(assembly, "ScalingMap`2", "GetEntries");
        Assert.Single(entries, reference => reference.MemberName == "valueList");
        Assert.DoesNotContain(entries, reference => reference.MemberName == "values");
    }
}
