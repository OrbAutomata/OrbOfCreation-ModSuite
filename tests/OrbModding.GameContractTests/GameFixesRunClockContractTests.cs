using System;
using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

/// <summary>
/// <c>PreciseRunClock</c> replaces the single-precision read and add in the run clock's one
/// accumulation. These tests pin that statement, that the clock has no other writer but the reset,
/// that the precise read is the value the game narrows, that the game saves it as a
/// <c>BigDouble</c>, and that the timed-challenge check compares it at full precision.
/// </summary>
public sealed class GameFixesRunClockContractTests
{
    private const string Manager = "PersistentResetManager";

    private static readonly (int Offset, byte Opcode, string? Member)[] ManagerUpdate =
    {
        (0x00, 0x02, null),
        (0x01, 0x7B, Manager + ".resetTimePassed"),
        (0x06, 0x02, null),
        (0x07, 0x7B, Manager + ".resetTimePassed"),
        (0x0C, 0x6F, "NumberVariable.AsFloat"),
        (0x11, 0x03, null),
        (0x12, 0x58, null),
        (0x13, 0x28, "BigDouble.op_Implicit"),
        (0x18, 0x6F, "NumberVariable.SetValue"),
        (0x1D, 0x02, null),
        (0x1E, 0x7B, Manager + ".persistenceObserver"),
        (0x23, 0x6F, "ObservingCache.IsUpdated"),
        (0x28, 0x2C, null),
        (0x2A, 0x02, null),
        (0x2B, 0x28, Manager + ".SetupPersistentValues"),
        (0x30, 0x2A, null),
    };

    [GameAssemblyFact]
    public void TheRunClockAdvancesInOneSinglePrecisionStatement()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        MethodBodyReferences.AssertMethod(assembly, Manager, "ManagerUpdate", false, "System.Void", "System.Single");
        Assert.Equal("DoubleVariable", assembly.GetFieldType(Manager, "resetTimePassed"));
        var il = assembly.GetMethodBodyBytes(Manager, "ManagerUpdate");
        var tokens = MethodBodyReferences.References(assembly, Manager, "ManagerUpdate")
            .GroupBy(reference => reference.DeclaringType + "." + reference.MemberName)
            .ToDictionary(member => member.Key, member => member.Select(reference => reference.Token).Distinct().Single());

        Assert.Equal(0x31, il.Length);
        foreach (var (offset, opcode, member) in ManagerUpdate)
        {
            Assert.Equal(opcode, il[offset]);
            if (member is not null) Assert.Equal(tokens[member], BitConverter.ToInt32(il, offset + 1));
        }
        Assert.Equal(0x30, 0x2A + (sbyte)il[0x29]);
        Assert.Equal(assembly.GetMethodToken("NumberVariable", "AsFloat"), tokens["NumberVariable.AsFloat"]);
        Assert.Equal(assembly.GetMethodToken("NumberVariable", "SetValue", "BigDouble"), tokens["NumberVariable.SetValue"]);
    }

    [GameAssemblyFact]
    public void OnlyTheResetWritesTheClockAndPlayerOnlyHandsItOut()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(
            new[] { Manager + ".ManagerUpdate", Manager + ".ManagerUpdate", Manager + ".PersistentResetLogic", "Player.GetResetTimePassed" },
            assembly.GetFieldUseSites(new[] { (Manager, "resetTimePassed"), ("Player", "resetTimePassed") })
                .Select(site => site.MethodOwner + "." + site.MethodName)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    [GameAssemblyFact]
    public void TheClocksSinglePrecisionReadNarrowsTheValueTheGameSavesAsABigDouble()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var precise = assembly.GetMethodToken("ValueModifierRecord", "AsBigDouble");
        Assert.Contains(MethodBodyReferences.References(assembly, "NumberVariable", "AsFloat"),
            reference => reference.Token == precise);
        Assert.Contains(MethodBodyReferences.References(assembly, "NumberVariable", "AsFloat"),
            reference => reference.DeclaringType == "BigDouble" && reference.MemberName == "ToFloat");
        Assert.Contains(MethodBodyReferences.References(assembly, "DoubleVariable", "AsBigDouble"),
            reference => reference.Token == precise);
        Assert.Equal("BigDouble", assembly.GetFieldType("NumberVariable+NumberSaveData", "savedValue"));
    }

    [GameAssemblyFact]
    public void ATimedChallengeComparesTheClockAtFullPrecision()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var references = MethodBodyReferences.References(assembly, "ChallengeCondition", "HasFailedTime");
        Assert.Equal(
            new[] { "Player.GetResetTimePassed", "NumberVariable.op_Implicit", "ChallengeCondition.GetTimeLimit", "BigDouble.op_GreaterThan" },
            references.Select(reference => reference.DeclaringType + "." + reference.MemberName));
    }
}
