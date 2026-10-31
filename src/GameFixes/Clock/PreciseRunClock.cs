using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Counts the time since the last world reset in the clock variable's own precision.
/// <c>PersistentResetManager.ManagerUpdate</c> adds each tick as
/// <c>resetTimePassed.SetValue(resetTimePassed.AsFloat() + deltaTime)</c>, so the sum is rounded to
/// single precision every tick: from about 2.3 hours the clock drifts by whole percents, and at
/// 2^19 seconds (about 145.6 hours) a tick is below half a step and the clock stops.
/// </summary>
/// <remarks>
/// This is a class C fix: it changes outcomes, toward the clock the game means to keep. The variable
/// is a <c>DoubleVariable</c> saved as a <c>BigDouble</c>, so the precise sum is what the game saves
/// and loads. Off, the patched call adds in single precision exactly as the game does.
/// </remarks>
internal sealed class PreciseRunClock : GameFix
{
    internal const string Target = "PersistentResetManager:ManagerUpdate";

    private static readonly FieldInfo? Clock = typeof(PersistentResetManager).GetField("resetTimePassed", BindingFlags.Instance | BindingFlags.Public);

    internal static readonly MethodInfo AdvanceMethod =
        typeof(PreciseRunClock).GetMethod(nameof(Advance), BindingFlags.Static | BindingFlags.NonPublic)!;

    internal static bool IsActive;

    private static readonly AutomationFeatureSetting Definition = AutomationFeatureSetting.Define(
        "PreciseRunClock", false, "Precise run clock",
        "The game counts the time since your last world reset in single precision: about 2 hours into a run it starts to drift, and at about 145 hours it stops. This counts it precisely, and that changes outcomes: timed challenges in the 2–18 hour range get about 2% stricter, and the clock keeps counting past 145 hours, so milestone times and lifetime rates follow the run's real time. Takes effect immediately.",
        40,
        static config => config.GameFixes.PreciseRunClock,
        static (config, value) => config with { GameFixes = config.GameFixes with { PreciseRunClock = value } });

    internal override AutomationFeatureSetting Setting => Definition;

    internal override IReadOnlyList<string> Install(Harmony harmony)
    {
        var failures = new List<string>();
        if (Clock is null || Clock.FieldType != typeof(DoubleVariable))
            failures.Add(Target + " does not keep its run clock as audited.");
        else
            NativePatch.TryPatch(failures, harmony, AccessTools.Method(Target, new[] { typeof(float) }), Target,
                transpiler: new HarmonyMethod(typeof(PreciseRunClock), nameof(Transpile)));
        return failures;
    }

    internal override void Apply(bool active) => IsActive = active;

    /// <summary>
    /// The audited body advances the clock in one statement:
    /// <c>resetTimePassed.SetValue((BigDouble)(resetTimePassed.AsFloat() + deltaTime))</c>. Its
    /// <c>AsFloat</c>, add and conversion are replaced by one call to <see cref="Advance"/>; any other
    /// shape is left unpatched.
    /// </summary>
    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var rewritten = new List<CodeInstruction>(instructions);
        var read = -1;
        for (var index = 0; index < rewritten.Count; index++)
        {
            if (!IsVariableCall(rewritten[index], OpCodes.Callvirt, "AsFloat")) continue;
            if (read >= 0) throw Refused("reads a variable in single precision more than once");
            read = index;
        }
        if (read < 0) throw Refused("does not read its run clock in single precision");
        var statement = read - 4;
        if (statement < 0 || statement + ClockStatement.Length > rewritten.Count)
            throw Refused("does not advance its run clock in the audited statement");
        for (var offset = 0; offset < ClockStatement.Length; offset++)
            if (!ClockStatement[offset](rewritten[statement + offset]))
                throw Refused("does not advance its run clock in the audited statement");
        rewritten.RemoveRange(read, 4);
        rewritten.InsertRange(read, new[]
        {
            new CodeInstruction(OpCodes.Ldarg_1),
            new CodeInstruction(OpCodes.Call, AdvanceMethod),
        });
        return rewritten;
    }

    /// <summary>The clock's next value: the game's single-precision sum, or while the fix is on the precise one.</summary>
    internal static BigDouble Advance(DoubleVariable clock, float deltaTime)
    {
        return IsActive ? clock.AsBigDouble() + deltaTime : clock.AsFloat() + deltaTime;
    }

    private static readonly Func<CodeInstruction, bool>[] ClockStatement =
    {
        instruction => instruction.opcode == OpCodes.Ldarg_0,
        IsClock,
        instruction => instruction.opcode == OpCodes.Ldarg_0,
        IsClock,
        instruction => IsVariableCall(instruction, OpCodes.Callvirt, "AsFloat"),
        instruction => instruction.opcode == OpCodes.Ldarg_1,
        instruction => instruction.opcode == OpCodes.Add,
        instruction => instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method &&
            method.DeclaringType == typeof(BigDouble) && method.Name == "op_Implicit" &&
            method.GetParameters()[0].ParameterType == typeof(float),
        instruction => IsVariableCall(instruction, OpCodes.Callvirt, "SetValue", typeof(BigDouble)),
    };

    private static bool IsClock(CodeInstruction instruction) =>
        instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field && Clock is not null &&
        field.DeclaringType == Clock.DeclaringType && field.Name == Clock.Name;

    private static bool IsVariableCall(CodeInstruction instruction, OpCode opcode, string name, params Type[] parameters)
    {
        if (instruction.opcode != opcode || instruction.operand is not MethodInfo method ||
            method.DeclaringType != typeof(NumberVariable) || method.Name != name)
            return false;
        var declared = method.GetParameters();
        if (declared.Length != parameters.Length) return false;
        for (var index = 0; index < parameters.Length; index++)
            if (declared[index].ParameterType != parameters[index]) return false;
        return true;
    }

    private static InvalidOperationException Refused(string shape) => new(Target + " " + shape + ".");
}
