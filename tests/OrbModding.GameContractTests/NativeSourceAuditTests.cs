using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OrbModding.TestSupport;
using Xunit;
using static OrbModding.GameContractTests.NativeSourceAudit;

namespace OrbModding.GameContractTests;

public sealed class NativeSourceAuditTests
{
    private static readonly Regex ReflectionUsePattern = new(
        "(?:HarmonyPatch|AccessTools\\.(?:Method|TypeByName)|ReflectionUtil\\.FindLoadedType"
            + "|NativeAccessorBinder\\.|NativeBindingSet\\.|WorldMemberBinding"
            + "|\\.(?:GetMethod|GetMethods|GetField|GetFields|GetProperty|GetProperties)\\s*\\("
            + "|\\b(?:FindMethod|FindField)\\s*\\()",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Binder members that take a string and select nothing with it.
    /// </summary>
    /// <remarks>
    /// Each of these strings is a diagnostic label. <c>Elements</c> is handed an already-resolved
    /// element <see cref="Type"/> and a name it only ever prints in a failure message, so
    /// <c>Elements(infoType, "PlotNodePhaseInfo")</c> reflects on nothing — auditing it would make
    /// the manifest answer for five names the suite never selects. The binding constructor's
    /// <c>typeName</c> and the failure list's <c>Add</c> are the same thing: text that says which
    /// member did not bind.
    /// </remarks>
    private static readonly string[] BinderDiagnosticNames =
        { "Add", "Elements", "NativeBindingException", "TryBind", "WorldMemberBinding" };

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
        var repositoryRoot = RepositorySources.Root;
        var exemptions = manifest.SourceAudit.Exemptions.ToDictionary(
            exemption => NormalizePath(exemption.Path),
            exemption => exemption,
            StringComparer.OrdinalIgnoreCase);
        Assert.All(exemptions.Values, exemption => Assert.False(string.IsNullOrWhiteSpace(exemption.Reason)));

        var declaredTargets = DeclaredTargets(manifest);
        var owed = UndeclaredBinderLiterals.ToHashSet(StringComparer.Ordinal);
        Assert.Equal(UndeclaredBinderLiterals.Length, owed.Count);
        var owedSeen = new HashSet<string>(StringComparer.Ordinal);

        var failures = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in manifest.SourceAudit.Roots)
        {
            foreach (var sourcePath in RepositorySources.SourceFiles("*.cs", root))
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

                foreach (var candidate in UndeclaredTargets(source, declaredTargets))
                {
                    if (owed.Contains(candidate))
                    {
                        owedSeen.Add(candidate);
                        continue;
                    }

                    failures.Add($"{relativePath}: native target literal '{candidate}' is not declared by any manifest contract");
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

        // Reconciled as an exact set, so a literal whose contract has landed has to leave the list
        // rather than sit there forgiving nothing.
        Assert.Equal(
            UndeclaredBinderLiterals.OrderBy(name => name, StringComparer.Ordinal),
            owedSeen.OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// Every native name a manifest contract answers for, from the contracts that touch it.
    /// </summary>
    /// <remarks>
    /// A mirrored contract declares a member the suite deliberately does not touch, so it must not
    /// widen what the source audit accepts. Counting it would let a literal selector pass against a
    /// row that says nobody selects anything — the audit would answer for a dependency of the wrong
    /// kind. Every contract id is accepted: a binding set names the id beside each lookup, and an id
    /// such as <c>consumable.quantity</c> is spelled like a member without being one.
    /// </remarks>
    private static HashSet<string> DeclaredTargets(NativeContractManifest manifest)
    {
        var declaredTargets = new HashSet<string>(
            manifest.Contracts.Select(contract => contract.Id), StringComparer.Ordinal);
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

        return declaredTargets;
    }

    /// <summary>
    /// The native names one source file selects that no contract answers for.
    /// </summary>
    /// <remarks>
    /// The whole failure mode the source audit exists for, in one expression, so that the walk over
    /// the repository and the shape-by-shape proof in
    /// <see cref="AnUndeclaredLiteralOfEveryBinderShapeIsCaught"/> exercise the same mechanism rather
    /// than two that can drift.
    /// </remarks>
    private static IEnumerable<string> UndeclaredTargets(string source, ISet<string> declaredTargets) =>
        FindLiteralTargets(source)
            .Distinct(StringComparer.Ordinal)
            .Where(candidate => !declaredTargets.Contains(candidate));

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
        var repositoryRoot = RepositorySources.Root;
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
            foreach (var sourcePath in RepositorySources.SourceFiles("*.cs", root))
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
    /// Native members a binder selects that no contract declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty, and every entry that was here is now a manifest row read from the audited assembly
    /// rather than from its declared sibling: <c>PersistentResetManager.challengeRerollsMax</c> is
    /// an <c>IntVariable</c> like <c>challengeRerollsLeft</c>, but
    /// <c>AlchemyManager.allAlchemy</c> is an <c>AlchemyRecipeListVariable</c> where its neighbour
    /// <c>activeAlchemy</c> is an <c>AlchemyInstanceListVariable</c> — inheriting the sibling's type
    /// would have shipped a contract that describes no member the game has.
    /// </para>
    /// <para>
    /// An entry only ever belongs here while a contract row is still owed, because a row is a claim
    /// about the shipped assembly's member shape that only an audit against the game may make. The
    /// list stays reconciled as an exact set, so an undeclared selector fails this test the day it
    /// arrives instead of being absorbed by a list that only forgives.
    /// </para>
    /// </remarks>
    private static readonly string[] UndeclaredBinderLiterals = Array.Empty<string>();

    /// <summary>A binder member declared with a string parameter, however the signature wraps.</summary>
    private static readonly Regex BinderStringMemberPattern = new(
        "\\binternal\\s+(?:static\\s+)?[^;(){}\\r\\n]*?\\b(?<name>[A-Za-z_][A-Za-z0-9_]*)"
            + "(?:<[^<>()]*>)?\\s*\\((?<parameters>[^()]*)\\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StringParameterPattern = new(
        "\\bstring\\??\\s+[A-Za-z_]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Every binder member that takes a string is either a selector the audit reads or a diagnostic
    /// the audit knows to ignore.
    /// </summary>
    /// <remarks>
    /// This is the check that would have caught the gap it was written for. The auditor knew five
    /// call shapes while the binder offered twenty-four selectors, and nothing said so: an instance
    /// <c>CollectionField</c> or <c>Through</c> simply produced no literal, and no literal reads
    /// exactly like nothing to declare. Reading the binder's own signatures rather than a list
    /// somebody maintains means the next selector added to it fails here until the pattern learns
    /// it — and a name that is deliberately not a selector has to say why, in
    /// <see cref="BinderDiagnosticNames"/>, rather than being quietly absent.
    /// </remarks>
    [Fact]
    public void EveryStringTakingBinderMemberIsClassified()
    {
        var repositoryRoot = RepositorySources.Root;
        var binderSources = new[]
        {
            Path.Combine(repositoryRoot, "src", "Common", "Runtime", "World", "NativeAccessorBinder.cs"),
            Path.Combine(repositoryRoot, "src", "Common", "Runtime", "World", "WorldCategoryMachinery.cs"),
            Path.Combine(repositoryRoot, "src", "Common", "NativeBindingSet.cs"),
        };

        var offered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in binderSources)
        {
            Assert.True(File.Exists(path), $"Binder source not found, so nothing was checked: {path}");
            foreach (Match match in BinderStringMemberPattern.Matches(File.ReadAllText(path)))
            {
                if (StringParameterPattern.IsMatch(match.Groups["parameters"].Value))
                {
                    offered.Add(match.Groups["name"].Value);
                }
            }
        }

        var classified = BinderSelectorNames.Split('|').Concat(BinderDiagnosticNames);
        Assert.Equal(
            classified.OrderBy(name => name, StringComparer.Ordinal),
            offered.OrderBy(name => name, StringComparer.Ordinal));

        // The proof table below answers for the pattern one selector at a time, so a selector the
        // pattern names but nothing proves is a selector nobody has shown the audit catches.
        Assert.Equal(
            BinderSelectorNames.Split('|').OrderBy(name => name, StringComparer.Ordinal),
            BinderShapes
                .Select(shape => shape.Member)
                .Where(member => member.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// Every binder call shape in use, the source it is written as, and the native names the audit
    /// must take out of it.
    /// </summary>
    /// <remarks>
    /// The census of what the binders actually write, kept as data the auditor is run against rather
    /// than as a note somebody took once. Each row's literals are deliberate nonsense, so a row
    /// passing means that shape carried the name out — not that some other pattern in
    /// <see cref="FindLiteralTargets"/> happened to know it. The form and member columns are what
    /// name the failing case when one fails.
    /// </remarks>
    private static readonly (string Form, string Member, string Source, string[] Expected)[] BinderShapes =
    {
        ("instance", "Call", "_id = bind.Call<Guid>(\"ZzProbeCall\");", new[] { "ZzProbeCall" }),
        ("instance", "CallList", "_rows = bind.CallList(\"ZzProbeCall\", tupleType);", new[] { "ZzProbeCall" }),
        ("instance", "CallObject", "_cost = bind.CallObject(\"ZzProbeCall\", costType);", new[] { "ZzProbeCall" }),
        ("instance", "CallObject", "_total = bind.CallObject<BigDouble, BigDouble>(\"ZzProbeCall\", costType);", new[] { "ZzProbeCall" }),
        ("instance", "CallReferenceGuid", "_owner = bind.CallReferenceGuid(\"ZzProbeCall\");", new[] { "ZzProbeCall" }),
        ("instance", "CallWithObjectArgument", "_amount = bind.CallWithObjectArgument<BigDouble>(\"ZzProbeCall\", recipeType);", new[] { "ZzProbeCall" }),
        ("instance", "CollectionCount", "_count = bind.CollectionCount(\"zzProbeField\");", new[] { "zzProbeField" }),
        ("instance", "CollectionElementType", "_element = bind.CollectionElementType(\"zzProbeField\");", new[] { "zzProbeField" }),
        ("instance", "CollectionField", "_values = bind.CollectionField(\"zzProbeField\");", new[] { "zzProbeField" }),
        ("instance", "CollectionField", "_values = bind.CollectionField(\"zzProbeField\", elementType);", new[] { "zzProbeField" }),
        ("instance", "EnumField", "_kind = bind.EnumField(\"zzProbeField\");", new[] { "zzProbeField" }),
        ("instance", "Field", "_level = bind.Field<int>(\"zzProbeField\");", new[] { "zzProbeField" }),
        ("instance", "ModifierRecord", "_power = bind.ModifierRecord(\"zzProbeField\");", new[] { "zzProbeField" }),
        ("instance", "NestedCollectionCount", "_count = bind.NestedCollectionCount(\"zzProbeField\", \"zzProbeNested\");", new[] { "zzProbeField", "zzProbeNested" }),
        ("instance", "NestedEnumField", "_kind = bind.NestedEnumField(\"zzProbeField\", \"zzProbeNested\");", new[] { "zzProbeField", "zzProbeNested" }),
        ("instance", "NestedField", "_adjust = bind.NestedField<BigDouble>(\"zzProbeField\", \"zzProbeNested\");", new[] { "zzProbeField", "zzProbeNested" }),
        ("instance", "Reference", "_cost = bind.Reference(\"zzProbeField\", costType);", new[] { "zzProbeField" }),
        ("instance", "ReferenceGuid", "_maximumId = bind.ReferenceGuid(\"zzProbeField\");", new[] { "zzProbeField" }),
        ("instance", "Through", "_inner = bind.Through(\"zzProbeField\");", new[] { "zzProbeField" }),
        ("instance", "StableUuid", "_id = bind.Elements(entryType, \"ZzProbeLabel\").StableUuid();", Array.Empty<string>()),

        ("set", "Type", "var owner = set.Type(\"probe.type-action\", \"ZzProbeType\");", new[] { "ZzProbeType" }),
        ("set", "GenericBase", "var list = set.GenericBase(\"probe.base-action\", owner, \"ZzProbeBase`1\");", new[] { "ZzProbeBase`1" }),
        ("set", "Call", "set.Call<Func<object, int, object?>>(\"probe.call-action\", owner, \"ZzProbeCall\", typeof(int), typeof(int)),", new[] { "ZzProbeCall" }),
        ("set", "StaticCall", "set.StaticCall<Func<bool>>(\"probe.static-action\", owner, \"ZzProbeCall\", typeof(bool)),", new[] { "ZzProbeCall" }),
        ("set", "Field", "set.Field<Func<object, object?>>(\"consumable.quantity\", owner, \"zzProbeField\", valueType),", new[] { "zzProbeField" }),
        ("set", "StaticField", "set.StaticField<Func<object?>>(\"probe.instance-action\", owner, \"zzProbeField\", owner),", new[] { "zzProbeField" }),
        ("set", "Setter", "set.Setter<Action<object, int>>(\"probe.setter-action\", owner, \"zzProbeField\", typeof(int)),", new[] { "zzProbeField" }),
        ("set", "New", "set.New<Func<object, object>>(\"probe.new-action\", owner, valueType),", Array.Empty<string>()),
        ("set", "StableUuid", "set.StableUuid(\"probe.guid-action\", owner),", Array.Empty<string>()),

        ("static", "BoxedField", "_penalty = NativeAccessorBinder.BoxedField(recipeType, \"zzProbeField\");", new[] { "zzProbeField" }),
        ("static", "Call", "_id = NativeAccessorBinder.Call<Guid>(recipeType, \"ZzProbeCall\");", new[] { "ZzProbeCall" }),
        ("static", "CallList", "_all = NativeAccessorBinder.CallList(listType, \"ZzProbeCall\", stationType);", new[] { "ZzProbeCall" }),
        ("static", "CallObject", "_cost = NativeAccessorBinder.CallObject(recipeType, \"ZzProbeCall\", costType);", new[] { "ZzProbeCall" }),
        ("static", "CallObject", "_slot = NativeAccessorBinder.CallObject<int>(stationType, \"ZzProbeCall\", elementType);", new[] { "ZzProbeCall" }),
        ("static", "CallObject", "_add = NativeAccessorBinder.CallObject(costType, \"ZzProbeCall\", costType, costType);", new[] { "ZzProbeCall" }),
        ("static", "CallReferenceGuid", "_coreId = NativeAccessorBinder.CallReferenceGuid(recipeType, \"ZzProbeCall\");", new[] { "ZzProbeCall" }),
        ("static", "CallWithObjectArgument", "_restricted = NativeAccessorBinder.CallWithObjectArgument<bool>(listType, \"ZzProbeCall\", challengeType);", new[] { "ZzProbeCall" }),
        ("static", "CollectionCount", "_count = NativeAccessorBinder.CollectionCount(recordType, \"zzProbeField\");", new[] { "zzProbeField" }),
        ("static", "CollectionElementType", "_element = NativeAccessorBinder.CollectionElementType(tooltipType, \"zzProbeField\");", new[] { "zzProbeField" }),
        ("static", "CollectionField", "_values = NativeAccessorBinder.CollectionField(tooltipType, \"zzProbeField\");", new[] { "zzProbeField" }),
        ("static", "CollectionField", "_values = NativeAccessorBinder.CollectionField(listType, \"zzProbeField\", elementType);", new[] { "zzProbeField" }),
        ("static", "EnumField", "_kind = NativeAccessorBinder.EnumField(modifierType, \"zzProbeField\");", new[] { "zzProbeField" }),
        ("static", "Field", "_available = NativeAccessorBinder.Field<bool>(prerequisitesType, \"zzProbeField\");", new[] { "zzProbeField" }),
        ("static", "NestedEnumField", "_kind = NativeAccessorBinder.NestedEnumField(thresholdType, \"zzProbeField\", \"zzProbeNested\");", new[] { "zzProbeField", "zzProbeNested" }),
        ("static", "NestedField", "_adjust = NativeAccessorBinder.NestedField<BigDouble>(thresholdType, \"zzProbeField\", \"zzProbeNested\");", new[] { "zzProbeField", "zzProbeNested" }),
        ("static", "Reference", "_recipes = NativeAccessorBinder.Reference(managerType, \"zzProbeField\");", new[] { "zzProbeField" }),
        ("static", "ReferenceGuid", "_resourceId = NativeAccessorBinder.ReferenceGuid(entryType, \"zzProbeField\");", new[] { "zzProbeField" }),
        ("static", "StaticCall", "_ready = NativeAccessorBinder.StaticCall<bool>(managerType, \"ZzProbeCall\");", new[] { "ZzProbeCall" }),
        ("static", "StaticCallObject", "_target = NativeAccessorBinder.StaticCallObject(managerType, \"ZzProbeCall\", targetType);", new[] { "ZzProbeCall" }),
        ("static", "StaticDictionaryAccessor", "_lookup = NativeAccessorBinder.StaticDictionaryAccessor(registryType, \"zzProbeField\");", new[] { "zzProbeField" }),
        ("static", "StaticField", "_frame = NativeAccessorBinder.StaticField<long>(managerType, \"zzProbeField\");", new[] { "zzProbeField" }),
        ("static", "StaticListAccessor", "_registry = NativeAccessorBinder.StaticListAccessor(challengeType, \"zzProbeField\");", new[] { "zzProbeField" }),
        ("static", "StaticReference", "_manager = NativeAccessorBinder.StaticReference(managerType, \"zzProbeField\");", new[] { "zzProbeField" }),
        ("static", "StaticReference", "_globals = NativeAccessorBinder.StaticReference(globalsType, \"zzProbeField\", globalsType);", new[] { "zzProbeField" }),

        // A selector built out of another selector: the outer call's own name sits past the inner
        // call's closing parenthesis, which is exactly where the audit used to stop reading.
        ("composed", "Call", "_id = NativeAccessorBinder.Call<Guid>(NativeAccessorBinder.CollectionElementType(spellType, \"zzProbeField\"), \"ZzProbeCall\");", new[] { "ZzProbeCall", "zzProbeField" }),

        ("override", "", "internal override string TypeName => \"ZzProbeType\";", new[] { "ZzProbeType" }),
        ("override", "", "internal override string RegistryMember => \"ZzProbeRegistry\";", new[] { "ZzProbeRegistry" }),
    };

    public static TheoryData<string, string, string, string[]> BinderLiteralShapes
    {
        get
        {
            var data = new TheoryData<string, string, string, string[]>();
            foreach (var shape in BinderShapes)
            {
                data.Add(shape.Form, shape.Member, shape.Source, shape.Expected);
            }

            return data;
        }
    }

    /// <summary>
    /// A native name of any shape the binders write, with no contract behind it, is caught.
    /// </summary>
    /// <remarks>
    /// The audit's own failure mode, proved one shape at a time. Reconciling the manifest against
    /// what the binders name is worth exactly as much as the pattern that reads them, and a pattern
    /// that misses a shape reports a clean sweep of the calls it cannot see — which is why this is a
    /// proof per shape rather than one assertion about the repository.
    /// </remarks>
    [Theory]
    [MemberData(nameof(BinderLiteralShapes))]
    public void AnUndeclaredLiteralOfEveryBinderShapeIsCaught(
        string form,
        string member,
        string source,
        string[] expected)
    {
        var declaredTargets = DeclaredTargets(NativeContractManifest.Load());
        Assert.All(expected, probe => Assert.DoesNotContain(probe, declaredTargets));

        var caught = UndeclaredTargets(source, declaredTargets)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            caught.SequenceEqual(expected.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal),
            $"{form} {member}".Trim()
                + $": the audit should report [{string.Join(", ", expected)}] out of this shape "
                + $"and reported [{string.Join(", ", caught)}]");
    }
}
