using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OrbModding.TestSupport;

namespace OrbModding.Tests.Services;

/// <summary>
/// Folder ownership for the single production assembly. Namespace identity cannot distinguish the
/// feature modules: most of them deliberately share OrbAutomata. Only declarations are indexed;
/// dependencies are read from compiled signatures and IL by the existing scanner.
/// </summary>
internal sealed class ProductionTypeSources
{
    private static readonly Regex Namespace = new(@"^namespace\s+([\w.]+)\s*[;{]", RegexOptions.Multiline);
    private static readonly Regex Declaration = new(
        @"^\s*(?:(?:public|internal|private|protected|static|abstract|sealed|readonly|ref|partial|unsafe|new)\s+)*(?:class|struct|enum|interface|record(?:\s+(?:class|struct))?)\s+(\w+)",
        RegexOptions.Multiline);
    private static readonly Regex DelegateDeclaration = new(
        @"^\s*(?:public|internal)\s+delegate\s+[\w.<>,?\[\]\s]+?\s+(\w+)(?:<[^()]+>)?\s*\(", RegexOptions.Multiline);
    private readonly Dictionary<string, List<string>> _files = new(StringComparer.Ordinal);

    internal ProductionTypeSources()
    {
        foreach (var file in RepositorySources.SourceFiles("*.cs"))
        {
            var source = Regex.Replace(File.ReadAllText(file), @"/\*.*?\*/|//[^\r\n]*", string.Empty, RegexOptions.Singleline);
            var ns = Namespace.Match(source);
            if (!ns.Success) continue;
            var relative = Path.GetRelativePath(Path.Combine(RepositorySources.Root, "src"), file).Replace('\\', '/');
            foreach (Match declaration in Declaration.Matches(source).Concat(DelegateDeclaration.Matches(source)))
            {
                var key = ns.Groups[1].Value + "." + declaration.Groups[1].Value;
                if (!_files.TryGetValue(key, out var paths)) _files.Add(key, paths = new List<string>());
                if (!paths.Contains(relative, StringComparer.Ordinal)) paths.Add(relative);
            }
        }
    }

    internal IReadOnlyList<string> Files(Type type)
    {
        while (type.DeclaringType is not null) type = type.DeclaringType;
        var name = (type.FullName ?? type.Name).Split('`')[0];
        return _files.TryGetValue(name, out var paths) ? paths : Array.Empty<string>();
    }

    internal string? Module(Type type)
    {
        var modules = Files(type).Select(path => path.Contains('/') ? path.Split('/')[0] : "<root>")
            .Distinct(StringComparer.Ordinal).ToArray();
        if (modules.Length > 1)
            throw new InvalidOperationException("Production type declared across module boundaries: " + type.FullName);
        return modules.SingleOrDefault();
    }
}
