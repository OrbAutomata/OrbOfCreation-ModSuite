using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Services.GameFixes;

[Collection(GameFixesCollection.Name)]
public sealed class PreciseRunClockTests : IDisposable
{
    private const float Tick = 0.02f;

    private static readonly FieldInfo Clock = typeof(PersistentResetManager).GetField(nameof(PersistentResetManager.resetTimePassed))!;

    private readonly PreciseRunClock _fix = new();

    public PreciseRunClockTests() => _fix.Apply(true);

    public void Dispose() => _fix.Apply(false);

    [Fact]
    public void AfterTwoHoursThePatchedClockCountsEveryTickWhereTheGamesFallsTwoPercentBehind()
    {
        var update = ManagerUpdate(PreciseRunClock.Transpile);
        var precise = Run(update, 10000, 1000);
        _fix.Apply(false);
        var patchedOff = Run(update, 10000, 1000);
        var game = Run(ManagerUpdate(instructions => instructions), 10000, 1000);

        Assert.Equal(10020, precise.ToDouble(), 6);
        Assert.Equal(10019.53125, game.ToDouble());
        Assert.Equal(SinglePrecisionClock(10000, 1000), game.ToDouble());
        AssertSame(game, patchedOff);
    }

    [Fact]
    public void PastOneHundredFortyFiveHoursThePatchedClockKeepsCountingWhereTheGamesStops()
    {
        var update = ManagerUpdate(PreciseRunClock.Transpile);
        var precise = Run(update, 524288, 500);

        _fix.Apply(false);
        var patchedOff = Run(update, 524288, 500);
        var game = Run(ManagerUpdate(instructions => instructions), 524288, 500);

        Assert.Equal(524298, precise.ToDouble(), 6);
        Assert.Equal(524288, game.ToDouble());
        AssertSame(game, patchedOff);
    }

    /// <summary>
    /// The fix acts on every tick, so it keeps no count: the Game Fixes status stays as it is while
    /// the clock runs instead of being republished at every activity poll.
    /// </summary>
    [Fact]
    public void TheClockRunningLeavesTheFixCountAtZero()
    {
        Run(ManagerUpdate(PreciseRunClock.Transpile), 10000, 1000);

        Assert.Equal(0, _fix.Fired);
    }

    [Fact]
    public void AClockOfAnotherShapeIsRefused()
    {
        var shapes = new Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>>[]
        {
            instructions => instructions.Where(instruction => !IsCall(instruction, "AsFloat")),
            instructions => instructions.Concat(instructions.Where(instruction => IsCall(instruction, "AsFloat"))),
            instructions => instructions.Select(instruction => instruction.opcode == OpCodes.Add ? new CodeInstruction(OpCodes.Sub) : instruction),
            instructions => instructions.Select(instruction => instruction.opcode == OpCodes.Ldarg_1 ? new CodeInstruction(OpCodes.Ldc_R4, 1f) : instruction),
            instructions => instructions.Select(instruction => IsCall(instruction, "SetValue") ? new CodeInstruction(OpCodes.Pop) : instruction),
            instructions => instructions.Select(instruction => instruction.operand is FieldInfo
                ? new CodeInstruction(OpCodes.Ldfld, typeof(PersistentResetManager).GetField(nameof(PersistentResetManager.challengeRerollsLeft)))
                : instruction),
        };

        foreach (var shape in shapes)
        {
            var refusal = Assert.Throws<InvalidOperationException>(
                () => ManagerUpdate(instructions => PreciseRunClock.Transpile(shape(instructions))));
            Assert.StartsWith(PreciseRunClock.Target, refusal.Message);
        }
    }

    private static BigDouble Run(Action<PersistentResetManager, float> update, double start, int ticks)
    {
        var manager = new PersistentResetManager();
        manager.resetTimePassed.SetValue(start);
        for (var tick = 0; tick < ticks; tick++) update(manager, Tick);
        return manager.resetTimePassed.AsBigDouble();
    }

    private static double SinglePrecisionClock(float start, int ticks)
    {
        for (var tick = 0; tick < ticks; tick++) start += Tick;
        return start;
    }

    private static void AssertSame(BigDouble expected, BigDouble actual)
    {
        Assert.Equal(expected.Mantissa, actual.Mantissa);
        Assert.Equal(expected.Exponent, actual.Exponent);
    }

    private static bool IsCall(CodeInstruction instruction, string name) =>
        instruction.operand is MethodInfo method && method.Name == name;

    /// <summary>
    /// The game's <c>ManagerUpdate</c> clock statement, instruction for instruction as the audited
    /// build ships it, run through a transpiler and compiled against the stub manager. The persistence
    /// refresh after it is left out: it reads nothing the statement writes.
    /// </summary>
    private static Action<PersistentResetManager, float> ManagerUpdate(
        Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>> transpile)
    {
        var owner = typeof(PersistentResetManager);
        var method = new DynamicMethod("ManagerUpdate", typeof(void), new[] { owner, typeof(float) }, owner, true);
        var il = method.GetILGenerator();
        var instructions = new List<CodeInstruction>
        {
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, Clock),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, Clock),
            new(OpCodes.Callvirt, typeof(NumberVariable).GetMethod(nameof(NumberVariable.AsFloat))),
            new(OpCodes.Ldarg_1),
            new(OpCodes.Add),
            new(OpCodes.Call, typeof(BigDouble).GetMethod("op_Implicit", new[] { typeof(float) })),
            new(OpCodes.Callvirt, typeof(NumberVariable).GetMethod(nameof(NumberVariable.SetValue), new[] { typeof(BigDouble) })),
            new(OpCodes.Ret),
        };

        foreach (var instruction in transpile(instructions))
        {
            switch (instruction.operand)
            {
                case null: il.Emit(instruction.opcode); break;
                case FieldInfo field: il.Emit(instruction.opcode, field); break;
                case MethodInfo called: il.Emit(instruction.opcode, called); break;
                default: throw new InvalidOperationException("Unexpected operand " + instruction.operand);
            }
        }
        return (Action<PersistentResetManager, float>)method.CreateDelegate(typeof(Action<PersistentResetManager, float>));
    }
}
