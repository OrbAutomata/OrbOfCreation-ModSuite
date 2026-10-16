using System;
using System.Collections.Generic;
using System.IO;

namespace OrbModding.IlInspect;

internal sealed record InspectionCommand(
    string AssemblyPath,
    string ManagedDirectory,
    string Verb,
    string Query);

internal sealed class CommandLineException : Exception
{
    internal CommandLineException(string message) : base(message)
    {
    }
}

internal static class CommandLine
{
    internal const string Usage =
        "Usage: OrbModding.IlInspect [--game-dir <path>] [--assembly <name.dll|relative/path.dll>] " +
        "<type|method|callers|implementers|strings> <query>";

    private static readonly HashSet<string> Verbs = new(StringComparer.Ordinal)
    {
        "type",
        "method",
        "callers",
        "implementers",
        "strings",
    };

    internal static InspectionCommand Parse(
        IReadOnlyList<string> args,
        Func<string?> gameDirectoryEnvironment)
    {
        string? gameDirectory = null;
        var assemblyName = "Assembly-CSharp.dll";
        var positionals = new List<string>();

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--game-dir":
                    gameDirectory = ReadOption(args, ref index, "--game-dir");
                    break;
                case "--assembly":
                    assemblyName = ReadOption(args, ref index, "--assembly");
                    break;
                default:
                    if (args[index].StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new CommandLineException($"Unknown option: {args[index]}");
                    }
                    positionals.Add(args[index]);
                    break;
            }
        }

        if (positionals.Count != 2 || !Verbs.Contains(positionals[0]))
        {
            throw new CommandLineException("Expected one query verb and one query value.");
        }

        gameDirectory ??= gameDirectoryEnvironment();
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            throw new CommandLineException(
                "No game directory was provided. Pass --game-dir or set OOC_GAME_DIR.");
        }

        ValidateAssemblyReference(assemblyName);
        var managedDirectory = ResolveManagedDirectory(gameDirectory);
        var assemblyPath = ResolveAssemblyPath(gameDirectory, managedDirectory, assemblyName);

        return new InspectionCommand(
            assemblyPath, managedDirectory, positionals[0], positionals[1]);
    }

    private static string ReadOption(IReadOnlyList<string> args, ref int index, string option)
    {
        index++;
        if (index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new CommandLineException($"{option} requires a value.");
        }
        return args[index];
    }

    /// <summary>
    /// A DLL the game ships, named relative to what the game ships it under. A bare name is the
    /// common case and means the Managed directory; anything a mod loader puts elsewhere —
    /// <c>BepInEx/core/BepInEx.dll</c> — is named by its path from the game directory. Rooted paths
    /// and <c>..</c> are refused, so no argument names a file outside the game.
    /// </summary>
    private static void ValidateAssemblyReference(string assembly)
    {
        if (!string.IsNullOrWhiteSpace(assembly) &&
            assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
            !Path.IsPathRooted(assembly) &&
            !Array.Exists(assembly.Split('/', '\\'), part => part == ".."))
        {
            return;
        }

        throw new CommandLineException(
            "--assembly must be one DLL under the game directory, named either by its file name " +
            "in Managed or by its relative path such as BepInEx/core/BepInEx.dll.");
    }

    private static string ResolveAssemblyPath(
        string gameDirectory,
        string managedDirectory,
        string assembly)
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(managedDirectory, assembly)),
            Path.GetFullPath(Path.Combine(Path.GetFullPath(gameDirectory), assembly)),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException(
            "Target assembly was not found: " + string.Join(", ", candidates), candidates[0]);
    }

    private static string ResolveManagedDirectory(string gameDirectory)
    {
        var root = Path.GetFullPath(gameDirectory);
        var candidates = new[]
        {
            root,
            Path.Combine(root, "Orb Of Creation_Data", "Managed"),
            Path.Combine(root, "Contents", "Resources", "Data", "Managed"),
            Path.Combine(root, "Orb Of Creation.app", "Contents", "Resources", "Data", "Managed"),
        };

        foreach (var candidate in candidates)
        {
            if (string.Equals(Path.GetFileName(candidate), "Managed", StringComparison.Ordinal) &&
                Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new DirectoryNotFoundException(
            $"No Orb of Creation Managed directory was found under: {root}");
    }
}
