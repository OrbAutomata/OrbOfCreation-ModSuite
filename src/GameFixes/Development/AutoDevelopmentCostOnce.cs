using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Engagement;
using HarmonyLib;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Works out an auto-developed attribute's cost once per tick instead of once per completion.
/// <c>EngagedEffectInstance.GetAndApplyCreationTimes</c> counts the developments a tick completes by
/// subtracting the build time from the elapsed duration, up to 10,000 times, and recalculates the
/// build time after every subtraction. For auto-development that is <c>StructureSO.GetNextCost</c>
/// each time, although the quantity it depends on only changes after the loop.
/// </summary>
/// <remarks>
/// The loop and its cap stay as the game wrote them. The first recalculation still runs; every later
/// one is handed the value the first produced, so the same subtractions run on the same numbers and
/// the completions and the leftover duration are bit-identical. The proof is in the game's IL and is
/// pinned by <c>GameFixesAutoDevelopmentContractTests</c>: the loop body writes only its counter, the
/// duration and the build time, and neither build time the game ever assigns reads those or anything
/// its own calculation writes. The game's closed-form branch for a missing build time is dead and is
/// not used: one division and floor can differ from repeated subtraction by one completion.
/// </remarks>
internal sealed class AutoDevelopmentCostOnce : GameFix
{
    internal const string Target = "Engagement.EngagedEffectInstance:GetAndApplyCreationTimes";
    internal const int CompletionCap = 10000;

    private const BindingFlags InstanceField = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo? Duration = typeof(EngagedEffectInstance).GetField("duration", InstanceField);
    private static readonly FieldInfo? MaxDuration = typeof(EngagedEffectInstance).GetField("maxDuration", InstanceField);
    private static readonly FieldInfo? GetMaxDuration = typeof(EngagedEffectInstance).GetField("getMaxDuration", InstanceField);

    internal static readonly MethodInfo CalculateOnceMethod =
        typeof(AutoDevelopmentCostOnce).GetMethod(nameof(CalculateOnce), BindingFlags.Static | BindingFlags.NonPublic)!;

    internal static bool IsActive;
    private static long s_skipped;

    private static readonly AutomationFeatureSetting Definition = AutomationFeatureSetting.Define(
        "AutoDevelopmentCostOnce", false, "Lighter auto-development",
        "At high speed the game works out an auto-developed attribute's cost again for every development it completes in a tick, up to 10,000 times. This works it out once per tick: the same results with less work at high speed. Takes effect immediately.",
        30,
        static config => config.GameFixes.AutoDevelopmentCostOnce,
        static (config, value) => config with { GameFixes = config.GameFixes with { AutoDevelopmentCostOnce = value } });

    internal override AutomationFeatureSetting Setting => Definition;

    internal override long Fired => s_skipped;

    internal override string DescribeFired(long fired) =>
        fired + (fired == 1 ? " repeated cost calculation" : " repeated cost calculations") + " skipped.";

    internal override IReadOnlyList<string> Install(Harmony harmony)
    {
        var failures = new List<string>();
        if (Duration is null || MaxDuration is null || GetMaxDuration is null)
            failures.Add(Target + " does not keep its duration, build time and build-time calculation as audited.");
        else
            NativePatch.TryPatch(failures, harmony, AccessTools.Method(Target, Type.EmptyTypes), Target,
                transpiler: new HarmonyMethod(typeof(AutoDevelopmentCostOnce), nameof(Transpile)));
        return failures;
    }

    internal override void Apply(bool active) => IsActive = active;

    /// <summary>
    /// The audited completion loop follows the closed form's <c>ret</c>, so it is entered only at its
    /// compare: <c>++completions; duration -= maxDuration; maxDuration = getMaxDuration.Calculate();</c>
    /// then <c>while (duration &gt;= maxDuration &amp;&amp; completions &lt; 10000)</c>. The one
    /// <c>Calculate</c> is routed through <see cref="CalculateOnce"/> with a fresh flag local; any
    /// other shape is left unpatched. The branch targets are pinned by the installed-game contracts.
    /// </summary>
    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var rewritten = new List<CodeInstruction>(instructions);
        var calculate = -1;
        for (var index = 0; index < rewritten.Count; index++)
        {
            if (!IsCalculate(rewritten[index])) continue;
            if (calculate >= 0) throw Refused("recalculates its build time more than once");
            calculate = index;
        }
        if (calculate < 0) throw Refused("does not recalculate its build time");
        var body = calculate - 13;
        var head = calculate + 2;
        if (body < 0 || head + LoopCondition.Length > rewritten.Count ||
            !Matches(rewritten, body, LoopBody) || !Matches(rewritten, head, LoopCondition))
            throw Refused("does not complete in the audited subtraction loop");
        if (rewritten[body - 1].opcode != OpCodes.Ret)
            throw Refused("can fall into its completion loop");
        var calculated = generator.DeclareLocal(typeof(bool));
        rewritten[calculate].opcode = OpCodes.Call;
        rewritten[calculate].operand = CalculateOnceMethod;
        rewritten.InsertRange(calculate, new[]
        {
            new CodeInstruction(OpCodes.Ldloca, calculated),
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldfld, MaxDuration),
        });
        return rewritten;
    }

    /// <summary>
    /// The loop's build-time recalculation. The first call in a run of the loop calculates; while the
    /// fix is on, every later one returns the build time that call stored, which is the value it
    /// would calculate again.
    /// </summary>
    internal static BigDouble CalculateOnce(ObservingCache.CalculatedValue<BigDouble> buildTime, ref bool calculated,
        BigDouble current)
    {
        if (calculated && IsActive)
        {
            s_skipped++;
            return current;
        }
        calculated = true;
        return buildTime.Calculate();
    }

    private static readonly Func<CodeInstruction, bool>[] LoopBody =
    {
        instruction => instruction.opcode == OpCodes.Ldloc_0,
        instruction => IsBigDouble(instruction, "op_Increment"),
        instruction => instruction.opcode == OpCodes.Stloc_0,
        instruction => instruction.opcode == OpCodes.Ldarg_0,
        instruction => instruction.opcode == OpCodes.Ldarg_0,
        instruction => IsField(instruction, OpCodes.Ldfld, Duration),
        instruction => instruction.opcode == OpCodes.Ldarg_0,
        instruction => IsField(instruction, OpCodes.Ldfld, MaxDuration),
        instruction => IsBigDouble(instruction, "op_Subtraction"),
        instruction => IsField(instruction, OpCodes.Stfld, Duration),
        instruction => instruction.opcode == OpCodes.Ldarg_0,
        instruction => instruction.opcode == OpCodes.Ldarg_0,
        instruction => IsField(instruction, OpCodes.Ldfld, GetMaxDuration),
        IsCalculate,
        instruction => IsField(instruction, OpCodes.Stfld, MaxDuration),
    };

    private static readonly Func<CodeInstruction, bool>[] LoopCondition =
    {
        instruction => instruction.opcode == OpCodes.Ldarg_0,
        instruction => IsField(instruction, OpCodes.Ldfld, Duration),
        instruction => instruction.opcode == OpCodes.Ldarg_0,
        instruction => IsField(instruction, OpCodes.Ldfld, MaxDuration),
        instruction => IsBigDouble(instruction, "op_GreaterThanOrEqual"),
        instruction => IsBranch(instruction, OpCodes.Brfalse, OpCodes.Brfalse_S),
        instruction => instruction.opcode == OpCodes.Ldloc_0,
        instruction => instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is CompletionCap,
        instruction => IsBigDouble(instruction, "op_Implicit") &&
            ((MethodInfo)instruction.operand!).GetParameters()[0].ParameterType == typeof(int),
        instruction => IsBigDouble(instruction, "op_LessThan"),
        instruction => IsBranch(instruction, OpCodes.Brtrue, OpCodes.Brtrue_S),
    };

    private static bool Matches(List<CodeInstruction> instructions, int start, Func<CodeInstruction, bool>[] shape)
    {
        for (var offset = 0; offset < shape.Length; offset++)
            if (!shape[offset](instructions[start + offset])) return false;
        return true;
    }

    private static bool IsCalculate(CodeInstruction instruction) =>
        instruction.opcode == OpCodes.Callvirt && instruction.operand is MethodInfo method &&
        method.DeclaringType == typeof(ObservingCache.CalculatedValue<BigDouble>) && method.Name == "Calculate" &&
        method.GetParameters().Length == 0;

    private static bool IsBigDouble(CodeInstruction instruction, string name) =>
        instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method &&
        method.DeclaringType == typeof(BigDouble) && method.Name == name;

    private static bool IsField(CodeInstruction instruction, OpCode opcode, FieldInfo? field) =>
        instruction.opcode == opcode && instruction.operand is FieldInfo operand && field is not null &&
        operand.DeclaringType == field.DeclaringType && operand.Name == field.Name;

    private static bool IsBranch(CodeInstruction instruction, OpCode longForm, OpCode shortForm) =>
        instruction.opcode == longForm || instruction.opcode == shortForm;

    private static InvalidOperationException Refused(string shape) => new(Target + " " + shape + ".");
}
