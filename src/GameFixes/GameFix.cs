using System.Collections.Generic;
using HarmonyLib;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

/// <summary>
/// One player-selectable repair to the game's own code. It owns its setting and its patches; the
/// Game Fixes runtime decides when it is active.
/// </summary>
internal abstract class GameFix
{
    internal abstract AutomationFeatureSetting Setting { get; }

    internal string Name => Setting.Label;

    internal bool IsSelected(SuiteRuntimeConfiguration configuration) =>
        Setting.ReadPublishedValue(configuration) is true;

    /// <summary>Installs every patch the fix needs; returns one line per patch that is not in place.</summary>
    internal abstract IReadOnlyList<string> Install(Harmony harmony);

    /// <summary>Switches the installed patches between the fix and the game's own behaviour.</summary>
    internal abstract void Apply(bool active);

    /// <summary>How many times the fix changed what the game did since the plugin loaded.</summary>
    internal abstract long Fired { get; }

    internal abstract string DescribeFired(long fired);
}
