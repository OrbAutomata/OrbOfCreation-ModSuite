using System;
using HarmonyLib;
using OrbModding.Common;
using Object = UnityEngine.Object;

namespace OrbAutomata;

/// <summary>
/// The one silent <c>AudioElement</c> handed out instead of a sound the game cannot or should not
/// play. It is cloned from the prefab the game builds its pool from, so every call the game chains
/// on a played sound works on it, and it is never part of the pool, so it never takes a channel.
/// </summary>
internal static class SoundOverflowElement
{
    internal const string PlayTarget = "AudioElement:Play";

    private static AudioElement? s_element;
    private static SoundManager? s_owner;
    private static Harmony? s_installedFor;
    private static string? s_failure;

    /// <summary>Silences the element wherever the game plays it, including after a delay.</summary>
    internal static string? Install(Harmony harmony)
    {
        if (ReferenceEquals(s_installedFor, harmony)) return s_failure;
        s_failure = NativePatch.TryPatch(harmony, AccessTools.Method(PlayTarget, Type.EmptyTypes), PlayTarget,
            prefix: new HarmonyMethod(typeof(SoundOverflowElement), nameof(StaySilent)));
        s_installedFor = harmony;
        return s_failure;
    }

    /// <summary>The overflow element for this sound manager, made on first use; null when it has no prefab.</summary>
    internal static AudioElement? For(SoundManager manager)
    {
        if (s_element != null && ReferenceEquals(s_owner, manager)) return s_element;
        var prefab = manager.audioElement;
        if (prefab == null) return null;
        s_element = Object.Instantiate(prefab);
        s_owner = manager;
        return s_element;
    }

    internal static bool IsOverflow(AudioElement element) => ReferenceEquals(element, s_element);

    internal static bool StaySilent(AudioElement __instance, ref AudioElement __result)
    {
        if (!IsOverflow(__instance)) return true;
        __result = __instance;
        return false;
    }
}
