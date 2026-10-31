using System;
using System.Collections.Generic;
using System.Reflection;
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
    internal const string SourceType = "UnityEngine.AudioSource";

    private static readonly FieldInfo? Source = BindSource();
    private static readonly PropertyInfo? Mute = BindMute();

    private static AudioElement? s_element;
    private static SoundManager? s_owner;
    private static Harmony? s_installedFor;
    private static IReadOnlyList<string> s_failures = Array.Empty<string>();
    private static Object? s_mergedClip;
    private static float s_mergedVolume;

    /// <summary>
    /// Requires the element's own sound source, which the clone mutes, and a prefix that keeps the
    /// clone from starting it, so it stays silent even where Mono inlined <c>Play</c>.
    /// </summary>
    internal static void Install(ICollection<string> failures, Harmony harmony)
    {
        if (!ReferenceEquals(s_installedFor, harmony))
        {
            var own = new List<string>();
            if (Mute is null) own.Add("AudioElement.audioSource is not the audited " + SourceType + " with a settable mute.");
            NativePatch.TryPatch(own, harmony, AccessTools.Method(PlayTarget, Type.EmptyTypes), PlayTarget,
                prefix: new HarmonyMethod(typeof(SoundOverflowElement), nameof(StaySilent)));
            s_failures = own;
            s_installedFor = harmony;
        }
        foreach (var failure in s_failures) failures.Add(failure);
    }

    /// <summary>The overflow element for a sound that found no free channel; null when the manager has no prefab.</summary>
    internal static AudioElement? HandOut(SoundManager manager)
    {
        s_mergedClip = null;
        return For(manager);
    }

    /// <summary>
    /// The overflow element for a one-shot merged into the same clip earlier this frame. The clip
    /// is remembered until the next hand-out, so a caller that loops it can still be given the sound.
    /// </summary>
    internal static AudioElement? HandOutMerged(SoundManager manager, Object clip, float volume)
    {
        var element = For(manager);
        s_mergedClip = element is null ? null : clip;
        s_mergedVolume = volume;
        return element;
    }

    /// <summary>The clip of the merged hand-out the overflow element last went to, at most once.</summary>
    internal static bool TakeMerged(out Object clip, out float volume)
    {
        clip = s_mergedClip!;
        volume = s_mergedVolume;
        s_mergedClip = null;
        return clip is not null;
    }

    internal static bool IsOverflow(AudioElement element) => ReferenceEquals(element, s_element);

    internal static bool StaySilent(AudioElement __instance, ref AudioElement __result)
    {
        if (!IsOverflow(__instance)) return true;
        __result = __instance;
        return false;
    }

    private static AudioElement? For(SoundManager manager)
    {
        if (s_element != null && ReferenceEquals(s_owner, manager)) return s_element;
        var prefab = manager.audioElement;
        if (prefab == null) return null;
        var element = Object.Instantiate(prefab);
        Mute!.SetValue(Source!.GetValue(element), true);
        s_element = element;
        s_owner = manager;
        return element;
    }

    private static FieldInfo? BindSource()
    {
        var field = typeof(AudioElement).GetField("audioSource", BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.FieldType.FullName == SourceType ? field : null;
    }

    private static PropertyInfo? BindMute()
    {
        var mute = Source?.FieldType.GetProperty("mute", BindingFlags.Instance | BindingFlags.Public);
        return mute?.PropertyType == typeof(bool) && mute.CanWrite ? mute : null;
    }
}
