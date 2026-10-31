using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Bounds the power-build sound loop in <c>StructureSO.CompleteQueuedQuantity</c>. The game plays
/// one sound per power fire of the whole batch, each with its own delay and coroutine, so a large
/// Bulk Development starts hundreds in one frame. The merge would silence every one after the first,
/// so the loop plays only that first one. Only the loop bound changes; the batch, its fires and its
/// popup are untouched.
/// </summary>
internal static class PowerBuildSoundCap
{
    internal const string Target = "StructureSO:CompleteQueuedQuantity";
    internal const int Cap = 1;

    internal static readonly MethodInfo PowerBuildSfx =
        typeof(GlobalVariables).GetMethod(nameof(GlobalVariables.GetPowerBuildSfx), Type.EmptyTypes)!;

    internal static readonly MethodInfo BoundMethod =
        typeof(PowerBuildSoundCap).GetMethod(nameof(Bound), BindingFlags.Static | BindingFlags.NonPublic)!;

    internal static void Install(ICollection<string> failures, Harmony harmony) =>
        NativePatch.TryPatch(failures, harmony, AccessTools.Method(Target, Type.EmptyTypes), Target,
            transpiler: new HarmonyMethod(typeof(PowerBuildSoundCap), nameof(Transpile)));

    /// <summary>
    /// The audited loop is entered by a branch straight to its compare, starts at its one
    /// <c>GetPowerBuildSfx</c> call, runs without another branch, and closes with
    /// <c>ldloc.1; blt</c>, local 1 holding the batch's power fires. The bound is routed through
    /// <see cref="Bound"/>; any other shape is left unpatched.
    /// </summary>
    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var rewritten = new List<CodeInstruction>(instructions);
        var start = -1;
        for (var index = 0; index < rewritten.Count; index++)
        {
            if (rewritten[index].opcode != OpCodes.Call || !Equals(rewritten[index].operand, PowerBuildSfx)) continue;
            if (start >= 0) throw Refused("calls GetPowerBuildSfx more than once");
            start = index;
        }
        if (start < 0) throw Refused("does not call GetPowerBuildSfx");
        if (start == 0 || !IsBranch(rewritten[start - 1].opcode, OpCodes.Br, OpCodes.Br_S))
            throw Refused("does not enter its power-build loop at the compare");
        var backEdge = start + 1;
        while (backEdge < rewritten.Count && rewritten[backEdge].opcode.FlowControl != FlowControl.Cond_Branch &&
               rewritten[backEdge].opcode.FlowControl != FlowControl.Branch)
            backEdge++;
        if (backEdge == rewritten.Count || !IsBranch(rewritten[backEdge].opcode, OpCodes.Blt, OpCodes.Blt_S) ||
            rewritten[backEdge - 1].opcode != OpCodes.Ldloc_1)
            throw Refused("does not bound its power-build loop by local 1");
        rewritten.Insert(backEdge, new CodeInstruction(OpCodes.Call, BoundMethod));
        return rewritten;
    }

    internal static int Bound(int fires) => MergeRepeatedSounds.IsActive && fires > Cap ? Cap : fires;

    private static bool IsBranch(OpCode opcode, OpCode longForm, OpCode shortForm) =>
        opcode == longForm || opcode == shortForm;

    private static InvalidOperationException Refused(string shape) => new(Target + " " + shape + ".");
}
