using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Engagement;
using HarmonyLib;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Services.GameFixes;

[Collection(GameFixesCollection.Name)]
public sealed class AutoDevelopmentCostOnceTests : IDisposable
{
    private const BindingFlags InstanceField = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private const int ClosedFormReturn = 36;

    private readonly AutoDevelopmentCostOnce _fix = new();

    public AutoDevelopmentCostOnceTests() => _fix.Apply(true);

    public void Dispose() => _fix.Apply(false);

    [Theory]
    [InlineData(1, 1, 3, 0, 3, 0)]
    [InlineData(2, 0, 3, 0, 3, 0)]
    [InlineData(1.003, 2, 1, -1, 1, -1)]
    [InlineData(1, 6, 7, -1, 7, -1)]
    [InlineData(1, 1, 4, 0, 3, 0)]
    [InlineData(7.77, 0, 5, -1, 1.3, 0)]
    [InlineData(1.234, 400, 1.1, 396, 1.1, 396)]
    [InlineData(5, 20, 1, 0, 3.3, 15)]
    public void ThePatchedLoopCompletesExactlyAsTheGameLoopWithOneCalculation(
        double durationMantissa, long durationExponent, double maxMantissa, long maxExponent,
        double buildMantissa, long buildExponent)
    {
        var duration = new BigDouble(durationMantissa, durationExponent);
        var maxDuration = new BigDouble(maxMantissa, maxExponent);
        var buildTime = new BigDouble(buildMantissa, buildExponent);
        var before = _fix.Fired;
        var vanilla = Run(CompletionLoop(instructions => instructions), duration, maxDuration, buildTime);
        var patched = Run(CompletionLoop(AutoDevelopmentCostOnce.Transpile), duration, maxDuration, buildTime);

        AssertSame(vanilla.Completions, patched.Completions);
        AssertSame(vanilla.Duration, patched.Duration);
        AssertSame(vanilla.MaxDuration, patched.MaxDuration);
        Assert.Equal(Math.Min(vanilla.Calculations, 1), patched.Calculations);
        Assert.Equal(before + vanilla.Calculations - patched.Calculations, _fix.Fired);
    }

    [Fact]
    public void TheCapStillEndsTheLoopAtTenThousandCompletions()
    {
        var patched = Run(CompletionLoop(AutoDevelopmentCostOnce.Transpile), (BigDouble)1e9, (BigDouble)1, (BigDouble)1);

        AssertSame(AutoDevelopmentCostOnce.CompletionCap, patched.Completions);
        AssertSame((BigDouble)1e9 - AutoDevelopmentCostOnce.CompletionCap, patched.Duration);
    }

    [Fact]
    public void SwitchedOffThePatchedLoopCalculatesOnEveryCompletion()
    {
        _fix.Apply(false);
        var before = _fix.Fired;

        var patched = Run(CompletionLoop(AutoDevelopmentCostOnce.Transpile), (BigDouble)10, (BigDouble)3, (BigDouble)3);

        Assert.Equal(3, patched.Calculations);
        Assert.Equal(before, _fix.Fired);
    }

    [Fact]
    public void ALoopOfAnotherShapeIsRefused()
    {
        var shapes = new Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>>[]
        {
            instructions => instructions.Where(instruction => instruction.opcode != OpCodes.Callvirt),
            instructions => Changed(instructions, instruction => instruction.operand is AutoDevelopmentCostOnce.CompletionCap,
                instruction => instruction.operand = 9999),
            instructions => Changed(instructions, instruction => instruction.operand is MethodInfo { Name: "op_Subtraction" },
                instruction => instruction.operand = typeof(BigDouble).GetMethod("op_Addition")),
            instructions => Changed(instructions, instruction => instruction.opcode == OpCodes.Brtrue_S,
                instruction => instruction.opcode = OpCodes.Brfalse),
            instructions => instructions.Select((instruction, index) => index == ClosedFormReturn ? new CodeInstruction(OpCodes.Nop) : instruction),
            instructions => Changed(instructions, instruction => instruction.opcode == OpCodes.Stloc_0,
                instruction => instruction.opcode = OpCodes.Pop),
            instructions => instructions.Concat(instructions.Where(instruction => instruction.opcode == OpCodes.Callvirt)),
        };

        foreach (var shape in shapes)
        {
            var refusal = Assert.Throws<InvalidOperationException>(
                () => CompletionLoop(instructions => AutoDevelopmentCostOnce.Transpile(shape(instructions), null!)));
            Assert.StartsWith(AutoDevelopmentCostOnce.Target, refusal.Message);
        }
    }

    private static (BigDouble Completions, BigDouble Duration, BigDouble MaxDuration, int Calculations) Run(
        Func<EngagedEffectInstance, BigDouble> loop, BigDouble duration, BigDouble maxDuration, BigDouble buildTime)
    {
        var calculations = 0;
        var instance = new EngagedEffectInstance(duration, maxDuration,
            new ObservingCache.CalculatedValue<BigDouble>(() =>
            {
                calculations++;
                return buildTime;
            }));
        calculations = 0;
        var completions = loop(instance);
        return (completions, instance.GetCurrentDuration(), instance.GetMaxDuration(), calculations);
    }

    private static void AssertSame(BigDouble expected, BigDouble actual)
    {
        Assert.Equal(expected.Mantissa, actual.Mantissa);
        Assert.Equal(expected.Exponent, actual.Exponent);
    }

    private static Func<EngagedEffectInstance, BigDouble> CompletionLoop(
        Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>> transpile) =>
        CompletionLoop((instructions, _) => transpile(instructions));

    /// <summary>
    /// The game's <c>GetAndApplyCreationTimes</c>, instruction for instruction as the audited build
    /// ships it, run through a transpiler and compiled against the stub instance.
    /// </summary>
    private static Func<EngagedEffectInstance, BigDouble> CompletionLoop(
        Func<IEnumerable<CodeInstruction>, ILGenerator, IEnumerable<CodeInstruction>> transpile)
    {
        var owner = typeof(EngagedEffectInstance);
        var method = new DynamicMethod("GetAndApplyCreationTimes", typeof(BigDouble), new[] { owner }, owner, true);
        var il = method.GetILGenerator();
        il.DeclareLocal(typeof(BigDouble));
        var notOnce = il.DefineLabel();
        var body = il.DefineLabel();
        var compare = il.DefineLabel();
        var done = il.DefineLabel();
        var isCompleteOnce = owner.GetField("isCompleteOnce", InstanceField)!;
        var duration = owner.GetField("duration", InstanceField)!;
        var maxDuration = owner.GetField("maxDuration", InstanceField)!;
        var getMaxDuration = owner.GetField("getMaxDuration", InstanceField)!;
        var calculate = typeof(ObservingCache.CalculatedValue<BigDouble>).GetMethod("Calculate")!;
        var fromInt = typeof(BigDouble).GetMethod("op_Implicit", new[] { typeof(int) })!;
        MethodInfo Big(string name) => typeof(BigDouble).GetMethods().Single(candidate =>
            candidate.Name == name && candidate.GetParameters().Length > 0 && candidate.GetParameters().All(parameter => parameter.ParameterType == typeof(BigDouble)));
        var marks = new Dictionary<CodeInstruction, Label>();
        CodeInstruction Marked(CodeInstruction instruction, Label label)
        {
            marks.Add(instruction, label);
            return instruction;
        }

        var instructions = new List<CodeInstruction>
        {
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, isCompleteOnce),
            new(OpCodes.Brfalse_S, notOnce),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, duration),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, maxDuration),
            new(OpCodes.Call, Big("op_Subtraction")),
            new(OpCodes.Stfld, duration),
            new(OpCodes.Ldc_I4_1),
            new(OpCodes.Call, fromInt),
            new(OpCodes.Ret),
            Marked(new(OpCodes.Ldc_I4_0), notOnce),
            new(OpCodes.Call, fromInt),
            new(OpCodes.Stloc_0),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, getMaxDuration),
            new(OpCodes.Brtrue_S, compare),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, duration),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, maxDuration),
            new(OpCodes.Call, Big("op_Division")),
            new(OpCodes.Call, Big("Floor")),
            new(OpCodes.Stloc_0),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, duration),
            new(OpCodes.Ldloc_0),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, maxDuration),
            new(OpCodes.Call, Big("op_Multiply")),
            new(OpCodes.Call, Big("op_Subtraction")),
            new(OpCodes.Stfld, duration),
            new(OpCodes.Ldloc_0),
            new(OpCodes.Ret),
            Marked(new(OpCodes.Ldloc_0), body),
            new(OpCodes.Call, Big("op_Increment")),
            new(OpCodes.Stloc_0),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, duration),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, maxDuration),
            new(OpCodes.Call, Big("op_Subtraction")),
            new(OpCodes.Stfld, duration),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, getMaxDuration),
            new(OpCodes.Callvirt, calculate),
            new(OpCodes.Stfld, maxDuration),
            Marked(new(OpCodes.Ldarg_0), compare),
            new(OpCodes.Ldfld, duration),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, maxDuration),
            new(OpCodes.Call, Big("op_GreaterThanOrEqual")),
            new(OpCodes.Brfalse_S, done),
            new(OpCodes.Ldloc_0),
            new(OpCodes.Ldc_I4, 10000),
            new(OpCodes.Call, fromInt),
            new(OpCodes.Call, Big("op_LessThan")),
            new(OpCodes.Brtrue_S, body),
            Marked(new(OpCodes.Ldloc_0), done),
            new(OpCodes.Ret),
        };

        foreach (var instruction in transpile(instructions, il))
        {
            if (marks.TryGetValue(instruction, out var label)) il.MarkLabel(label);
            switch (instruction.operand)
            {
                case null: il.Emit(instruction.opcode); break;
                case Label target: il.Emit(instruction.opcode, target); break;
                case int value: il.Emit(instruction.opcode, value); break;
                case FieldInfo field: il.Emit(instruction.opcode, field); break;
                case MethodInfo called: il.Emit(instruction.opcode, called); break;
                case LocalBuilder local: il.Emit(instruction.opcode, local); break;
                default: throw new InvalidOperationException("Unexpected operand " + instruction.operand);
            }
        }
        return (Func<EngagedEffectInstance, BigDouble>)method.CreateDelegate(typeof(Func<EngagedEffectInstance, BigDouble>));
    }

    private static IEnumerable<CodeInstruction> Changed(IEnumerable<CodeInstruction> instructions,
        Func<CodeInstruction, bool> match, Action<CodeInstruction> change)
    {
        foreach (var instruction in instructions)
        {
            if (match(instruction)) change(instruction);
            yield return instruction;
        }
    }
}
