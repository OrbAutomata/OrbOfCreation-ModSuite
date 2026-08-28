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
    /// No GameAction hands a caller the game's own .NET exception text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stack trace and a type name are the one thing a caller can do nothing with, so
    /// <c>GameActionAnswer.CouldNotRead</c> and <c>GameErrored</c> take the exception and
    /// <c>GameActionFaultLog</c> writes it to the suite log under a reference the sentence ends
    /// with. The rule is mechanical because the alternative is remembering it: a new fault arm
    /// that concatenates <c>GetBaseException().Message</c> onto its reason reads exactly like the
    /// thirty-four that used to, and only a sweep tells the two apart.
    /// </para>
    /// <para>
    /// One shape stays, and it is not an exemption granted to make this pass. A binding
    /// composition failure's text is the suite's own — the binding helpers throw
    /// <c>owner.Name + "." + name + " did not match."</c> — so it names the native member rather
    /// than a runtime fault, and
    /// <c>docs/development/mcp-tools.md</c> keeps that deliberately: a
    /// <c>contract_unavailable</c> result is a defect report and the member is its subject.
    /// It is recognised by the field it lands in, so a wire sentence cannot borrow it.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoGameActionPutsTheGamesOwnExceptionTextOnTheWire()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
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
            var bindingFailureStatement = false;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                if (line.Contains("_bindingFailure", StringComparison.Ordinal))
                    bindingFailureStatement = true;
                if (line.Contains("GetBaseException", StringComparison.Ordinal) &&
                    !bindingFailureStatement)
                {
                    offenders.Add(relativePath + ":" + lineNumber);
                }
                if (line.TrimEnd().EndsWith(";", StringComparison.Ordinal))
                    bindingFailureStatement = false;
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A GameAction put the game's own exception text where a caller reads it; hand the " +
            "exception to GameActionAnswer.CouldNotRead or GameErrored instead so it goes to the " +
            "suite log under a reference: " + string.Join(", ", offenders));
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
