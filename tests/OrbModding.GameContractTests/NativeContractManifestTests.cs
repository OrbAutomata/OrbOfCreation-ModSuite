using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using OrbModding.Common;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class NativeContractManifestTests
{
    private static readonly Regex QualifiedTargetPattern = new(
        "\"(?<type>[A-Za-z_][A-Za-z0-9_.+`]*):(?<member>[A-Za-z_][A-Za-z0-9_]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TypeResolverPattern = new(
        "(?:AccessTools\\.TypeByName|ReflectionUtil\\.FindLoadedType)\\s*\\(\\s*\"(?<type>[A-Za-z_][A-Za-z0-9_.+`]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A reflection selector and the literal it names, which need not share a physical line.
    /// </summary>
    /// <remarks>
    /// The gap was <c>[^;\r\n]</c> — same line only — so a call wrapped across lines named a native
    /// target the audit could not see. Twenty-five such calls were live when this was widened, among
    /// them <c>AutoBuyNativeQueueRoomAdapter</c>'s <c>GetRemainingRoom</c>. Stopping at the statement
    /// terminator still keeps one call's literals from bleeding into the next.
    /// </remarks>
    private static readonly Regex LiteralSelectorPattern = new(
        "(?:GetMethod|GetField|GetProperty|FindMethod|FindField|FindNoArgMethod|ResolveStaticNoArgMethod|InvokeNoArgs|TryInvokeBool|ReadMember|ReadBool|ReadInt|InvokeRequired|ReadStaticList)\\s*\\([^;]*?\"(?<target>[A-Za-z_][A-Za-z0-9_.+`]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ReflectionUsePattern = new(
        "(?:HarmonyPatch|AccessTools\\.(?:Method|TypeByName)|ReflectionUtil\\.FindLoadedType"
            + "|NativeAccessorBinder\\.|WorldMemberBinding"
            + "|\\.(?:GetMethod|GetMethods|GetField|GetFields|GetProperty|GetProperties)\\s*\\("
            + "|\\b(?:FindMethod|FindField)\\s*\\()",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// World-category binders name their native targets two ways that no reflection API appears in:
    /// as arguments to the accessor-binding helper, and as the <c>TypeName</c> and
    /// <c>RegistryMember</c> overrides that say which type and registry the category walks. Both are
    /// declarations of a native contract, so both are audited as one — otherwise the manifest would
    /// stop covering the collector precisely as the collector grew to span every category the game
    /// ships.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The accessor-binding form is matched on the method name and its type argument rather than on
    /// the receiver, so renaming the local a binder binds through cannot quietly drop a file out of
    /// the audit.
    /// </para>
    /// <para>
    /// The enum-field forms need their own alternative because they carry no type argument to match
    /// on: the binder returns the raw <c>int</c> rather than a mirrored enum, so <c>EnumField</c> and
    /// <c>NestedEnumField</c> are called bare. Requiring a type argument silently excluded every
    /// instance-form enum selector in the collector — ten files' worth, including every plot phase
    /// and modifier type.
    /// </para>
    /// <para>
    /// <c>ModifierRecord</c> is bare for the same reason and matters more than most: it is how every
    /// derived number in the game is now read, so a pattern that did not list it would drop a
    /// hundred and fifty native member names out of the audit in one commit.
    /// </para>
    /// </remarks>
    private static readonly Regex BinderTargetPattern = new(
        "(?:\\.(?:Call|Field|NestedField|EnumField)<[^<>()]*>\\s*\\([^;]*?\\)"
            + "|\\.(?:EnumField|NestedEnumField)\\s*\\([^;]*?\\)"
            + "|\\.(?:CollectionCount|NestedCollectionCount|ReferenceGuid|CallReferenceGuid"
            + "|ModifierRecord)\\s*\\([^;]*?\\)"
            + "|NativeAccessorBinder\\.(?:Call|CallWithConstructedLongArgument|Field|NestedField|EnumField|NestedEnumField|StaticList"
            + "|StaticDictionary|CollectionCount|CollectionField|CollectionElementType|NestedCollectionCount|ReferenceGuid|ModifierRecord)"
            + "(?:<[^<>()]*>)?\\s*\\([^;]*?\\)"
            + "|(?:TypeName|RegistryMember)\\s*=>\\s*\"[^\"\\r\\n]*\")",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The game type each category walks, as its binder declares it.</summary>
    /// <summary>The namespace a source file declares, which is how a capture root finds it.</summary>
    private static readonly Regex NamespacePattern = new(
        "^\\s*namespace\\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

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

    private static readonly Regex BareLiteralPattern = new(
        "\"(?<target>[A-Za-z_][A-Za-z0-9_.+`]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
        var repositoryRoot = RepositoryPaths.RequireRoot();

        Assert.Equal(5, manifest.SchemaVersion);

        // Reconciled against the file, never pinned to a number. A literal is a second place to
        // remember when a contract lands, and it silently drifted for nine of them; what has to
        // hold is that every declaration written into the manifest survives into the loaded model.
        Assert.Equal(DeclaredContractCount(), manifest.Contracts.Count);
        Assert.Equal(10, manifest.SourceAudit.Exemptions.Count);
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
        var repositoryRoot = RepositoryPaths.RequireRoot();
        var roots = manifest.SourceAudit.Roots
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var directories = Directory
            .EnumerateDirectories(Path.Combine(repositoryRoot, "src"))
            .Where(directory =>
            {
                var name = Path.GetFileName(directory)!;
                return !name.StartsWith("bin", StringComparison.Ordinal) &&
                       !name.StartsWith("obj", StringComparison.Ordinal) &&
                       ContainsAuditableSource(directory);
            })
            .Select(directory => NormalizePath(Path.GetRelativePath(repositoryRoot, directory)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(directories);
        Assert.All(directories, directory =>
            Assert.True(
                roots.Contains(directory),
                $"Source directory is not a native-contract audit root, so reflection in it is never "
                    + $"checked: {directory}"));
    }

    private static bool ContainsAuditableSource(string directory)
    {
        if (Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly).Any())
            return true;

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child)!;
            if (name.StartsWith("bin", StringComparison.Ordinal) ||
                name.StartsWith("obj", StringComparison.Ordinal))
                continue;
            if (ContainsAuditableSource(child)) return true;
        }

        return false;
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
    /// Every native type and member the suite names in a reflection or Harmony call is declared by
    /// some contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The check is on the native shape, not on which of our files touches it. It used to be both:
    /// every contract carried a hand-maintained <c>sources[]</c> path list and a literal counted as
    /// declared only against contracts naming that same file. That coupled an audit of the game's
    /// surface to our own folder layout — moving a file broke it while the native surface was
    /// unchanged, and a new file reflecting on an already-declared member meant hand-editing entries
    /// spread across the manifest with no compiler help. The scoping bought nothing the global check
    /// does not: an undeclared native target still fails here.
    /// </para>
    /// <para>
    /// Deriving the list instead of dropping it was considered and rejected — a field computed from
    /// the extracted literals cannot then gate those same literals.
    /// </para>
    /// </remarks>
    [Fact]
    public void ActiveNativeReflectionAndHarmonyTargets_AreDeclared()
    {
        var manifest = NativeContractManifest.Load();
        var repositoryRoot = RepositoryPaths.RequireRoot();
        var exemptions = manifest.SourceAudit.Exemptions.ToDictionary(
            exemption => NormalizePath(exemption.Path),
            exemption => exemption,
            StringComparer.OrdinalIgnoreCase);
        Assert.All(exemptions.Values, exemption => Assert.False(string.IsNullOrWhiteSpace(exemption.Reason)));

        // A mirrored contract declares a member the suite deliberately does not touch, so it must
        // not widen what the source audit accepts. Counting it would let a literal selector pass
        // against a row that says nobody selects anything — the audit would answer for a
        // dependency of the wrong kind.
        var declaredTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contract in manifest.Contracts.Where(IsTouched))
        {
            declaredTargets.Add(contract.Type);
            if (contract.Member is not null)
            {
                declaredTargets.Add(contract.Member);
            }

            foreach (var token in contract.SourceTokens)
            {
                declaredTargets.Add(token);
            }
        }

        var failures = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in manifest.SourceAudit.Roots)
        {
            var absoluteRoot = Path.Combine(repositoryRoot, root.Replace('/', Path.DirectorySeparatorChar));
            foreach (var sourcePath in Directory.EnumerateFiles(absoluteRoot, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = NormalizePath(Path.GetRelativePath(repositoryRoot, sourcePath));
                if (!visited.Add(relativePath))
                {
                    continue;
                }

                var source = File.ReadAllText(sourcePath);
                if (!ReflectionUsePattern.IsMatch(source))
                {
                    continue;
                }

                if (exemptions.ContainsKey(relativePath))
                {
                    continue;
                }

                foreach (var candidate in FindLiteralTargets(source).Distinct(StringComparer.Ordinal))
                {
                    if (!declaredTargets.Contains(candidate))
                    {
                        failures.Add($"{relativePath}: native target literal '{candidate}' is not declared by any manifest contract");
                    }
                }
            }
        }

        foreach (var exemption in exemptions.Values)
        {
            var sourcePath = Path.Combine(repositoryRoot, exemption.Path.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(sourcePath), $"Source-audit exemption no longer exists: {exemption.Path}");
            Assert.Matches(ReflectionUsePattern, File.ReadAllText(sourcePath));
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// A <c>mirrored</c> contract names a member no audited source selects.
    /// </summary>
    /// <remarks>
    /// That is the whole content of the word: the suite copies what the member holds without ever
    /// reaching for it. The day someone does reach for it the dependency changed kind — the row
    /// has to say <c>reflection</c> and start widening the source audit again — and this is what
    /// says so, rather than leaving a row that quietly under-describes a live selector.
    /// </remarks>
    [Fact]
    public void EveryMirroredContractNamesAMemberNoSourceSelects()
    {
        var manifest = NativeContractManifest.Load();
        var repositoryRoot = RepositoryPaths.RequireRoot();
        var mirrored = manifest.Contracts.Where(contract => !IsTouched(contract)).ToArray();
        Assert.NotEmpty(mirrored);

        var forbidden = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contract in mirrored)
        {
            if (contract.Member is not null) forbidden.Add(contract.Member);
        }
        foreach (var contract in manifest.Contracts.Where(IsTouched))
        {
            if (contract.Member is not null) forbidden.Remove(contract.Member);
            foreach (var token in contract.SourceTokens) forbidden.Remove(token);
        }

        var failures = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in manifest.SourceAudit.Roots)
        {
            var absoluteRoot = Path.Combine(repositoryRoot, root.Replace('/', Path.DirectorySeparatorChar));
            foreach (var sourcePath in Directory.EnumerateFiles(absoluteRoot, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = NormalizePath(Path.GetRelativePath(repositoryRoot, sourcePath));
                if (!visited.Add(relativePath)) continue;

                var source = File.ReadAllText(sourcePath);
                if (!ReflectionUsePattern.IsMatch(source)) continue;

                foreach (var candidate in FindLiteralTargets(source).Distinct(StringComparer.Ordinal))
                {
                    if (forbidden.Contains(candidate))
                    {
                        failures.Add(
                            $"{relativePath}: selects '{candidate}', which the manifest declares as "
                                + "mirrored — a member nothing touches. Change that contract's usage "
                                + "to the one that describes the selector.");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static bool IsTouched(NativeContractEntry contract) =>
        contract.Usages.Any(usage => !string.Equals(usage, "mirrored", StringComparison.Ordinal));

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
        var repositoryRoot = RepositoryPaths.RequireRoot();
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
        var repositoryRoot = RepositoryPaths.RequireRoot();

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
        Assert.Equal(34, collected.Length);

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

    /// <summary>The capture classes a contract may declare.</summary>
    private static readonly string[] CaptureClasses =
        { "grab", "computes", "composite", "enumerating" };

    /// <summary>How often a capture touch happens.</summary>
    private static readonly string[] CaptureCadences =
        { "per-pass", "per-epoch", "request-time" };

    /// <summary>
    /// Capture touches that write while answering, on the pass that runs four times a second.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every entry is a defect, not a design.</b> Each is a reading the collector takes by making
    /// the game recalculate and cache something, which is a mutation of the state the snapshot claims
    /// to observe. Each is derivable — the manifest says so on the row — and each leaves when the
    /// replacing suite math has a parity pass proving it, never before.
    /// </para>
    /// <para>
    /// Pinned as an exact set rather than a ceiling: an arrival fails here, and so does a departure
    /// that forgets to strike its line. The list grew from twelve to nineteen when the census was
    /// taken by call site rather than by member name, and to twenty when a row that had been
    /// classified from its signature had its body read — the same <c>HasEnough()</c> was reached from
    /// six more readers than the manifest had rows for, because a member-name reconciliation cannot
    /// see a second site. Growth of that kind is the census improving; the rule the doctrine actually
    /// wants is this list being empty.
    /// </para>
    /// <para>
    /// Ten are one family. <c>Prerequisites.Container.Check()</c> latches <c>available</c>, and every
    /// whole-entity availability or visibility predicate reaches it: <c>StructureSO</c>,
    /// <c>UpgradeSO</c>, <c>ViewSO</c>, <c>RecipeBookSO</c>, <c>CraftingRecipeSO</c>, and
    /// <c>GlyphSO</c> call it directly, <c>ResearchSO</c> through two visibility containers,
    /// <c>RitualSO.HasMetUsageRequirements()</c> through its own <c>usageRequirements</c>, and
    /// <c>DiscoveryTreeSO.IsVisible()</c> through <c>viewLocation.All(view =&gt; view.IsAvailable())</c>.
    /// They leave together, when the whole-entity container is published and the evaluator answers
    /// for it. The ritual row was the one that had been classified from its signature alone and read
    /// <c>sideEffects: []</c> until its body was audited.
    /// </para>
    /// <para>
    /// Nine more are <c>ResourceCostList.HasEnough()</c>, one per reader that asks it, plus
    /// <c>Spell.HasEnoughResources()</c> which is a one-line call to it. All reach
    /// <c>ValueModifierRecord</c>'s memo through <c>ResourceSO.GetTrueSpend</c>. This family is the
    /// one whose replacement is already proven — <c>WorldResourceCoordinate.HasAmount</c>, compared
    /// against this very call by the spell-level affordability pass — so what these rows are waiting
    /// on is each reader publishing its cost rows for a deriver, not more evidence.
    /// </para>
    /// <para>
    /// The last is the enchantment-target sweep, which is the same latch reached transitively and at
    /// the widest scope: <c>GetRandomList</c> filters the entire structure registry through
    /// <c>StructureSO.IsVisible()</c>, once per enchantment role, per pass. Nothing in the sweep
    /// itself is random or cached — the name is the game's, not a description — so it leaves with the
    /// availability family rather than needing an answer of its own.
    /// </para>
    /// </remarks>
    private static readonly string[] PerPassCaptureWrites =
    {
        "consumable.cost-has-enough-capture",
        "crafting-decision.cost-has-enough-capture",
        "crafting-recipe.visible",
        "discovery-tree-reader.cost-has-enough",
        "discovery-tree-reader.is-visible",
        "generic-discovery.cost-enough-capture",
        "generic-level.cost-has-enough-capture",
        "harvest-lifecycle.cost-has-enough-capture",
        "recipe-book.is-available",
        "research.cost-has-enough-capture",
        "research.is-available",
        "research.is-visible",
        "ritual-lifecycle.cost-has-enough-capture",
        "ritual-lifecycle.ritual-usage-requirements-capture",
        "scribe-relations.target-structure.get-random-list",
        "spell-composition.glyph-is-available-capture",
        "spell.has-enough-resources",
        "structure.is-available",
        "upgrade.is-available",
        "view.is-available",
    };

    /// <summary>
    /// Members a capture-root file selects whose only contract is an action.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>place</c> holds one value, and each of these members is genuinely read by world collection
    /// <em>and</em> used by a GameAction — their <c>owners</c> lists say so. The contract is filed
    /// under the transaction because that is the boundary with the stronger obligations, which
    /// leaves the capture-root walk seeing a selector with no capture row behind it.
    /// </para>
    /// <para>
    /// Pinned rather than waved through: this is what a dual-place binding costs today, and the fix
    /// is a schema that lets one contract name both places rather than a longer list here. The list
    /// is reconciled as an exact set, so a new arrival fails until someone decides which it is, and
    /// a name that stops being selected dual-place fails until it is deleted from here. A list that
    /// only forgives is a list that grows quietly.
    /// </para>
    /// </remarks>
    private static readonly string[] CaptureRootActionSelectors =
    {
        "AllResourcesVisible", "CanAddInstance", "CanApplyBonusLevels", "CanCastASpell",
        "CanLevel", "Check", "GetFreeBonusLevelsLeft", "GetFreeLevels", "GetFreeUsageSlots",
        "GetMaxLevel", "GetMaxSelectedLevel", "GetMaxTypeSlots", "GetMaximumInstances",
        "GetMinSelectedLevel", "GetQueuedLevels", "GetRemainingFreeUsageSlots",
        "GetRemainingMaxUsageSlots", "GetTooltipable", "HasMaxLevel", "IsActive", "IsAtMax",
        "IsLoaded", "MaximumCostTimes", "MaximumNumberInstances", "ingredientLists",
    };

    /// <summary>
    /// A row the suite never touches sits at <c>mirrored</c>, and only such a row does.
    /// </summary>
    /// <remarks>
    /// The census of what capture costs the game is the artifact this whole discipline exists to
    /// make trustworthy, and twenty-six rows nothing reflects on, patches, or calls were filed at
    /// <c>capture</c> — so each declared a per-pass reading, with generated prose asserting a touch
    /// that never happens. The place a row sits in is now decided by its usages rather than left to
    /// whoever wrote it: mirrored-only means mirrored, and any touching usage means a real place.
    /// </remarks>
    [Fact]
    public void OnlyAnUntouchedContractSitsInTheMirroredPlace()
    {
        var manifest = NativeContractManifest.Load();
        var failures = new List<string>();

        foreach (var contract in manifest.Contracts)
        {
            var untouched = contract.Usages.Count == 1 && contract.Usages[0] == "mirrored";
            if (untouched && contract.Place != "mirrored")
            {
                failures.Add(
                    $"{contract.Id}: nothing touches this member, so it belongs at place 'mirrored' "
                        + $"rather than '{contract.Place}'");
            }
            else if (!untouched && contract.Place == "mirrored")
            {
                failures.Add(
                    $"{contract.Id}: place 'mirrored' claims nothing touches this member, but it "
                        + $"declares {string.Join(", ", contract.Usages)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Every capture contract says what it makes the game do, how often, and on what evidence.
    /// </summary>
    /// <remarks>
    /// The manifest proved shape and nothing else, so a per-pass sweep over the whole registry and a
    /// single field load were indistinguishable in it — which is how both came to live in capture
    /// unremarked. These six answers are what tells them apart, and they are required rather than
    /// optional so that a new capture contract cannot arrive without someone having thought about
    /// its cost.
    /// </remarks>
    [Fact]
    public void EveryCaptureContractDeclaresWhatItCostsTheGame()
    {
        var manifest = NativeContractManifest.Load();
        var failures = new List<string>();

        foreach (var contract in manifest.Contracts)
        {
            if (contract.Place != "capture")
            {
                if (contract.Capture is not null)
                {
                    failures.Add($"{contract.Id}: only a capture contract may declare capture discipline");
                }
                continue;
            }

            var capture = contract.Capture;
            if (capture is null)
            {
                failures.Add($"{contract.Id}: declares no capture discipline");
                continue;
            }

            if (!CaptureClasses.Contains(capture.Class))
                failures.Add($"{contract.Id}: capture class '{capture.Class}' is not one of the four");
            if (!CaptureCadences.Contains(capture.Cadence))
                failures.Add($"{contract.Id}: capture cadence '{capture.Cadence}' is not one of the three");
            if (string.IsNullOrWhiteSpace(capture.Justification))
                failures.Add($"{contract.Id}: capture justification is empty");
            if (string.IsNullOrWhiteSpace(capture.Evidence))
                failures.Add($"{contract.Id}: capture evidence is empty");
            foreach (var effect in capture.SideEffects)
            {
                if (string.IsNullOrWhiteSpace(effect))
                    failures.Add($"{contract.Id}: a side effect is named by an empty string");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Nothing writes on the pass that runs four times a second, except what is written down.
    /// </summary>
    /// <remarks>
    /// This is the one rule the capture manifest exists to make enforceable. See
    /// <see cref="PerPassCaptureWrites"/> for why the list is not yet empty and what empties it.
    /// </remarks>
    [Fact]
    public void NoPerPassCaptureWritesExceptTheOnesStillOwed()
    {
        var manifest = NativeContractManifest.Load();

        var writing = manifest.Contracts
            .Where(contract => contract.Place == "capture" &&
                contract.Capture?.Cadence == "per-pass" &&
                contract.Capture.SideEffects.Count > 0)
            .Select(contract => contract.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(PerPassCaptureWrites.OrderBy(id => id, StringComparer.Ordinal), writing);

        // A write the suite cannot yet replace is debt, and debt that is not derivable is a
        // permanent exception nobody signed off on.
        Assert.All(
            manifest.Contracts.Where(contract => writing.Contains(contract.Id)),
            contract => Assert.True(
                contract.Capture!.Derivable,
                $"{contract.Id}: writes on a capture pass and claims it cannot be derived"));
    }

    /// <summary>
    /// The readers the manifest calls epoch-scoped are the ones the collector runs that way.
    /// </summary>
    /// <remarks>
    /// A cadence nobody reconciles is a wish. The collector marks its structural readers by object
    /// identity at construction so that reordering the array cannot reclassify one; this reads that
    /// same marking out of its source and holds the manifest to it. The marking is on the fields, so
    /// the fields are what the manifest names — one of the nine is a closed generic shared with
    /// several per-pass categories, and its type name would say nothing about which reader was meant.
    /// </remarks>
    [Fact]
    public void TheManifestsEpochScopedReadersAreTheCollectorsOwn()
    {
        var manifest = NativeContractManifest.Load();
        var collector = Path.Combine(
            RepositoryPaths.RequireRoot(),
            "src", "Common", "Runtime", "World", "GameWorldCollector.cs");
        Assert.True(File.Exists(collector), $"The collector was not found at {collector}");

        var marked = Regex
            .Matches(
                File.ReadAllText(collector),
                @"ReferenceEquals\(_readers\[index\], (?<field>_[A-Za-z0-9_]+)\)")
            .Select(match => match.Groups["field"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(marked);
        Assert.Equal(
            manifest.CaptureStructuralReaders.OrderBy(name => name, StringComparer.Ordinal),
            marked);
    }

    /// <summary>
    /// Every native member a capture root selects is declared as a capture contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The union is on the member name rather than on the exact contract, because one literal in one
    /// binder call can be satisfied by any contract naming that member — the walk reads source text
    /// and cannot resolve which overload on which type a call meant. That is coarse on purpose for a
    /// first cut: it catches a capture root reaching for something the manifest files as a
    /// transaction, which is the failure that matters.
    /// </para>
    /// <para>
    /// <b>It under-reports, and knowing by how much is the point of saying so here.</b> The walk sees
    /// the reflection APIs and the world binder's own helpers; it does not see a feature binding's
    /// private <c>Method(...)</c> and <c>Field(...)</c> wrappers, so
    /// <c>LoadoutNativeBindings</c> — which sits inside a capture root and binds a transaction —
    /// passes without being looked at. Splitting that file into its reading and mutating halves is
    /// what lets the walk see it, and until then it is a named gap rather than a silent one.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryCaptureRootSelectorNamesACaptureContract()
    {
        var manifest = NativeContractManifest.Load();
        var repositoryRoot = RepositoryPaths.RequireRoot();
        Assert.NotEmpty(manifest.CaptureRoots);

        var capturing = manifest.Contracts
            .Where(contract => contract.Place == "capture")
            .SelectMany(contract => contract.Kind == "type"
                ? new[] { contract.Type }
                : new[] { contract.Type, contract.Member! })
            .ToHashSet(StringComparer.Ordinal);
        var declared = manifest.Contracts
            .SelectMany(contract => contract.Kind == "type"
                ? new[] { contract.Type }
                : new[] { contract.Type, contract.Member! })
            .ToHashSet(StringComparer.Ordinal);
        var allowed = CaptureRootActionSelectors.ToHashSet(StringComparer.Ordinal);
        var selectedDualPlace = new HashSet<string>(StringComparer.Ordinal);

        var failures = new List<string>();
        var walked = 0;
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(repositoryRoot, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            var declaration = NamespacePattern.Match(source);
            if (!declaration.Success) continue;

            var space = declaration.Groups["name"].Value;
            if (!manifest.CaptureRoots.Any(root =>
                    space == root || space.StartsWith(root + ".", StringComparison.Ordinal)))
            {
                continue;
            }

            walked++;
            foreach (var literal in FindLiteralTargets(source).Distinct(StringComparer.Ordinal))
            {
                if (capturing.Contains(literal)) continue;
                if (!declared.Contains(literal)) continue;
                if (allowed.Contains(literal))
                {
                    selectedDualPlace.Add(literal);
                    continue;
                }

                failures.Add(
                    $"{NormalizePath(Path.GetRelativePath(repositoryRoot, file))}: " +
                    $"'{literal}' is selected by a capture root but no capture contract names it");
            }
        }

        // A namespace that moved would walk nothing and report nothing, which reads exactly like a
        // clean sweep. The count is the difference.
        Assert.True(walked > 50, $"Only {walked} files were walked for capture roots.");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

        // Documented as shrink-only, so it is reconciled as an exact set: a name whose dual-place
        // selection is gone has to leave the list, or the list quietly outlives what it forgives.
        Assert.Equal(
            CaptureRootActionSelectors.OrderBy(name => name, StringComparer.Ordinal),
            selectedDualPlace.OrderBy(name => name, StringComparer.Ordinal));
    }

    private static IEnumerable<string> FindLiteralTargets(string source)
    {
        foreach (Match match in QualifiedTargetPattern.Matches(source))
        {
            yield return match.Groups["type"].Value;
            yield return match.Groups["member"].Value;
        }

        foreach (Match match in TypeResolverPattern.Matches(source))
        {
            yield return match.Groups["type"].Value;
        }

        foreach (Match match in LiteralSelectorPattern.Matches(source))
        {
            yield return match.Groups["target"].Value;
        }

        foreach (Match match in BinderTargetPattern.Matches(source))
        {
            // One binder call can name two members — a field and a field inside it — so every literal
            // in the call is a target, not just the first.
            foreach (Match literal in BareLiteralPattern.Matches(match.Value))
            {
                yield return literal.Groups["target"].Value;
            }
        }

        if (source.Contains("[HarmonyPatch]", StringComparison.Ordinal))
        {
            foreach (Match match in Regex.Matches(source, "\"(?<target>[A-Za-z_][A-Za-z0-9_.+`]*)\""))
            {
                yield return match.Groups["target"].Value;
            }
        }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    /// <summary>
    /// How many contracts the checked-in manifest declares, read straight from the file with a
    /// second parser rather than through the typed model the assertion is about.
    /// </summary>
    private static int DeclaredContractCount()
    {
        var path = Path.Combine(
            RepositoryPaths.RequireRoot(), "data", "native-contracts.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var declared = document.RootElement.GetProperty("contracts").GetArrayLength();
        Assert.True(declared > 0, $"The manifest at {path} declares no contracts.");
        return declared;
    }
}
