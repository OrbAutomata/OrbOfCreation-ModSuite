using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using OrbModding.Common;
using OrbModding.TestSupport;
using Xunit;
using static OrbModding.GameContractTests.NativeSourceAudit;

namespace OrbModding.GameContractTests;

public sealed class NativeContractManifestTests
{
    /// <summary>The game type each category walks, as its binder declares it.</summary>
    private static readonly Regex TypeNamePattern = new(
        "TypeName\\s*=>\\s*\"(?<type>[A-Za-z_][A-Za-z0-9_]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The field types a collected type must declare a contract for. Everything else — collections,
    /// entity references, Unity objects — is governed by its own rule in D17 rather than by this one.
    /// </summary>
    private static readonly HashSet<string> ScalarFieldTypes = new(StringComparer.Ordinal)
    {
        "System.Boolean", "System.Int32", "System.Int64", "System.Double", "System.Single",
        "System.String", "BigDouble",
    };

    private static readonly HashSet<string> ModifierRecordTypes = new(StringComparer.Ordinal)
    {
        "ValueModifierRecord", "ModifierRecord", "OrderedMultiplierRecord", "MergingModifierRecord",
    };

    /// <summary>
    /// Contracts outside the service-cycle boundary model which are therefore still allowed to
    /// declare <c>"place": "legacy"</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This list only ever shrinks. Migrating a service deletes its native access, so every id in
    /// that service's block either disappears from the manifest or stops being legacy — and both
    /// fail <see cref="EveryLegacyContractIsAnAllowlistedBoundaryException"/> until the block is
    /// removed. Nothing has to remember to prune it.
    /// </para>
    /// <para>
    /// <c>legacy</c> outranks the structural places rather than sitting beside them: migration-only
    /// Harmony patches remain migration debt until their owning service moves.
    /// </para>
    /// </remarks>
    private static readonly string[] LegacyBoundaryExceptions = Array.Empty<string>();

    /// <summary>The places a contract may sit in once its service is on ServiceCycle.</summary>
    /// <remarks>
    /// The first three are where the suite touches the member. <c>mirrored</c> is where a contract
    /// sits when the suite touches it nowhere: the row exists to make a copied value answer for the
    /// member it copies, and filing it under one of the touching places made the capture census
    /// count sweeps that never run.
    /// </remarks>
    private static readonly string[] LivePlaces = { "capture", "action", "patch", "mirrored" };

    /// <summary>
    /// How the suite depends on a native member. The first three touch it; <c>mirrored</c> does not.
    /// </summary>
    /// <remarks>
    /// A mirrored contract is a member-shape dependency the suite relies on <em>without</em>
    /// reflecting on, patching, or calling it: a suite constant whose value is only correct because
    /// of what that member holds. The ritual starting-level floor and the two casting-dial floors
    /// are all the literal <c>1</c> because <c>UIValueSelectButton.SetClamp</c> stores it in
    /// <c>minValue</c> and the control's decrement never goes below it. Nothing reflects on either
    /// member, so before this value existed the dependency could only be written as a comment —
    /// and a comment does not fail when a game update changes the member's shape. Declaring it
    /// makes the audit answer for it like any other contract.
    /// </remarks>
    private static readonly string[] DeclarableUsages =
        { "direct", "reflection", "harmony", "mirrored" };

    /// <summary>
    /// Every contract is either owned by a live place — collected into the world snapshot, read at
    /// an action boundary, or Harmony-patched — or is named debt of a service that has not migrated.
    /// </summary>
    /// <remarks>
    /// The manifest recorded which native members the suite touches but never where, so nothing
    /// distinguished a member read once during capture from one reflected on mid-action. The north
    /// star is capture-once-then-decide, and that claim was unmeasurable while every contract looked
    /// alike.
    /// </remarks>
    [Fact]
    public void EveryLegacyContractIsAnAllowlistedBoundaryException()
    {
        var manifest = NativeContractManifest.Load();
        var allowlist = LegacyBoundaryExceptions.ToHashSet(StringComparer.Ordinal);
        Assert.Equal(LegacyBoundaryExceptions.Length, allowlist.Count);

        var byId = manifest.Contracts.ToDictionary(contract => contract.Id, StringComparer.Ordinal);
        var failures = new List<string>();

        foreach (var id in LegacyBoundaryExceptions)
        {
            if (!byId.TryGetValue(id, out var contract))
            {
                failures.Add($"{id}: allowlisted as unmigrated-service debt but no longer in the manifest; drop it from the allowlist");
                continue;
            }

            if (contract.Place != "legacy")
            {
                failures.Add($"{id}: allowlisted as unmigrated-service debt but now declares place '{contract.Place}'; drop it from the allowlist");
            }
        }

        foreach (var contract in manifest.Contracts)
        {
            if (allowlist.Contains(contract.Id))
            {
                continue;
            }

            if (!LivePlaces.Contains(contract.Place, StringComparer.Ordinal))
            {
                failures.Add(
                    $"{contract.Id}: place '{contract.Place}' is not one of {string.Join(", ", LivePlaces)}; "
                        + "a contract may only be legacy while it is listed as an unmigrated service's debt");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Manifest_IsCompleteAndInternallyConsistent()
    {
        var manifest = NativeContractManifest.Load();
        var repositoryRoot = RepositorySources.Root;

        Assert.Equal(5, manifest.SchemaVersion);

        // Reconciled against the file, never pinned to a number. A literal is a second place to
        // remember when a contract lands, and it silently drifted for nine of them; what has to
        // hold is that every declaration written into the manifest survives into the loaded model.
        Assert.Equal(DeclaredContractCount(), manifest.Contracts.Count);
        Assert.Equal(9, manifest.SourceAudit.Exemptions.Count);
        Assert.False(string.IsNullOrWhiteSpace(manifest.AuditedAt));
        Assert.False(string.IsNullOrWhiteSpace(manifest.GameBuild));
        Assert.False(string.IsNullOrWhiteSpace(manifest.Provenance));
        Assert.NotEmpty(manifest.Assemblies);
        Assert.NotEmpty(manifest.Contracts);

        Assert.All(manifest.Assemblies, assembly =>
        {
            Assert.False(string.IsNullOrWhiteSpace(assembly.Id));
            Assert.EndsWith(".dll", assembly.File, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(manifest.Assemblies.Count, manifest.Assemblies.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());

        Assert.NotEmpty(manifest.Baselines);
        Assert.Equal(
            manifest.Baselines.Count,
            manifest.Baselines.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(manifest.Baselines, baseline =>
        {
            Assert.False(string.IsNullOrWhiteSpace(baseline.Id));
            Assert.Contains(baseline.Platform, new[] { "windows", "macos" });
            Assert.False(string.IsNullOrWhiteSpace(baseline.AuditedAt));
            Assert.False(string.IsNullOrWhiteSpace(baseline.GameBuild));
            Assert.False(string.IsNullOrWhiteSpace(baseline.Provenance));
            AssertNoUserSpecificPath(baseline.Provenance);
            Assert.NotEmpty(baseline.Assemblies);
            Assert.Equal(
                baseline.Assemblies.Count,
                baseline.Assemblies.Select(item => item.Assembly).Distinct(StringComparer.Ordinal).Count());
            Assert.All(baseline.Assemblies, assembly =>
            {
                Assert.Contains(manifest.Assemblies, declared => declared.Id == assembly.Assembly);
                Assert.Matches("^[A-F0-9]{64}$", assembly.Sha256);
                Assert.False(string.IsNullOrWhiteSpace(assembly.Provenance));
                AssertNoUserSpecificPath(assembly.Provenance);
                Assert.False(Path.IsPathRooted(assembly.Provenance));
            });
        });
        Assert.All(
            manifest.Assemblies,
            declared => Assert.Contains(
                manifest.Baselines.SelectMany(baseline => baseline.Assemblies),
                baselineAssembly => baselineAssembly.Assembly == declared.Id));

        Assert.Equal(manifest.Contracts.Count, manifest.Contracts.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(manifest.Contracts, contract =>
        {
            Assert.False(string.IsNullOrWhiteSpace(contract.Id));
            Assert.Contains(manifest.Assemblies, assembly => assembly.Id == contract.Assembly);
            Assert.False(string.IsNullOrWhiteSpace(contract.Type));
            Assert.Contains(contract.TypeVisibility, new[] { "public", "private", "family", "assembly", "family-or-assembly", "family-and-assembly" });
            Assert.Contains(contract.Kind, new[] { "type", "field", "method" });
            Assert.NotEmpty(contract.Owners);
            Assert.All(contract.Owners, owner => Assert.False(string.IsNullOrWhiteSpace(owner)));
            Assert.NotEmpty(contract.Usages);
            Assert.All(contract.Usages, usage => Assert.Contains(usage, DeclarableUsages));
            Assert.Contains(contract.Place, LivePlaces.Append("legacy"));

            if (contract.Kind == "field")
            {
                Assert.False(string.IsNullOrWhiteSpace(contract.Member));
                Assert.False(string.IsNullOrWhiteSpace(contract.Visibility));
                Assert.NotNull(contract.Static);
                Assert.False(string.IsNullOrWhiteSpace(contract.ValueType));
            }
            else if (contract.Kind == "method")
            {
                Assert.False(string.IsNullOrWhiteSpace(contract.Member));
                Assert.False(string.IsNullOrWhiteSpace(contract.Visibility));
                Assert.NotNull(contract.Static);
                Assert.False(string.IsNullOrWhiteSpace(contract.ReturnType));
            }
            else
            {
                Assert.Null(contract.Member);
            }
        });

        Assert.NotEmpty(manifest.SourceAudit.Roots);
        Assert.Equal(
            manifest.SourceAudit.Roots.Count,
            manifest.SourceAudit.Roots.Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(manifest.SourceAudit.Roots, root =>
            Assert.True(
                Directory.Exists(Path.Combine(repositoryRoot, root.Replace('/', Path.DirectorySeparatorChar))),
                $"Source-audit root does not exist: {root}"));
        Assert.Equal(
            manifest.SourceAudit.Exemptions.Count,
            manifest.SourceAudit.Exemptions.Select(item => NormalizePath(item.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// Every source directory under <c>src/</c> must be an audit root, so that reflection added
    /// anywhere in it is walked.
    /// </summary>
    /// <remarks>
    /// A missing root does not fail — the walk simply never visits that directory and reports nothing,
    /// which reads exactly like having nothing to report. <c>src/Common</c> was missing for
    /// the whole life of the manifest while holding live game reflection, and was about to receive the
    /// world collector on top of it (W26). Enumerating directories that contain real C# source rather
    /// than listing them is the point: a new one is audited by existing, not by someone remembering.
    /// The one suite project now lives at <c>src/</c> itself, so current and legacy build output can
    /// land beside the feature folders; build output is not source and is never audited.
    /// </remarks>
    [Fact]
    public void EverySourceDirectoryIsAnAuditRoot()
    {
        var manifest = NativeContractManifest.Load();
        var repositoryRoot = RepositorySources.Root;
        var roots = manifest.SourceAudit.Roots
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var directories = RepositorySources
            .SourceFiles("*.cs")
            .Select(path => NormalizePath(Path.GetRelativePath(repositoryRoot, path)).Split('/'))
            .Where(segments => segments.Length > 2)
            .Select(segments => segments[0] + "/" + segments[1])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(directories);
        Assert.All(directories, directory =>
            Assert.True(
                roots.Contains(directory),
                $"Source directory is not a native-contract audit root, so reflection in it is never "
                    + $"checked: {directory}"));
    }

    [Fact]
    public void RuntimeHashGuards_MatchEveryManifestBaselinePair()
    {
        var manifest = NativeContractManifest.Load();

        Assert.Equal(4, manifest.Baselines.Count);
        AssertRuntimeBaseline(manifest, GameAssemblyAudit.WindowsSteamBaseline);
        AssertRuntimeBaseline(manifest, GameAssemblyAudit.WindowsV1052SteamBaseline);
        AssertRuntimeBaseline(manifest, GameAssemblyAudit.MacSteamBaseline);
        AssertRuntimeBaseline(manifest, GameAssemblyAudit.MacV1052SteamBaseline);
    }

    /// <summary>
    /// The installed assemblies are one of the audited baseline pairs, byte for byte.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="InstalledGame_MatchesEveryDeclaredContract"/> because the two answer
    /// different questions and a re-audit needs them apart: a build that is deliberately not yet a
    /// baseline fails this one by definition, while the contract sweep below is exactly what has to
    /// be run against it before anyone stamps it. Folding them together made the sweep unrunnable
    /// for the one build it matters most for. See <c>script/re-audit</c>.
    /// </remarks>
    [GameAssemblyFact]
    public void InstalledGame_IsAnAuditedBaseline()
    {
        var manifest = NativeContractManifest.Load();
        var paths = GameAssemblyPaths.Require();
        var failures = new List<string>();
        var audit = GameAssemblyAudit.Check(paths.GameRoot);
        if (!audit.MatchesExpected)
        {
            failures.Add(
                $"Unknown assembly pair: main={audit.AssemblyCSharp.ActualSha256 ?? "<missing>"}, " +
                $"first-pass={audit.AssemblyCSharpFirstPass.ActualSha256 ?? "<missing>"}; " +
                $"discovery={audit.DiscoveryFailure}");
        }
        else
        {
            var baseline = manifest.RequireBaseline(audit.MatchedBaselineId);
            foreach (var baselineAssembly in baseline.Assemblies)
            {
                var assemblyEntry = manifest.Assemblies.Single(
                    item => item.Id == baselineAssembly.Assembly);
                using var stream = File.OpenRead(Path.Combine(paths.ManagedDirectory, assemblyEntry.File));
                var actualHash = Convert.ToHexString(SHA256.HashData(stream));
                var expectedHash = baselineAssembly.Sha256;
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"{assemblyEntry.File}: expected SHA-256 {expectedHash}, actual {actualHash}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Every declared contract still names a member of the shape the shipped assembly declares.
    /// </summary>
    [GameAssemblyFact]
    public void InstalledGame_MatchesEveryDeclaredContract()
    {
        var manifest = NativeContractManifest.Load();
        var paths = GameAssemblyPaths.Require();
        var failures = new List<string>();

        foreach (var assemblyEntry in manifest.Assemblies)
        {
            using var metadata = new GameAssemblyMetadata(
                Path.Combine(paths.ManagedDirectory, assemblyEntry.File));
            foreach (var contract in manifest.Contracts.Where(contract => contract.Assembly == assemblyEntry.Id))
            {
                ValidateContract(metadata, contract, failures);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Every declared contract resolves member-exact against the metadata-only references committed
    /// for game-free builds and CI contract verification.
    /// </summary>
    /// <remarks>
    /// This is deliberately independent of <c>OOC_GAME_DIR</c>. In particular, the private-member
    /// non-empty private set prevents a public-only Refasmer regeneration from looking complete;
    /// the member-exact loop below proves every declared private member independently.
    /// </remarks>
    [Fact]
    public void CheckedInGameReferences_MatchEveryDeclaredContract()
    {
        var manifest = NativeContractManifest.Load();
        var repositoryRoot = RepositorySources.Root;
        var referenceRoot = Path.Combine(repositoryRoot, "lib", "game-refs", "v1.0.5");
        var failures = new List<string>();

        Assert.Contains(manifest.Contracts, contract => contract.Visibility == "private");

        foreach (var assemblyEntry in manifest.Assemblies)
        {
            var path = Path.Combine(referenceRoot, assemblyEntry.File);
            if (!File.Exists(path))
            {
                failures.Add($"{assemblyEntry.File}: checked-in reference assembly was not found at {path}");
                continue;
            }

            using var metadata = new GameAssemblyMetadata(path);
            foreach (var contract in manifest.Contracts.Where(contract => contract.Assembly == assemblyEntry.Id))
            {
                ValidateContract(metadata, contract, failures);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Every value-shaped member of every type world collection walks is declared by a contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is D17 as a gate rather than a habit. The collector's first pass over twenty-five
    /// categories was built from each type's <c>SaveDataBase</c> record and looked complete while
    /// omitting 165 scalars and 125 modifier records — every value the game recomputes on load rather
    /// than persisting. Nothing failed. A consumer would simply have found nothing where there was
    /// something.
    /// </para>
    /// <para>
    /// The check runs against the shipped assembly's metadata, so it measures the collector against
    /// the type the game actually ships rather than against a decompile or a stub. A category that
    /// gains a field in a future build fails here until it is either collected or exempted in the
    /// manifest with a reason.
    /// </para>
    /// <para>
    /// A field read through an accessor still needs its own contract. <c>UpgradeSO.level</c> is
    /// collected by calling <c>GetPurchaseLevel()</c>, which returns it — so the suite depends on
    /// that field's name and type just as directly as if it read it, and a manifest that recorded
    /// only the method would not say so.
    /// </para>
    /// <para>
    /// Only scalars and modifier records are required. A collection, a reference to another entity,
    /// and a Unity object are all deliberately out of scope — the first two have their own rules in
    /// D17 and the third is not state.
    /// </para>
    /// </remarks>
    [GameAssemblyFact]
    public void EveryValueMemberOfACollectedTypeIsDeclared()
    {
        var manifest = NativeContractManifest.Load();
        var paths = GameAssemblyPaths.Require();
        var repositoryRoot = RepositorySources.Root;

        var worldDirectory = Path.Combine(
            repositoryRoot, "src", "Common", "Runtime", "World");

        // The directory is named here rather than discovered, so the next move of the world
        // collector silently points this walk at nothing. Say so in the test's own words instead
        // of leaving a DirectoryNotFoundException to explain it.
        Assert.True(
            Directory.Exists(worldDirectory),
            $"World collector directory not found, so nothing was checked: {worldDirectory}");

        var collected = Directory
            .EnumerateFiles(worldDirectory, "World*.cs", SearchOption.AllDirectories)
            .SelectMany(file => TypeNamePattern.Matches(File.ReadAllText(file)).Cast<Match>())
            .Select(match => match.Groups["type"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Asserted as an exact count rather than merely non-empty. This walk went blind once
        // already: the binders moved into a Categories/ subdirectory while the search was still
        // top-level, so it matched three variable binders, found their contracts, and passed — while
        // twenty-nine categories went unchecked. NotEmpty cannot tell "everything is declared" from
        // "almost nothing was looked at". Update the number when a category is added or removed.
        Assert.Equal(56, collected.Length);

        var declared = manifest.Contracts
            .Where(contract => contract.Assembly == "assembly-csharp")
            .Select(contract => (contract.Type, contract.Member))
            .ToHashSet();

        var path = Path.Combine(paths.ManagedDirectory, "Assembly-CSharp.dll");
        using var metadata = new GameAssemblyMetadata(path);
        var failures = new List<string>();

        foreach (var type in collected)
        {
            if (!metadata.HasType(type))
            {
                failures.Add($"{type}: collected but absent from the shipped assembly");
                continue;
            }

            foreach (var field in metadata.GetFields(type))
            {
                if (field.IsStatic || !IsValueShaped(field.FieldType)) continue;
                if (declared.Contains((type, field.Name))) continue;
                failures.Add($"{type}.{field.Name} ({field.FieldType}) is not declared by any contract");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static bool IsValueShaped(string fieldType) =>
        ScalarFieldTypes.Contains(fieldType) || ModifierRecordTypes.Contains(fieldType);

    private static void AssertRuntimeBaseline(
        NativeContractManifest manifest,
        GameAssemblyBaseline runtime)
    {
        Assert.True(runtime.IsValid);
        var declared = manifest.RequireBaseline(runtime.Id);
        Assert.Equal(
            runtime.AssemblyCSharpSha256,
            declared.RequireAssembly("assembly-csharp").Sha256,
            ignoreCase: true);
        Assert.Equal(
            runtime.FirstPassSha256,
            declared.RequireAssembly("assembly-csharp-firstpass").Sha256,
            ignoreCase: true);
    }

    private static void AssertNoUserSpecificPath(string value)
    {
        Assert.DoesNotContain("/Users/", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/home/", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("^[A-Za-z]:[/\\\\]Users[/\\\\]", value);
    }

    private static void ValidateContract(
        GameAssemblyMetadata metadata,
        NativeContractEntry expected,
        ICollection<string> failures)
    {
        try
        {
            var type = metadata.GetType(expected.Type);
            if (type.Visibility != expected.TypeVisibility)
            {
                failures.Add($"{expected.Id}: type visibility expected {expected.TypeVisibility}, actual {type.Visibility}");
            }

            if (expected.Kind == "type")
            {
                if (expected.BaseType is not null && type.BaseType != expected.BaseType)
                {
                    failures.Add($"{expected.Id}: base type expected {expected.BaseType}, actual {type.BaseType}");
                }
                return;
            }

            if (expected.Kind == "field")
            {
                var field = metadata.GetField(expected.Type, expected.Member!);
                if (field.Visibility != expected.Visibility || field.IsStatic != expected.Static || field.FieldType != expected.ValueType)
                {
                    failures.Add(
                        $"{expected.Id}: expected {expected.Visibility} {(expected.Static == true ? "static " : string.Empty)}{expected.ValueType}, " +
                        $"actual {field.Visibility} {(field.IsStatic ? "static " : string.Empty)}{field.FieldType}");
                }
                return;
            }

            var matches = metadata.GetMethods(expected.Type, expected.Member!);
            if (!matches.Any(method =>
                    method.Visibility == expected.Visibility &&
                    method.IsStatic == expected.Static &&
                    method.ReturnType == expected.ReturnType &&
                    method.ParameterTypes.SequenceEqual(expected.Parameters)))
            {
                var actual = string.Join(
                    "; ",
                    matches.Select(method =>
                        $"{method.Visibility} {(method.IsStatic ? "static " : string.Empty)}{method.ReturnType}({string.Join(",", method.ParameterTypes)})"));
                failures.Add(
                    $"{expected.Id}: expected {expected.Visibility} {(expected.Static == true ? "static " : string.Empty)}" +
                    $"{expected.ReturnType}({string.Join(",", expected.Parameters)}); actual [{actual}]");
            }
        }
        catch (Exception exception)
        {
            failures.Add($"{expected.Id}: {exception.Message}");
        }
    }

    /// <summary>
    /// How many contracts the checked-in manifest declares, read straight from the file with a
    /// second parser rather than through the typed model the assertion is about.
    /// </summary>
    private static int DeclaredContractCount()
    {
        var path = Path.Combine(
            RepositorySources.Root, "data", "native-contracts.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var declared = document.RootElement.GetProperty("contracts").GetArrayLength();
        Assert.True(declared > 0, $"The manifest at {path} declares no contracts.");
        return declared;
    }
}
