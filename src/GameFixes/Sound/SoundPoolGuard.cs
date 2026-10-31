using System;
using System.Collections.Generic;
using HarmonyLib;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Keeps a full sound pool from crashing the game's tick. <c>SoundManager.GetAudioElement</c>
/// returns null once every pooled element is playing a loop, and <c>Play</c> and <c>PlayLoop</c>
/// then fault on it, which aborts the rest of that tick. The guard hands out the silent overflow
/// element instead, and keeps the pool from filling with loops nobody owns any more.
/// </summary>
internal sealed class SoundPoolGuard : GameFix
{
    internal const string Target = "SoundManager:GetAudioElement";

    internal static bool IsActive;
    private static long s_overflows;

    private static readonly AutomationFeatureSetting Definition = AutomationFeatureSetting.Define(
        "SoundPoolGuard", true, "Sound pool guard",
        "When every sound channel is busy with a looping sound, the game stops its own update every tick: development, managers and spells stall. This plays the extra sound silently instead, and releases looping sounds the game leaves running. Audio only. Takes effect immediately.",
        10,
        static config => config.GameFixes.SoundPoolGuard,
        static (config, value) => config with { GameFixes = config.GameFixes with { SoundPoolGuard = value } });

    internal override AutomationFeatureSetting Setting => Definition;

    internal override long Fired => s_overflows;

    internal override string DescribeFired(long fired) =>
        fired + (fired == 1 ? " sound" : " sounds") + " played silently because every sound channel was busy.";

    internal override IReadOnlyList<string> Install(Harmony harmony)
    {
        var failures = new List<string>();
        SoundOverflowElement.Install(failures, harmony);
        NativePatch.TryPatch(failures, harmony, AccessTools.Method(Target, Type.EmptyTypes), Target,
            postfix: new HarmonyMethod(typeof(SoundPoolGuard), nameof(GuardHandOut)));
        SoundLoopRelease.Install(failures, harmony);
        return failures;
    }

    internal override void Apply(bool active) => IsActive = active;

    /// <summary>
    /// The pool's answer, just before <c>Play</c> or <c>PlayLoop</c> initializes it. A missing
    /// element becomes the overflow element. A pooled one first forgets its previous owner: a
    /// delayed play still pending on it would otherwise restart whatever it plays next, and a loop
    /// restarted that way holds its channel for good.
    /// </summary>
    internal static void GuardHandOut(SoundManager __instance, ref AudioElement? __result)
    {
        if (!IsActive) return;
        if (__result == null)
        {
            var overflow = SoundOverflowElement.HandOut(__instance);
            if (overflow is null) return;
            __result = overflow;
            s_overflows++;
            return;
        }
        __result.StopAllCoroutines();
        __result.CancelFade();
    }
}
