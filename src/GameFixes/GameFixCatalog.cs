using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace OrbAutomata;

/// <summary>The one list of game fixes. Settings, installation and health all read it.</summary>
internal static class GameFixCatalog
{
    internal static IReadOnlyList<GameFix> All { get; } = Array.AsReadOnly(new GameFix[]
    {
        new SoundPoolGuard(),
        new MergeRepeatedSounds(),
    });

    private static Harmony? s_installedFor;
    private static IReadOnlyDictionary<GameFix, string> s_failures = new Dictionary<GameFix, string>();

    /// <summary>
    /// Installs every fix once per Harmony instance and returns the fixes that could not be
    /// installed, each with its reason. A fix that is not installed stays off: the game runs its own code.
    /// </summary>
    internal static IReadOnlyDictionary<GameFix, string> Install(Harmony harmony)
    {
        if (ReferenceEquals(s_installedFor, harmony)) return s_failures;
        var failures = new Dictionary<GameFix, string>();
        foreach (var fix in All)
        {
            var lines = fix.Install(harmony);
            if (lines.Count != 0) failures.Add(fix, string.Join(" ", lines));
        }
        s_installedFor = harmony;
        s_failures = failures;
        return failures;
    }

    internal static IEnumerable<AutomationFeatureSetting> Settings => All.Select(fix => fix.Setting);
}
