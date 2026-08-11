using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class KnownEntitiesGenerationTests
{
    [Fact]
    public void ExplicitSelectionIsUniqueValidAndPinnedToCanonicalMappings()
    {
        var canonical = ReadTsv("entity-mappings.tsv", "id\tname\ttype", 3)
            .ToDictionary(parts => parts[0], StringComparer.OrdinalIgnoreCase);
        var selected = ReadTsv("known-entities.tsv", "symbol\tid\tname\ttype", 4);

        Assert.Equal(71, selected.Count);
        Assert.Equal(selected.Count, selected.Select(parts => parts[0]).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(selected.Count, selected.Select(parts => parts[1]).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(selected, parts =>
        {
            Assert.Matches("^[A-Z][A-Za-z0-9]*$", parts[0]);
            Assert.True(Guid.TryParseExact(parts[1], "D", out _), $"Invalid UUID for {parts[0]}: {parts[1]}");
            Assert.True(canonical.TryGetValue(parts[1], out var mapping), $"Canonical mapping missing: {parts[1]}");
            Assert.Equal(mapping![1], parts[2]);
            Assert.Equal(mapping[2], parts[3]);
        });
    }

    [Fact]
    public void GeneratedDeclarationsMatchTheExplicitSelection()
    {
        var selected = ReadTsv("known-entities.tsv", "symbol\tid\tname\ttype", 4)
            .ToDictionary(parts => parts[0], StringComparer.Ordinal);
        var fields = typeof(KnownEntities).GetFields(BindingFlags.Public | BindingFlags.Static);

        Assert.Equal(selected.Count, fields.Length);
        Assert.All(fields, field =>
        {
            Assert.True(field.IsInitOnly);
            Assert.True(field.FieldType.IsGenericType);
            Assert.Equal(typeof(KnownEntity<>), field.FieldType.GetGenericTypeDefinition());
            Assert.True(selected.TryGetValue(field.Name, out var expected), $"Unexpected generated entry: {field.Name}");
            var actual = field.GetValue(null);
            Assert.NotNull(actual);
            Assert.Equal(new Guid(expected![1]), Read<Guid>(actual!, "Uuid"));
            Assert.Equal(expected[2], Read<string>(actual!, "DiagnosticName"));
            Assert.Equal(expected[3], Read<string>(actual!, "ManagedTypeName"));
            var marker = field.FieldType.GetGenericArguments()[0];
            Assert.Equal(expected[3] + "Contract", marker.Name);
            Assert.Equal(typeof(KnownEntities).Assembly, marker.Assembly);
            Assert.Empty(marker.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
        });
    }

    [Fact]
    [Trait("Category", "ExternalProcess")]
    public void GeneratorRejectsInvalidDuplicateDriftAndStaleInputs()
    {
        const string id = "67acd892-8a8a-455a-aa71-3fb06e75bf38";
        const string canonical = "id\tname\ttype\n" + id + "\tAlchemicScroll\tResourceSO\n";
        var cases = new[]
        {
            new FailureCase(
                "symbol\tid\tname\ttype\nFirst\t" + id + "\tAlchemicScroll\tResourceSO\nSecond\t" + id + "\tAlchemicScroll\tResourceSO\n",
                false,
                "Duplicate selected UUID"),
            new FailureCase(
                "symbol\tid\tname\ttype\nBroken\tnot-a-guid\tAlchemicScroll\tResourceSO\n",
                false,
                "Invalid selected UUID"),
            new FailureCase(
                "symbol\tid\tname\ttype\nAlchemicScroll\t" + id + "\tAlchemicScroll\tWrongType\n",
                false,
                "Canonical mapping drift"),
            new FailureCase(
                "symbol\tid\tname\ttype\nAlchemicScroll\t" + id + "\tAlchemicScroll\tResourceSO\n",
                true,
                "Generated output is stale"),
        };

        foreach (var testCase in cases)
        {
            var result = RunGenerator(canonical, testCase.Selection, testCase.Verify);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(testCase.ExpectedFailure, result.Output, StringComparison.Ordinal);
        }
    }

    private static IReadOnlyList<string[]> ReadTsv(string fileName, string header, int width)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", fileName);
        var lines = File.ReadAllLines(path);
        Assert.NotEmpty(lines);
        Assert.Equal(header, lines[0]);
        return lines.Skip(1).Select((line, index) =>
        {
            var parts = line.Split('\t');
            Assert.True(parts.Length == width, $"Unexpected field count at {fileName}:{index + 2}");
            return parts;
        }).ToArray();
    }

    private static T Read<T>(object value, string property) =>
        Assert.IsType<T>(value.GetType().GetProperty(property)!.GetValue(value));

    private static GeneratorResult RunGenerator(string canonical, string selection, bool verify)
    {
        var directory = Path.Combine(Path.GetTempPath(), "orb-known-entities-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var canonicalPath = Path.Combine(directory, "canonical.tsv");
            var selectionPath = Path.Combine(directory, "selection.tsv");
            var outputPath = Path.Combine(directory, "generated.cs");
            File.WriteAllText(canonicalPath, canonical);
            File.WriteAllText(selectionPath, selection);
            File.WriteAllText(outputPath, "stale");
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var start = new ProcessStartInfo
            {
                FileName = isWindows ? "powershell.exe" : "bash",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            if (isWindows)
            {
                start.ArgumentList.Add("-NoProfile");
                start.ArgumentList.Add("-ExecutionPolicy");
                start.ArgumentList.Add("Bypass");
                start.ArgumentList.Add("-File");
                start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "tools", "generate-known-entities.ps1"));
            }
            else
            {
                start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "tools", "generate-known-entities.sh"));
            }
            start.ArgumentList.Add("-CanonicalPath");
            start.ArgumentList.Add(canonicalPath);
            start.ArgumentList.Add("-SelectionPath");
            start.ArgumentList.Add(selectionPath);
            start.ArgumentList.Add("-OutputPath");
            start.ArgumentList.Add(outputPath);
            if (verify) start.ArgumentList.Add("-Verify");
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new GeneratorResult(process.ExitCode, output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record FailureCase(string Selection, bool Verify, string ExpectedFailure);
    private sealed record GeneratorResult(int ExitCode, string Output);
}
