using System;
using System.Linq;
using System.Reflection;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services;

/// <summary>
/// Enforces the runtime boundary: Common carries no feature-domain identity, and plugin patches
/// target only their declared native classes. Namespace rules apply within the suite assembly.
/// </summary>
public sealed class ArchitectureBoundaryTests
{
    private static readonly Assembly CommonAssembly = typeof(GameLifecycleMonitor).Assembly;
    private static readonly Assembly AutomataAssembly = typeof(OrbAutomata.BepInExAutomataConfiguration).Assembly;

    private static readonly string[] FeatureIdentityFragments =
    {
        "AutoHarvest", "AutoItems", "AutoBuy", "AutoCast", "AutoConcept",
        "AutoSpell", "SpellLevel", "Mentor", "Agrimancy", "Agromancy",
        "Automata", "ModConfig",
    };

    /// <summary>
    /// The only Common types allowed to carry a feature fragment in their name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both are the vocabulary a feature uses to describe its own settings to whoever renders them.
    /// Common owns the contract and Mod Config is merely its reader, so they are not the leak the
    /// rule is looking for — unlike every other fragment, whose feature could not name a Common type
    /// for any honest reason. The fragment is carried rather than dropped so that a real Mod Config
    /// type landing in Common is still caught.
    /// </para>
    /// <para>
    /// Renaming these two would touch every feature that declares config metadata, so it is deferred
    /// as its own change rather than folded in here. <see cref="FeatureIdentityExemptionsAreLive"/>
    /// makes the deferral expire on its own: the day they are renamed, this list must shrink.
    /// </para>
    /// </remarks>
    private static readonly string[] FeatureIdentityExemptions =
    {
        "ModConfigDependency", "ModConfigMetadata",
    };

    [Fact]
    public void CommonContainsNoFeatureDomainIdentityType() =>
        Assert.Empty(FeatureIdentityOffenders("OrbModding.Common"));

    [Fact]
    public void CommonRuntimeContainsNoFeatureDomainIdentityType() =>
        Assert.Empty(FeatureIdentityOffenders("OrbModding.Common.Runtime"));

    /// <summary>
    /// An exemption that no longer names a Common type is a carve-out held open for nothing.
    /// </summary>
    [Fact]
    public void FeatureIdentityExemptionsAreLive()
    {
        var commonTypeNames = CommonAssembly.GetTypes()
            .Where(type => type.Namespace is not null &&
                           type.Namespace.StartsWith("OrbModding.Common", StringComparison.Ordinal))
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(FeatureIdentityExemptions, exemption =>
            Assert.True(
                commonTypeNames.Contains(exemption),
                $"Exempted from the feature-identity rule but no longer a Common type: {exemption}"));
    }

    private static string[] FeatureIdentityOffenders(string namespacePrefix) =>
        CommonAssembly.GetTypes()
            .Where(type => type.Namespace is not null &&
                           type.Namespace.StartsWith(namespacePrefix, StringComparison.Ordinal))
            .Where(type => !FeatureIdentityExemptions.Contains(type.Name, StringComparer.Ordinal))
            .Where(type => FeatureIdentityFragments.Any(fragment =>
                type.Name.IndexOf(fragment, StringComparison.Ordinal) >= 0))
            .Select(type => type.FullName!)
            .ToArray();

    [Fact]
    public void EveryHarmonyFinalizerReturnsVoidSoTheGamesStackTraceSurvives()
    {
        // HarmonyX rethrows a patched method's exception with `throw` instead of `rethrow` as soon as
        // any finalizer returns a value, which cuts the game's frames out of every trace the
        // tick-fault diagnostics report.
        var returning = global::OrbModding.Plugin.HarmonyPatchTypes
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(method => method.Name == "Finalizer" && method.ReturnType != typeof(void))
            .Select(method => method.DeclaringType!.FullName)
            .ToArray();

        Assert.Empty(returning);
    }

    [Fact]
    public void AutomataPatchesExactlyTheClassesItNames()
    {
        // Assembly-wide PatchAll would adopt every [HarmonyPatch] compiled beside it, which after the
        // merge is the whole suite. Plugin names its patch classes instead; this pins the naming to
        // the classes that actually exist, so a new patch class cannot install itself and an existing
        // one cannot be dropped from the list unnoticed.
        var declared = AutomataAssembly.GetTypes()
            .Where(type => type.IsDefined(typeof(HarmonyLib.HarmonyPatchAttribute), inherit: false))
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var patched = global::OrbModding.Plugin.HarmonyPatchTypes
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(declared, patched);
        Assert.Equal(patched.Length, patched.Distinct().Count());
    }
}
