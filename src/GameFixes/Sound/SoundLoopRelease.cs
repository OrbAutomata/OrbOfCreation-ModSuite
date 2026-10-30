using System;
using System.Collections.Generic;
using HarmonyLib;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Releases looping sounds the game would otherwise leave holding a pool channel: a spell's hold
/// or charge loop that is replaced while it still plays, and a research or brewing item's loop
/// when the item is disabled or destroyed. Each release is the game's own: a half-second fade out.
/// </summary>
internal static class SoundLoopRelease
{
    internal const string HoldLoopTarget = "Spell:InitializePersistence";
    internal const string ChargeLoopTarget = "Spell:StartCharging";
    internal const string ItemDisableTarget = "UIRenderGroupElement:OnDisable";

    private const float GameReleaseFadeSeconds = 0.5f;

    internal static void Install(ICollection<string> failures, Harmony harmony)
    {
        NativePatch.TryPatch(failures, harmony, AccessTools.Method(HoldLoopTarget, Type.EmptyTypes), HoldLoopTarget,
            prefix: new HarmonyMethod(typeof(SoundLoopRelease), nameof(ReleaseHoldLoop)));
        NativePatch.TryPatch(failures, harmony, AccessTools.Method(ChargeLoopTarget, Type.EmptyTypes), ChargeLoopTarget,
            prefix: new HarmonyMethod(typeof(SoundLoopRelease), nameof(ReleaseChargeLoop)));
        NativePatch.TryPatch(failures, harmony, AccessTools.Method(ItemDisableTarget, Type.EmptyTypes), ItemDisableTarget,
            postfix: new HarmonyMethod(typeof(SoundLoopRelease), nameof(ReleaseItemLoop)));
    }

    internal static void ReleaseHoldLoop(ref AudioElement? ___loopingElement) => Release(ref ___loopingElement);

    internal static void ReleaseChargeLoop(ref AudioElement? ___chargeFxElement) => Release(ref ___chargeFxElement);

    internal static void ReleaseItemLoop(UIRenderGroupElement __instance)
    {
        if (!SoundPoolGuard.IsActive) return;
        if (__instance is UIResearchItem research) research.DestroyResearchSound();
        else if (__instance is UIBrewingStation brewing) brewing.DestroyLoopingAudio();
    }

    private static void Release(ref AudioElement? loop)
    {
        if (!SoundPoolGuard.IsActive || loop == null) return;
        loop.FadeOutDestroy(GameReleaseFadeSeconds);
        loop = null;
    }
}
