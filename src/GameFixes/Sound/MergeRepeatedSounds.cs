using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OrbModding.Common;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OrbAutomata;

/// <summary>
/// Plays a one-shot sound once per frame. The game can ask for the same clip dozens of times in
/// one frame after a large development batch or a burst of casts; every repeat after the first
/// gets the silent overflow element, so whatever the game chains on it still works. A sound that
/// ends up looping is never merged: <c>PlayLoop</c> is left alone, and a merged one-shot its caller
/// then loops is played on a real element after all.
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
    internal const string LoopTarget = "AudioElement:SetLooping";

    private static readonly MethodInfo? PlayMethod = BindPlay();
    private static readonly FastInvokeHandler? Replay = PlayMethod is null ? null : MethodInvoker.GetHandler(PlayMethod);

    internal static bool IsActive;
    private static readonly HashSet<Object> s_playedThisFrame = new();
    private static int s_frame = -1;
    private static long s_merged;

    private static readonly AutomationFeatureSetting Definition = AutomationFeatureSetting.Define(
        "MergeRepeatedSounds", false, "Merge repeated sounds",
        "Plays a sound once when the game asks for it several times in the same frame, so a development batch plays one power-build sound instead of one per power fire. A sound the game keeps looping always plays. Audio only. Takes effect immediately.",
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
        SoundOverflowElement.Install(failures, harmony);
        if (PlayMethod is null) failures.Add(Target + " is not the audited Play(AudioClip, float).");
        else
            NativePatch.TryPatch(failures, harmony, PlayMethod, Target,
                prefix: new HarmonyMethod(typeof(MergeRepeatedSounds), nameof(PlayOncePerFrame)));
        NativePatch.TryPatch(failures, harmony, AccessTools.Method(LoopTarget, new[] { typeof(bool) }), LoopTarget,
            postfix: new HarmonyMethod(typeof(MergeRepeatedSounds), nameof(KeepMergedLoop)));
        PowerBuildSoundCap.Install(failures, harmony);
        return failures;
    }

    internal override void Apply(bool active)
    {
        IsActive = active;
        if (active) return;
        s_playedThisFrame.Clear();
        s_frame = -1;
    }

    internal static bool PlayOncePerFrame(Object __0, float __1, ref AudioElement __result)
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
        var overflow = SoundOverflowElement.HandOutMerged(manager, __0, __1);
        if (overflow is null) return true;
        __result = overflow;
        s_merged++;
        return false;
    }

    /// <summary>
    /// <c>Spell.StartCharging</c> and <c>UIResearchItem.LoopResearchSound</c> start their loops as
    /// <c>Play().SetLooping()</c> and keep the chained result. When that <c>Play</c> was merged, the
    /// loop is played now on a real element and the caller keeps that one instead.
    /// </summary>
    internal static void KeepMergedLoop(AudioElement __instance, bool __0, ref AudioElement __result)
    {
        if (!__0 || !SoundOverflowElement.IsOverflow(__instance) ||
            !SoundOverflowElement.TakeMerged(out var clip, out var volume))
            return;
        s_playedThisFrame.Remove(clip);
        s_merged--;
        __result = ((AudioElement)Replay!(null!, new object[] { clip, volume })).SetLooping(true);
    }

    private static MethodInfo? BindPlay()
    {
        var play = AccessTools.Method(Target);
        if (play is null || !play.IsStatic || play.ReturnType != typeof(AudioElement)) return null;
        var parameters = play.GetParameters();
        return parameters.Length == 2 && typeof(Object).IsAssignableFrom(parameters[0].ParameterType) &&
            parameters[1].ParameterType == typeof(float)
            ? play
            : null;
    }
}
