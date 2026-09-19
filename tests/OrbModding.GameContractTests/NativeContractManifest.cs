using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbModding.GameContractTests;

internal sealed class NativeContractManifest
{
    public int SchemaVersion { get; set; }

    public string AuditedAt { get; set; } = string.Empty;

    public string GameBuild { get; set; } = string.Empty;

    public string Provenance { get; set; } = string.Empty;

    public List<NativeAssemblyManifest> Assemblies { get; set; } = new();

    public List<NativeBaselineManifest> Baselines { get; set; } = new();

    public List<NativeContractEntry> Contracts { get; set; } = new();

    /// <summary>
    /// The namespaces whose reflection is capture, and so must answer the capture discipline. Every
    /// selector inside one is audited against the capture blocks rather than merely against the
    /// existence of a contract.
    /// </summary>
    public List<string> CaptureRoots { get; set; } = new();

    /// <summary>
    /// The readers the collector runs once per lifecycle epoch rather than once per pass, named here
    /// so the manifest's <c>per-epoch</c> cadence can be reconciled against the collector's own
    /// marking rather than believed.
    /// </summary>
    public List<string> CaptureStructuralReaders { get; set; } = new();

    public NativeSourceAuditManifest SourceAudit { get; set; } = new();

    public static NativeContractManifest Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "native-contracts.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Native contract manifest was not copied to the test output.", path);
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        return JsonSerializer.Deserialize<NativeContractManifest>(File.ReadAllText(path), options)
            ?? throw new InvalidDataException("Native contract manifest is empty.");
    }

    public NativeAssemblyManifest RequireAssembly(string id) =>
        Assemblies.Single(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));

    public NativeBaselineManifest RequireBaseline(string id) =>
        Baselines.Single(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
}

internal sealed class NativeAssemblyManifest
{
    public string Id { get; set; } = string.Empty;

    public string File { get; set; } = string.Empty;
}

internal sealed class NativeBaselineManifest
{
    public string Id { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public string AuditedAt { get; set; } = string.Empty;

    public string GameBuild { get; set; } = string.Empty;

    public string Provenance { get; set; } = string.Empty;

    public List<NativeBaselineAssemblyManifest> Assemblies { get; set; } = new();

    public NativeBaselineAssemblyManifest RequireAssembly(string id) =>
        Assemblies.Single(candidate => string.Equals(candidate.Assembly, id, StringComparison.Ordinal));
}

internal sealed class NativeBaselineAssemblyManifest
{
    public string Assembly { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public string Provenance { get; set; } = string.Empty;
}

internal sealed class NativeContractEntry
{
    public string Id { get; set; } = string.Empty;

    public string Assembly { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string TypeVisibility { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string? Member { get; set; }

    public string? Visibility { get; set; }

    public bool? Static { get; set; }

    public string? ValueType { get; set; }

    public string? ReturnType { get; set; }

    public List<string> Parameters { get; set; } = new();

    public string? BaseType { get; set; }

    public List<string> Owners { get; set; } = new();

    public List<string> Usages { get; set; } = new();

    public string Place { get; set; } = string.Empty;

    public List<string> SourceTokens { get; set; } = new();

    /// <summary>
    /// What this touch costs the game, for a contract at the capture boundary. Null everywhere else:
    /// an action's whole purpose is to make the game do something, so the questions this answers are
    /// not questions about it.
    /// </summary>
    public NativeCaptureDiscipline? Capture { get; set; }
}

/// <summary>
/// One capture touch, described by what it makes the game do rather than by what it returns.
/// </summary>
/// <remarks>
/// The manifest already proved shape — that the member exists, with that signature. It said nothing
/// about cost or about writes, which is how a per-pass sweep over the whole registry and a single
/// field load came to look identical in it. These are the questions that tell them apart.
/// </remarks>
internal sealed class NativeCaptureDiscipline
{
    /// <summary>
    /// <c>grab</c> reads a stored value; <c>computes</c> makes the game run a formula;
    /// <c>composite</c> makes it build an aggregate; <c>enumerating</c> makes it walk a collection.
    /// </summary>
    public string Class { get; set; } = string.Empty;

    /// <summary>How often the touch happens: <c>per-pass</c>, <c>per-epoch</c>, or <c>request-time</c>.</summary>
    public string Cadence { get; set; } = string.Empty;

    /// <summary>Why the suite takes this reading from the game rather than deriving it.</summary>
    public string Justification { get; set; } = string.Empty;

    /// <summary>What the classification rests on — a quoted IL body, or the signature alone.</summary>
    public string Evidence { get; set; } = string.Empty;

    /// <summary>
    /// Whether the suite could answer this from facts it already publishes. True is debt: a reading
    /// still taken from the game that a port would replace, named so it can be counted.
    /// </summary>
    public bool Derivable { get; set; }

    /// <summary>
    /// What the game writes while answering. Empty is the only acceptable answer at
    /// <c>per-pass</c> cadence, and the shrinking allowlist in the manifest tests is what the ones
    /// that are not empty cost.
    /// </summary>
    public List<string> SideEffects { get; set; } = new();
}

internal sealed class NativeSourceAuditManifest
{
    public List<string> Roots { get; set; } = new();

    public List<NativeSourceExemption> Exemptions { get; set; } = new();
}

internal sealed class NativeSourceExemption
{
    public string Path { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}

internal static class RepositoryPaths
{
    public static string RequireRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "data")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output.");
    }
}
