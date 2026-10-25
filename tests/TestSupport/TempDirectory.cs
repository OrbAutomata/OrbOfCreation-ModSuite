using System;
using System.IO;

namespace OrbModding.TestSupport;

/// <summary>Owns one unique path, including tests that require it to remain absent until first use.</summary>
internal sealed class TempDirectory : IDisposable
{
    internal TempDirectory(bool create = false)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "orb-tests-" + Guid.NewGuid().ToString("N"));
        if (create) Directory.CreateDirectory(Path);
    }

    internal string Path { get; }

    internal string CreateDirectory(string relativePath)
    {
        var path = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
