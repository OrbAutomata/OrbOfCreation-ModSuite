using System;
using System.Collections.Generic;
using HarmonyLib;
using OrbModding.Common;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OrbAutomata;

/// <summary>
/// Plays a one-shot sound once per frame. The game can ask for the same clip dozens of times in
/// one frame after a large development batch or a burst of casts; every repeat after the first
/// gets the silent overflow element, so whatever the game chains on it still works. Looping
/// sounds are left alone because their owners stop them later.
/// </summary>
/// <remarks>
/// The merge key is the clip alone. <c>SoundManager.Play(AudioClip, float)</c> passes its second
/// argument to <c>SetVolume</c>; a delay is set by the caller afterwards, on the returned element,
/// so two requests for one clip in one frame differ at most in volume. The clip is held as a
/// Unity object because the suite does not compile against the audio module; the game declares one
/// <c>Play</c>, which the installed-game contracts pin.
/// </remarks>
internal sealed class MergeRepeatedSounds : GameFix
{
    internal const string Target = "SoundManager:Play";

    internal static bool IsActive;
    private static readonly HashSet<Object> s_playedThisFrame = new();
    private static int s_frame = -1;
    private static long s_merged;

    private static readonly AutomationFeatureSetting Definition = AutomationFeatureSetting.Define(
        "MergeRepeatedSounds", false, "Merge repeated sounds",
        "Plays a sound once when the game asks for it several times in the same frame, and plays at most three power-build sounds after a development batch instead of one per power fire. Audio only. Takes effect immediately.",
        20,
        static config => config.GameFixes.MergeRepeatedSounds,
        static (config, value) => config with { GameFixes = config.GameFixes with { MergeRepeatedSounds = value } });

    internal override AutomationFeatureSetting Setting => Definition;

    internal override long Fired => s_merged;

    internal override string DescribeFired(long fired) =>
        fired + (fired == 1 ? " repeated sound" : " repeated sounds") + " merged.";

    internal override IReadOnlyList<string> Install(Harmony harmony)
    {
        var failures = new List<string>();
        Add(failures, SoundOverflowElement.Install(harmony));
        Add(failures, NativePatch.TryPatch(harmony,
            AccessTools.Method(Target), Target,
            prefix: new HarmonyMethod(typeof(MergeRepeatedSounds), nameof(PlayOncePerFrame))));
        Add(failures, PowerBuildSoundCap.Install(harmony));
        return failures;
    }

    internal override void Apply(bool active)
    {
        IsActive = active;
        if (active) return;
        s_playedThisFrame.Clear();
        s_frame = -1;
    }

    internal static bool PlayOncePerFrame(Object __0, ref AudioElement __result)
    {
        if (!IsActive || __0 == null) return true;
        var frame = Time.frameCount;
        if (frame != s_frame)
        {
            s_frame = frame;
            s_playedThisFrame.Clear();
        }
        if (s_playedThisFrame.Add(__0)) return true;
        var manager = SoundManager.instance;
        if (manager == null) return true;
        var overflow = SoundOverflowElement.For(manager);
        if (overflow is null) return true;
        __result = overflow;
        s_merged++;
        return false;
    }

    private static void Add(List<string> failures, string? failure)
    {
        if (failure is not null) failures.Add(failure);
    }
}
