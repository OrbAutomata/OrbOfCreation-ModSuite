using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

/// <summary>
/// Reading a tooltip changes nothing in the game, whichever entity it is read for and whether or
/// not the screen is drawing it.
/// </summary>
/// <remarks>
/// <para>
/// <c>game_tooltip</c> reads an entity's own tooltip when the screen draws nothing about it, so
/// the set of tooltip builders the suite runs is every builder in the game, not only the ones the
/// current screen happens to have up. A read with a side effect is the defect class
/// <c>LoadoutManager.CanSwapLoadouts</c> was: it answered a question and played the error sound
/// and raised a popup on the way. So every method the reader calls is a root, the whole call graph
/// below them is walked, and no path may reach a sound, a popup, an event-log entry, a modal, a
/// spawned or destroyed object, or a modifier being applied or removed.
/// </para>
/// <para>
/// The game's own lazily recomputed values are not on that list. A tooltip reads its numbers
/// through the same accessors the screen does when the player hovers, and those refresh their
/// cached result on read; that is the read the game itself takes, and nothing the player sees or
/// keeps changes because of it.
/// </para>
/// </remarks>
public sealed class TooltipReadSideEffectContractTests
{
    private static readonly (string Owner, string[] Methods)[] ReaderCalls =
    {
        ("ITooltipable", new[]
        {
            "GetName", "GetDisplayType", "GetDescription", "HasAltTooltips", "GetTooltipNodes",
            "GetAltTooltipNodes",
        }),
        ("IHasResourceCost", new[] { "GetResourceCost" }),
        ("IHasResourceCostPrefix", new[] { "GetResourceCostPrefix" }),
        ("IHasUsageCost", new[] { "GetUsageCost" }),
        ("IHasRequirements", new[] { "GetRequirements" }),
        ("IHasExperienceElement", new[] { "GetExperienceElement" }),
        ("IExperienceElement", new[] { "GetExperienceLevel", "IsForceVisible" }),
        ("ResourceCostList", new[] { "IsEmpty" }),
        ("ResourceTuple", new[] { "GetValue" }),
        ("ResourceSO", new[] { "GetTrueSpend" }),
    };

    [GameAssemblyFact]
    public void NoTooltipBuilderReachesAGameSideEffect()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var graph = GameCallGraph.Build(assembly);

        var roots = ReaderCalls.SelectMany(call => graph.MethodsBelow(call.Owner, call.Methods)).ToArray();
        var paths = graph.PathsTo(roots, IsGameSideEffect);

        Assert.True(roots.Length > 250, "only " + roots.Length + " tooltip readers were found to walk from");
        Assert.True(graph.Reached > 2000, "the walk reached only " + graph.Reached + " methods");
        Assert.True(paths.Count == 0, string.Join(Environment.NewLine, paths));
    }

    /// <summary>
    /// The walk can see the defect it exists to catch: the read that played the error sound is
    /// found by the same predicate over the same graph.
    /// </summary>
    [GameAssemblyFact]
    public void TheWalkFindsTheReadThatPlayedTheErrorSound()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var graph = GameCallGraph.Build(assembly);

        var paths = graph.PathsTo(
            graph.MethodsBelow("LoadoutManager", new[] { "CanSwapLoadouts" }), IsGameSideEffect);

        Assert.Contains(paths, path => path.EndsWith("-> SoundEffectSO.Play", StringComparison.Ordinal));
        Assert.Contains(paths, path => path.EndsWith("-> UIPopupText.Create", StringComparison.Ordinal));
    }

    private static bool IsGameSideEffect(string type, string name) => type switch
    {
        "SoundEffectSO" or "AudioInstance" or "AudioElement" =>
            name.StartsWith("Play", StringComparison.Ordinal) ||
            name.StartsWith("Fade", StringComparison.Ordinal) ||
            name is "Stop" or "StartPlayingAfter",
        "AudioManager" or "SoundManager" or "LeanAudio" => true,
        "UnityEngine.AudioSource" => name.StartsWith("Play", StringComparison.Ordinal),
        "UIPopupText" or "EventLogManager" => name.StartsWith("Create", StringComparison.Ordinal),
        "UIModal" => name.StartsWith("Open", StringComparison.Ordinal) ||
            name.StartsWith("Close", StringComparison.Ordinal) ||
            name.StartsWith("Toggle", StringComparison.Ordinal) ||
            name == "SetElementVisibility",
        "UIModalActivator" => true,
        "UnityEngine.Object" => name == "Instantiate" || name.StartsWith("Destroy", StringComparison.Ordinal),
        "UnityEngine.GameObject" => name == "SetActive",
        "EffectExecutionInfo" => name is "ImplementModifier" or "RemoveModifier",
        _ => false,
    };
}
