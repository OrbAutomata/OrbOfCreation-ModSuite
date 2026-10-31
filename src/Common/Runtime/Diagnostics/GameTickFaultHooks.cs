using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace OrbModding.Common.Runtime;

/// <summary>
/// Routes the game's per-element tick call and per-view visibility check through
/// <see cref="GameTickFaultObserver"/>. Each transpiler swaps one interface call for a static call
/// with the same arguments and nothing else; a body whose call-site count differs from the audited
/// IL is left unpatched.
/// </summary>
internal static class GameTickFaultHooks
{
    internal const string ElementIteratorTarget = "GameManager+GameElementIterator:Increment";
    internal const string ViewManagerTarget = "ViewManager:CheckViews";

    /// <summary>
    /// <c>GameElementIterator.Increment</c> calls <c>IIncrement.Increment</c> twice: once over its
    /// cached elements and once over the list's lifecycle items.
    /// </summary>
    internal const int ElementCallSites = 2;

    internal const int ViewCallSites = 1;

    internal static readonly MethodInfo NativeIncrement =
        typeof(IIncrement).GetMethod(nameof(IIncrement.Increment), new[] { typeof(float) })!;

    internal static readonly MethodInfo NativeCheckView =
        typeof(ManagedView).GetMethod(nameof(ManagedView.CheckViewVisibility), Type.EmptyTypes)!;

    internal static readonly MethodInfo ObservedIncrement =
        typeof(GameTickFaultObserver).GetMethod(
            nameof(GameTickFaultObserver.IncrementElement),
            BindingFlags.Static | BindingFlags.NonPublic)!;

    internal static readonly MethodInfo ObservedCheckView =
        typeof(GameTickFaultObserver).GetMethod(
            nameof(GameTickFaultObserver.CheckView),
            BindingFlags.Static | BindingFlags.NonPublic)!;

    /// <summary>Installs both hooks; returns one failure line per hook that could not be installed.</summary>
    internal static IReadOnlyList<string> Install(Harmony harmony)
    {
        if (harmony is null) throw new ArgumentNullException(nameof(harmony));
        var failures = new List<string>();
        NativePatch.TryPatch(
            failures,
            harmony,
            AccessTools.Method(ElementIteratorTarget, new[] { typeof(float) }),
            ElementIteratorTarget,
            transpiler: new HarmonyMethod(typeof(GameTickFaultHooks), nameof(TranspileElementIterator)));
        NativePatch.TryPatch(
            failures,
            harmony,
            AccessTools.Method(ViewManagerTarget, Type.EmptyTypes),
            ViewManagerTarget,
            transpiler: new HarmonyMethod(typeof(GameTickFaultHooks), nameof(TranspileViewManager)));
        return failures;
    }

    internal static IEnumerable<CodeInstruction> TranspileElementIterator(
        IEnumerable<CodeInstruction> instructions) =>
        Replace(instructions, NativeIncrement, ObservedIncrement, ElementCallSites, ElementIteratorTarget);

    internal static IEnumerable<CodeInstruction> TranspileViewManager(
        IEnumerable<CodeInstruction> instructions) =>
        Replace(instructions, NativeCheckView, ObservedCheckView, ViewCallSites, ViewManagerTarget);

    internal static List<CodeInstruction> Replace(
        IEnumerable<CodeInstruction> instructions,
        MethodInfo native,
        MethodInfo observed,
        int expectedSites,
        string target)
    {
        var rewritten = new List<CodeInstruction>(instructions);
        var sites = 0;
        foreach (var instruction in rewritten)
        {
            if (instruction.opcode != OpCodes.Callvirt || !Equals(instruction.operand, native)) continue;
            instruction.opcode = OpCodes.Call;
            instruction.operand = observed;
            sites++;
        }
        if (sites != expectedSites)
        {
            throw new InvalidOperationException(
                target + " has " + sites + " " + native.DeclaringType!.Name + "." + native.Name +
                " call site(s), not the audited " + expectedSites + ".");
        }
        return rewritten;
    }
}
