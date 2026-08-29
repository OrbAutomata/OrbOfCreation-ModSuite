using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace OrbModding.Tests;

public sealed class ProductionSourceAuditTests
{
    [Fact]
    public void ProductionCSharpContainsNoUseGameStubsTokens()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/');
            if (relativePath.StartsWith("bin/", StringComparison.Ordinal) ||
                relativePath.StartsWith("bin-", StringComparison.Ordinal) ||
                relativePath.StartsWith("obj/", StringComparison.Ordinal) ||
                relativePath.StartsWith("obj-", StringComparison.Ordinal))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                if (line.Contains("USE_GAME_STUBS", StringComparison.Ordinal))
                {
                    offenders.Add(relativePath + ":" + lineNumber);
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Production C# source must not vary for game stubs: " + string.Join(", ", offenders));
    }

    [Fact]
    public void GameMcpProjectionsLeaveWireCodeNormalizationToTheEncoder()
    {
        var projectionRoot = Path.Combine(
            FindRepositoryRoot(), "src", "Automata", "Runtime", "GameMcp");
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(
                     projectionRoot, "*Projection.cs", SearchOption.TopDirectoryOnly))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                if (line.Contains("GameMcpEntityWireNormalizer.Snake(", StringComparison.Ordinal))
                    offenders.Add(Path.GetFileName(path) + ":" + lineNumber);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "MCP projections must publish domain vocabulary; the one wire encoder owns code normalization: " +
            string.Join(", ", offenders));
    }

    /// <summary>
    /// Nothing on the requirement path may reach the <c>Check()</c> overload that latches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Prerequisites.Container</c> ships two <c>Check</c> overloads and only one of them is a read.
    /// The no-argument one calls <c>CheckGameId()</c>, returns early on the <c>available</c> it latched
    /// before, and on success stores <c>available = true</c> — so a suite that called it while
    /// explaining a lock would unlock the thing it was asked about. The parameterised one stores
    /// nothing, which is what makes the unlock rows readable and the differential possible.
    /// </para>
    /// <para>
    /// The forbidden tokens are the ways that overload is actually reachable by reflection: resolving
    /// <c>Check</c> against <c>Type.EmptyTypes</c>, binding it through the no-argument call binder, and
    /// the three whole-entity predicates whose bodies are that call. This is scoped to the files that
    /// capture, evaluate, verify and project requirements — the entity categories do call
    /// <c>IsAvailable()</c> and <c>IsVisible()</c> on purpose, because the latched <c>available</c> is
    /// the published gate W58 designed and their manifest rows declare the latch as a side effect.
    /// Widening this sweep to them would be a ruling about W58 rather than a test.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRequirementPathNeverBindsTheCheckOverloadThatLatches()
    {
        var root = FindRepositoryRoot();
        var scoped = new[]
        {
            Path.Combine(root, "src", "Common", "Runtime", "World", "Categories", "WorldEntityRequirement.cs"),
            Path.Combine(root, "src", "Common", "Runtime", "World", "WorldRequirementEvaluator.cs"),
            Path.Combine(root, "src", "Automata", "Runtime", "Verification", "AutomataRequirementVerifier.cs"),
            Path.Combine(root, "src", "Automata", "Runtime", "GameMcp", "GameMcpEntityExplainer.cs"),
        };
        var predicates = new[] { "\"IsAvailable\"", "\"IsVisible\"", "\"IsEnabled\"" };
        var offenders = new List<string>();
        var parameterisedBindings = 0;
        foreach (var path in scoped)
        {
            Assert.True(File.Exists(path), "the requirement path moved: " + path);
            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                var name = Path.GetFileName(path) + ":" + lineNumber;
                if (line.Contains("Requirements.ConditionInfo", StringComparison.Ordinal) ||
                    line.Contains("parameters.Length == 1", StringComparison.Ordinal))
                {
                    parameterisedBindings++;
                }

                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                if (line.TrimStart().StartsWith("///", StringComparison.Ordinal)) continue;
                foreach (var predicate in predicates)
                {
                    if (line.Contains(predicate, StringComparison.Ordinal))
                        offenders.Add(name + " " + predicate);
                }

                if (!line.Contains("\"Check\"", StringComparison.Ordinal)) continue;
                if (line.Contains("EmptyTypes", StringComparison.Ordinal))
                    offenders.Add(name + " resolves Check with no parameters");
                if (line.Contains("Call<bool>(", StringComparison.Ordinal))
                    offenders.Add(name + " binds Check through the no-argument call binder");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "the no-argument Container.Check() latches `available`, so the requirement path reads " +
            "the container's rows instead and asks the parameterised overload for the game's own " +
            "verdict: " + string.Join(", ", offenders));

        // Without this the sweep would also pass if the oracle were deleted outright.
        Assert.True(
            parameterisedBindings >= 2,
            "the parameterised Check must still be how the requirement path asks the game.");
    }

    /// <summary>
    /// A requirement row on the wire says what a player must do about it. The game's own C# class
    /// names are not that, and neither is the suite's account of how it read them.
    /// </summary>
    /// <remarks>
    /// Three of these shipped as columns a caller read: <c>conditionTypeName</c> on the rows the
    /// suite could not model, <c>ownerKind</c> beside a uuid that already answers which registry the
    /// owner is in, and <c>requirementNativeType</c> on every leaf of every tree. The rest are the
    /// suite's own reading mechanics — which accessor it selected, and two of the three thresholds
    /// it folded to reach the one the screen draws. Named literals rather than a pattern, because
    /// the rule is that these particular columns do not come back, and a review that swept the sites
    /// it could see left the scan projection carrying two of them.
    /// </remarks>
    [Fact]
    public void NoRequirementColumnCarriesANativeTypeNameOrTheSuitesOwnMechanics()
    {
        var retired = new[]
        {
            "conditionTypeName", "conditionType", "ownerKind", "requirementNativeType",
            "selectedValueKind", "baseThreshold", "scaledThreshold", "nodeKind", "parentOrdinal",
            "checkLevel",
        };
        var surfaceRoot = Path.Combine(
            FindRepositoryRoot(), "src", "Automata", "Runtime", "GameMcp");
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(
                     surfaceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                foreach (var column in retired)
                {
                    if (line.Contains("\"" + column + "\"", StringComparison.Ordinal))
                        offenders.Add(Path.GetFileName(path) + ":" + lineNumber + " " + column);
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "requirement rows publish what a player needs and whether it holds, not the game's " +
            "class names or the suite's reading mechanics: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The MCP surface names entities the way a player does, everywhere and without exception.
    /// </summary>
    /// <remarks>
    /// <c>Format</c> renders the diagnostic <c>Name [AssetName] (uuid)</c> triple, which is what a
    /// log wants and what a sentence does not: the same response already carries the asset name and
    /// the UUID as fields. Sweeping the sites named in one review left seventeen more behind, so the
    /// rule is enforced over the whole surface rather than over a list somebody has to keep current.
    /// </remarks>
    [Fact]
    public void TheMcpSurfaceNamesEntitiesTheWayAPlayerDoes()
    {
        var surfaceRoot = Path.Combine(
            FindRepositoryRoot(), "src", "Automata", "Runtime", "GameMcp");
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(
                     surfaceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                if (line.Contains("EntityIdentityFormatter.Format(", StringComparison.Ordinal))
                    offenders.Add(Path.GetFileName(path) + ":" + lineNumber);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "MCP prose names entities with EntityIdentityFormatter.PlayerName; Format is the " +
            "diagnostic triple for logs: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The answers a GameAction gives when it stops short of a committed mutation are written once,
    /// in <c>GameActionAnswer</c>, and no feature composes its own.
    /// </summary>
    /// <remarks>
    /// Eighteen of the surface's mechanism-vocabulary findings were the same five sentences,
    /// authored separately in twenty-one features: thread ids, lifecycle epochs, "preflight",
    /// "observable", "resolution", and the game's raw .NET exception text, each reaching a caller as
    /// the explanation of what had happened. Nothing but a rule over the whole set stops the
    /// twenty-second feature from writing a twenty-second wording.
    /// </remarks>
    [Fact]
    public void EveryGameActionSpeaksTheOneSetOfFaultSentences()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        var mechanism = new[]
        {
            "preflight failed before mutation",
            "bound to Unity thread",
            "lifecycle is stale",
            "resolution became stale",
            "was not observable",
            "callback threw",
        };
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(
                     sourceRoot, "*GameAction.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/');
            if (relativePath.StartsWith("bin", StringComparison.Ordinal) ||
                relativePath.StartsWith("obj", StringComparison.Ordinal))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                foreach (var phrase in mechanism)
                {
                    if (line.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                        offenders.Add(relativePath + ":" + lineNumber + " (" + phrase + ")");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A GameAction wrote its own fault sentence; GameActionAnswer owns these: " +
            string.Join(", ", offenders));
    }

    /// <summary>
    /// No sentence a GameAction puts on the wire names the game's own code.
    /// </summary>
    /// <remarks>
    /// A caller told <c>SpellManager.instance is unavailable in this lifecycle</c> or
    /// <c>TargetLink.GetRandom did not return one exact StructureSO</c> can look up neither name and
    /// act on neither fact. The rule reads the prose rather than the code: a literal with a space in
    /// it is a sentence, a bare type name in a comparison is not, and an internal <c>throw</c> is an
    /// assertion no caller ever sees.
    /// </remarks>
    [Fact]
    public void NoWireSentenceInAGameActionNamesTheGamesOwnCode()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        var nativeVocabulary = new[]
        {
            "binding set", "decision graph", "persistent reset manager", "live registry",
            "SpellManager", "EquipmentManager", "ActionManager", "TargetLink", "GuidContainer",
            "DiscoveryTreeOfferLifecycle", "ResourceCostList", "ITooltipable",
            "RitualSO", "ResearchSO", "DiscoveryTreeSO", "SpellRecipeSO", "StructureSO",
            "CraftingRecipeSO", "PlotNodeSO", "EffectResultInfo",
        };
        var literal = new Regex("\"([^\"]*)\"");
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(
                     sourceRoot, "*GameAction.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/');
            if (relativePath.StartsWith("bin", StringComparison.Ordinal) ||
                relativePath.StartsWith("obj", StringComparison.Ordinal))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                if (line.Contains("throw", StringComparison.Ordinal)) continue;
                foreach (Match match in literal.Matches(line))
                {
                    var text = match.Groups[1].Value;
                    if (!text.Contains(' ')) continue;
                    foreach (var name in nativeVocabulary)
                    {
                        if (text.Contains(name, StringComparison.Ordinal))
                            offenders.Add(relativePath + ":" + lineNumber + " (" + name + ")");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A wire sentence named the game's own code, which a caller can neither look up nor " +
            "act on: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// Nothing a caller can read carries the game's own .NET exception text — across the whole MCP
    /// transport, every feature's GameAction, and the plugin that answers with them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stack trace and a type name are the one thing a caller can do nothing with, so
    /// <c>GameActionAnswer.CouldNotRead</c> and <c>GameErrored</c> take the exception and
    /// <c>GameActionFaultLog</c> writes it to the suite log under a reference the sentence ends
    /// with. The rule is mechanical because the alternative is remembering it: a new fault arm
    /// that concatenates <c>GetBaseException().Message</c> onto its reason reads exactly like the
    /// thirty-four that used to, and only a sweep tells the two apart. Enumerating
    /// <c>*GameAction.cs</c> alone was too narrow to say that: it left
    /// <c>GameMcpTooltipProjector</c> appending the game's exception text straight into the
    /// tooltip words a caller reads, so the roots are the whole transport.
    /// </para>
    /// <para>
    /// Two shapes stay and neither is an exemption granted to make this pass; both are recognised
    /// by the sink they write to rather than by a path, so no file is exempt and a wire sentence
    /// cannot borrow them. <c>_bindingFailure</c> holds the suite's own composition text — the
    /// binding helpers throw <c>owner.Name + "." + name + " did not match."</c> — which names the
    /// native member rather than a runtime fault, and <c>docs/development/mcp-tools.md</c> keeps
    /// that deliberately: a <c>contract_unavailable</c> result is a defect report and the member is
    /// its subject. <c>nativeDetail</c> is the tooltip reader's log-only sink, which
    /// <c>Plugin.TooltipsUnreadableBecause</c> writes to the log before answering the caller with
    /// an authored sentence. A statement that writes to a log is the third shape, and it is the
    /// destination this whole rule exists to push the text towards —
    /// <c>AutomaticSaveBackupStatus.Failed</c> named among them because the backup health surface
    /// is Mod Config's own diagnostic, not a wire answer.
    /// </para>
    /// <para>
    /// Exactly one site is exempt by name: the JSON-RPC parse error in <c>GameMcpHttpServer</c>.
    /// Its text describes the caller's own bytes, which is the single exception text a caller can
    /// act on, and no part of the game failed — so there is no fault to mint a reference for.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoRawExceptionTextReachesTheWire()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        var diagnosticSinks = new[]
        {
            "_bindingFailure", "nativeDetail", "AutomaticSaveBackupStatus.Failed",
        };
        var logCalls = new[]
        {
            "logerror", "logwarning", "logmessage", "loginfo", "logdebug", "logfatal",
        };
        const string parseErrorFile = "Automata/Runtime/GameMcp/GameMcpHttpServer.cs";
        const string parseErrorSentence = "The request body is not JSON-RPC the suite can read: ";

        var offenders = new List<string>();
        foreach (var path in FilesWhoseTextCanReachACaller(sourceRoot))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/');
            var lineNumber = 0;
            var allowedStatement = false;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                foreach (var sink in diagnosticSinks)
                {
                    if (line.Contains(sink, StringComparison.Ordinal)) allowedStatement = true;
                }
                foreach (var call in logCalls)
                {
                    if (line.Contains(call, StringComparison.OrdinalIgnoreCase))
                        allowedStatement = true;
                }
                if (string.Equals(relativePath, parseErrorFile, StringComparison.Ordinal) &&
                    line.Contains(parseErrorSentence, StringComparison.Ordinal))
                {
                    allowedStatement = true;
                }

                if (line.Contains("GetBaseException", StringComparison.Ordinal) &&
                    !allowedStatement)
                {
                    offenders.Add(relativePath + ":" + lineNumber);
                }
                if (line.TrimEnd().EndsWith(";", StringComparison.Ordinal))
                    allowedStatement = false;
            }
        }

        Assert.True(
            offenders.Count == 0,
            "The game's own exception text reached a caller; hand the exception to " +
            "GameActionFaultLog.Record instead so it goes to the suite log under the reference " +
            "the sentence names: " + string.Join(", ", offenders));
    }

    private static IEnumerable<string> FilesWhoseTextCanReachACaller(string sourceRoot)
    {
        var candidates = new List<string>();
        candidates.AddRange(Directory.EnumerateFiles(
            Path.Combine(sourceRoot, "Automata", "Runtime", "GameMcp"),
            "*.cs",
            SearchOption.AllDirectories));
        candidates.AddRange(Directory.EnumerateFiles(
            sourceRoot, "*GameAction.cs", SearchOption.AllDirectories));
        candidates.Add(Path.Combine(sourceRoot, "Plugin.cs"));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in candidates)
        {
            var relativePath = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/');
            if (relativePath.StartsWith("bin", StringComparison.Ordinal) ||
                relativePath.StartsWith("obj", StringComparison.Ordinal))
            {
                continue;
            }
            if (seen.Add(relativePath)) yield return path;
        }
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "src", "OrbModSuite.csproj")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository source directory.");
    }
}
