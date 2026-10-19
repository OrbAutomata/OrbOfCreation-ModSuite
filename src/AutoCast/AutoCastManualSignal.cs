using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using OrbModding.Common;

namespace OrbAutomata;

internal static class AutoCastManualSignal
{
    [ThreadStatic]
    private static int _automatedDepth;

    private static long _fireEpoch;
    private static long _manualFireEpoch;

    public static event Action? ManualSpellFired;

    /// <summary>
    /// Every cast the game has made, ours included. The mutation verifier's postcondition reads this:
    /// a submitted cast must advance it.
    /// </summary>
    public static long FireEpoch => Volatile.Read(ref _fireEpoch);

    /// <summary>
    /// Only the casts that were not ours.
    /// </summary>
    /// <remarks>
    /// A counter rather than the event, because the pause it arms is a timestamp and the patch runs
    /// inside the game's own call stack with no clock to hand and no business taking one. Counting is
    /// all a prefix can honestly do; the main thread polls this and stamps the deadline itself.
    /// </remarks>
    public static long ManualFireEpoch => Volatile.Read(ref _manualFireEpoch);

    public static IDisposable EnterAutomatedFire()
    {
        _automatedDepth++;
        return new Scope();
    }

    public static void NotifySpellFire()
    {
        Interlocked.Increment(ref _fireEpoch);
        if (_automatedDepth != 0) return;
        Interlocked.Increment(ref _manualFireEpoch);
        ManualSpellFired?.Invoke();
    }

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _automatedDepth = Math.Max(0, _automatedDepth - 1);
        }
    }
}

[HarmonyPatch]
internal static class SpellFirePatch
{
    private static MethodBase? TargetMethod()
    {
        return ReflectionUtil.FindLoadedType("Spell")?.GetMethod(
            "Fire",
            ReflectionUtil.InstanceFlags,
            null,
            Type.EmptyTypes,
            null);
    }

    private static void Prefix()
    {
        AutoCastManualSignal.NotifySpellFire();
    }
}
