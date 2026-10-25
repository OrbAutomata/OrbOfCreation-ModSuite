using System;
using System.Reflection.Emit;
using Xunit;
using static OrbModding.GameContractTests.MethodBodyReferences;

namespace OrbModding.GameContractTests;

public sealed class LevelEconomyContractTests
{
    [GameAssemblyFact]
    public void SpellOutputQuotesUseTheNativeLevelOffsetAndRoundBeforeRequirementPenalty()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var level = References(assembly, "Spell", "GetLevel");
        Assert.True(Offset(level, "Spell", "GetOutputLevel") < Offset(level, "Spell", "GetBaseEffectLevel"));
        var bytes = assembly.GetMethodBodyBytes("Spell", "GetLevel");
        // Metadata references point at the four-byte operand, one byte after the call opcode.
        var afterBase = Offset(level, "Spell", "GetBaseEffectLevel") + sizeof(int);
        Assert.Equal((byte)OpCodes.Sub.Value, bytes[afterBase]);
        Assert.Equal((byte)OpCodes.Ldc_I4_1.Value, bytes[afterBase + 1]);
        Assert.True(Offset(level, "Spell", "GetBaseEffectLevel") < Offset(level, "System.Math", "Max"));

        var cost = References(assembly, "Spell", "ComputeCost");
        Assert.True(Offset(cost, "GlyphSO", "AugmentCost") < Offset(cost, "ResourceCostList", "SetToLevel"));
        Assert.True(Offset(cost, "ResourceCostList", "SetToLevel") < Offset(cost, "Spell", "GetCostMod"));
        Assert.True(Offset(cost, "Spell", "GetCostMod") < Offset(cost, "ResourceCostList", "RoundToTwoSigs"));
        Assert.True(Offset(cost, "ResourceCostList", "RoundToTwoSigs") < Offset(cost, "Spell", "HasMetUsageRequirements"));
        Assert.True(Offset(cost, "Spell", "HasMetUsageRequirements") < Offset(cost, "ResourceCostList", "Apply"));
    }

    [GameAssemblyFact]
    public void FreshAugmentedBaseCostDoesNotReachLiveDialOrGameplayMutation()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var graph = GameCallGraph.Build(assembly);
        var paths = graph.PathsTo(graph.MethodsBelow("GlyphSO", new[] { "AugmentCost" }),
            (owner, member) => owner switch
            {
                "IntVariable" => member is "SetValue" or "ApplyEffects",
                "ResourceSO" => member is "Spend" or "AddUsage" or "RemoveUsage" or "RegisterGain",
                "Spell" => member is "SetLevel" or "SetAugmentGlyphs" or "Cast" or "FireSpell",
                "EventLogManager" or "UIPopupText" => true,
                _ => false,
            });
        Assert.Empty(paths);
    }
}
