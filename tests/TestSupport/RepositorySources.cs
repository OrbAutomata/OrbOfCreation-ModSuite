using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OrbModding.TestSupport;

internal static class RepositorySources
{
    public static string Root
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "src", "OrbModSuite.csproj")))
                    return directory.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate the repository source directory.");
        }
    }

    public static IEnumerable<string> SourceFiles(string pattern, string directory = "src")
    {
        var root = Root;
        return Directory
            .EnumerateFiles(
                Path.Combine(root, directory.Replace('/', Path.DirectorySeparatorChar)),
                pattern,
                SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(Path.GetRelativePath(root, Path.GetDirectoryName(path)!)));
    }

    private static bool IsBuildOutput(string relativeDirectory) =>
        relativeDirectory
            .Split(Path.DirectorySeparatorChar)
            .Any(segment =>
                segment.StartsWith("bin", StringComparison.Ordinal) ||
                segment.StartsWith("obj", StringComparison.Ordinal));
}
