using System;
using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

/// <summary>
/// <c>AutoDevelopmentCostOnce</c> hands the completion loop's first build-time calculation to the
/// rest of the loop. These tests pin the parity proof in the game's IL: the loop's shape, every build
/// time the game can store where the loop calculates it, and that neither reads anything the loop or
/// its own calculation changes.
/// </summary>
public sealed class GameFixesAutoDevelopmentContractTests
{
    private const string Instance = "Engagement.EngagedEffectInstance";
    private const string BuildTime = "ObservingCache+CalculatedValue`1<BigDouble>";

    private static readonly (int Offset, byte Opcode, string? Member)[] CompletionLoop =
    {
        (0x00, 0x06, null),
        (0x01, 0x28, "BigDouble.op_Increment"),
        (0x06, 0x0A, null),
        (0x07, 0x02, null),
        (0x08, 0x02, null),
        (0x09, 0x7B, Instance + ".duration"),
        (0x0E, 0x02, null),
        (0x0F, 0x7B, Instance + ".maxDuration"),
        (0x14, 0x28, "BigDouble.op_Subtraction"),
        (0x19, 0x7D, Instance + ".duration"),
        (0x1E, 0x02, null),
        (0x1F, 0x02, null),
        (0x20, 0x7B, Instance + ".getMaxDuration"),
        (0x25, 0x6F, BuildTime + ".Calculate"),
        (0x2A, 0x7D, Instance + ".maxDuration"),
        (0x2F, 0x02, null),
        (0x30, 0x7B, Instance + ".duration"),
        (0x35, 0x02, null),
        (0x36, 0x7B, Instance + ".maxDuration"),
        (0x3B, 0x28, "BigDouble.op_GreaterThanOrEqual"),
        (0x40, 0x2C, null),
        (0x42, 0x06, null),
        (0x43, 0x20, null),
        (0x48, 0x28, "BigDouble.op_Implicit"),
        (0x4D, 0x28, "BigDouble.op_LessThan"),
        (0x52, 0x2D, null),
    };

    [GameAssemblyFact]
    public void TheCompletionLoopOnlyCountsSubtractsAndRecalculatesItsBuildTime()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        MethodBodyReferences.AssertMethod(assembly, Instance, "GetAndApplyCreationTimes", false, "BigDouble");
        var il = assembly.GetMethodBodyBytes(Instance, "GetAndApplyCreationTimes");
        var references = MethodBodyReferences.References(assembly, Instance, "GetAndApplyCreationTimes");
        var body = MethodBodyReferences.Offset(references, BuildTime, "Calculate") - 1 - 0x25;
        var tokens = references.GroupBy(reference => reference.DeclaringType + "." + reference.MemberName)
            .ToDictionary(member => member.Key, member => member.Select(reference => reference.Token).Distinct().Single());
        foreach (var (offset, opcode, member) in CompletionLoop)
        {
            Assert.Equal(opcode, il[body + offset]);
            if (member is not null) Assert.Equal(tokens[member], BitConverter.ToInt32(il, body + offset + 1));
        }
        Assert.Equal(10000, BitConverter.ToInt32(il, body + 0x44));
        Assert.Equal(body, body + 0x54 + (sbyte)il[body + 0x53]);

        var head = body + 0x2F;
        var entry = references.First(reference => reference.MemberName == "getMaxDuration").Offset - 1;
        Assert.Equal(0x7B, il[entry]);
        Assert.Equal(0x2D, il[entry + 5]);
        Assert.Equal(head, entry + 7 + (sbyte)il[entry + 6]);
    }

    [GameAssemblyFact]
    public void TheLoopsBuildTimeIsAConstantOrAutoDevelopmentsNextCost()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var graph = GameCallGraph.Build(assembly);

        Assert.Equal(
            new[] { "..ctor", ".SetMaxDuration", ".SetMaxDuration" },
            assembly.GetFieldUseSites(new[] { (Instance, "getMaxDuration") })
                .Where(site => site.Use == "store").Select(site => "." + site.MethodName).OrderBy(name => name, StringComparer.Ordinal));
        foreach (var assigns in new[] { (".ctor", Array.Empty<string>()), ("SetMaxDuration", new[] { "BigDouble" }) })
            Assert.Contains(MethodBodyReferences.References(assembly, Instance, assigns.Item1, assigns.Item2),
                reference => reference.DeclaringType == BuildTime && reference.MemberName == "Static");

        var constant = assembly.GetMethodToken(Instance, "SetMaxDuration", "BigDouble");
        var calculated = assembly.GetMethodToken(Instance, "SetMaxDuration", BuildTime);
        var function = assembly.GetMethodToken(Instance, "SetMaxDuration", "System.Func`1<BigDouble>");
        Assert.Equal(
            new[] { "AutoPurchaseInstance.Initialize", "CraftingInstance.Initiate", "CraftingInstance.SetQuantity", Instance + ".SetMaxDuration" },
            graph.CallSitesOf(Instance, "SetMaxDuration"));
        Assert.Equal(new[] { calculated }, SetMaxDurationTokens(assembly, "AutoPurchaseInstance", "Initialize"));
        Assert.Equal(new[] { constant }, SetMaxDurationTokens(assembly, "CraftingInstance", "Initiate"));
        Assert.Equal(new[] { constant }, SetMaxDurationTokens(assembly, "CraftingInstance", "SetQuantity"));
        Assert.Equal(new[] { calculated }, SetMaxDurationTokens(assembly, Instance, "SetMaxDuration", "System.Func`1<BigDouble>"));
        Assert.DoesNotContain(function, assembly.GetReferencedMethodTokens(new[] { function }));

        var initialize = assembly.GetMethodBodyBytes("AutoPurchaseInstance", "Initialize");
        var delegateMade = MethodBodyReferences.Offset(
            MethodBodyReferences.References(assembly, "AutoPurchaseInstance", "Initialize"), "System.Func`1<BigDouble>", ".ctor") - 7;
        Assert.Equal(new byte[] { 0xFE, 0x06 }, initialize.Skip(delegateMade).Take(2));
        Assert.Equal(assembly.GetMethodToken("AutoPurchaseInstance", "CalcTotalTime"), BitConverter.ToInt32(initialize, delegateMade + 2));

        var closure = "ObservingCache+CalculatedValue`1+<>c__DisplayClass15_0";
        Assert.Equal(new byte[] { 0x02, 0x7B }, assembly.GetMethodBodyBytes(closure, "<Static>b__0").Take(2));
        Assert.Equal(7, assembly.GetMethodBodyBytes(closure, "<Static>b__0").Length);
    }

    /// <summary>
    /// Auto-development's build time reads the structure's quantity, cost modifiers and resource
    /// rarities. Nothing it reaches can read the loop's own state, draw a random number, or change a
    /// value it reads; the only writes on the way are lazily refreshed caches that the first
    /// calculation settles.
    /// </summary>
    [GameAssemblyFact]
    public void AutoDevelopmentsBuildTimeChangesNothingItReads()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var graph = GameCallGraph.Build(assembly);

        var paths = graph.PathsTo(graph.MethodsBelow("AutoPurchaseInstance", new[] { "CalcTotalTime" }), ChangesTheBuildTime);

        Assert.True(paths.Count == 0, string.Join(Environment.NewLine, paths));
        Assert.Equal(90, graph.Reached);
        Assert.Contains(graph.PathsTo(graph.MethodsBelow("AutoPurchaseInstance", new[] { "OnComplete" }), ChangesTheBuildTime),
            path => path.EndsWith("-> StructureSO.IncrementQuantity", StringComparison.Ordinal));
    }

    private static bool ChangesTheBuildTime(string type, string name) => type switch
    {
        Instance or "ObservingCache" or "ObservingCache+CalculatedValue`1" => true,
        "UnityEngine.Random" or "System.Random" => true,
        "StructureSO" => name.StartsWith("Set", StringComparison.Ordinal) ||
            name.StartsWith("Increment", StringComparison.Ordinal) || name.StartsWith("Queue", StringComparison.Ordinal) ||
            name.StartsWith("Complete", StringComparison.Ordinal),
        "ModifierRecord" or "ValueModifierRecord" => name is "Add" or "Remove" or "Clear" or "SetBase" or "SetBaseCalc" or "Load",
        "NumberVariable" or "DoubleVariable" or "IntVariable" => name.StartsWith("Set", StringComparison.Ordinal),
        _ => false,
    };

    private static int[] SetMaxDurationTokens(GameAssemblyMetadata assembly, string type, string method, params string[] parameters) =>
        MethodBodyReferences.References(assembly, type, method, parameters)
            .Where(reference => reference.DeclaringType == Instance && reference.MemberName == "SetMaxDuration")
            .Select(reference => reference.Token)
            .ToArray();
}
