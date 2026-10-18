using System;
using System.Linq;
using System.Reflection;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services;

/// <summary>
/// Mechanically enforces the runtime boundary so that namespaces and folders are not the only
/// guard. Nothing under the <c>OrbModding.Common</c> namespaces may carry a feature domain identity
/// or reintroduce the superseded runtime substrate, and the plugin patches exactly the classes it
/// names.
/// <para>
/// The two assembly-reference facts that used to live here — Common referencing no feature plugin,
/// and Automata referencing Common — are gone. Both are about to be answered by a single DLL, where
/// one is vacuously true and the other simply false; every fact left here is keyed on namespaces, so
/// it survives the merge saying the same thing.
/// </para>
/// </summary>
public sealed class ArchitectureBoundaryTests
{
    private static readonly Assembly CommonAssembly = typeof(GameLifecycleMonitor).Assembly;
    private static readonly Assembly AutomataAssembly = typeof(OrbAutomata.BepInExAutomataConfiguration).Assembly;

    private static readonly string[] SupersededRuntimeNamespaces =
    {
        "OrbModding.Common.Runtime.Contracts",
        "OrbModding.Common.Runtime.Host",
        "OrbModding.Common.Runtime.Kernel",
        "OrbModding.Common.Runtime.Lanes",
        "OrbModding.Common.Runtime.Process",
        "OrbModding.Common.Runtime.Telemetry",
        "OrbModding.Common.Runtime.Views",
    };

    private static readonly string[] SupersededRuntimeTypeNames =
    {
        "BoundedTraceWriter",
        "CausalTraceCodec",
        "CausalTraceDocument",
        "CausalTraceRecord",
        "CommandGeneration",
        "CommandId",
        "DemandPulledLiveView",
        "LiveViewBufferPool",
        "PlanGeneration",
        "PolicyGeneration",
        "RuntimeGenerationSet",
        "RuntimeCycleDiagnostics",
        "RuntimeServiceId",
        "RuntimeWorkKey",
        "SnapshotGeneration",
        "StrategyScope",
        "TraceCapture",
        "TraceCaptureDrainReport",
        "TraceWriterMetrics",
        "WorkRequestId",
        "WorkSessionId",
    };

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
    public void CommonContainsNoSupersededRuntimeSubstrate()
    {
        var types = CommonAssembly.GetTypes();
        var namespaceOffenders = types
            .Where(type => SupersededRuntimeNamespaces.Any(prefix =>
                type.Namespace?.StartsWith(prefix, StringComparison.Ordinal) == true))
            .Select(type => type.FullName)
            .ToArray();
        var typeOffenders = types
            .Where(type => SupersededRuntimeTypeNames.Contains(
                type.Name.Split('`')[0],
                StringComparer.Ordinal))
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(namespaceOffenders);
        Assert.Empty(typeOffenders);
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

    [Fact]
    public void LegacyPerformanceCoordinatorSubstrateIsAbsent()
    {
        var forbidden = new[]
        {
            "OrbModding.Common.SuitePerformanceCoordinator",
            "OrbModding.Common.SuiteWorkRegistration",
            "OrbModding.Common.SuiteWorkLease",
            "OrbModding.Common.SuiteWorkAdmission",
            "OrbModding.Common.SuitePerformanceWorkIdentity",
            "OrbModding.Common.SuitePerformanceEvidence",
        };

        Assert.All(forbidden, name => Assert.Null(CommonAssembly.GetType(name, throwOnError: false)));
    }

}
