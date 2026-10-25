using OrbModding.TestSupport;
using System;
using System.IO;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class GameAssemblyAuditTests
{
    [Fact]
    public void ExactWindowsAndMacPairsAreAcceptedButMixedPairsFailClosed()
    {
        Assert.True(Result(
            GameAssemblyAudit.WindowsAssemblyCSharpSha256,
            GameAssemblyAudit.WindowsFirstPassSha256).MatchesExpected);
        Assert.True(Result(
            GameAssemblyAudit.WindowsV1052AssemblyCSharpSha256,
            GameAssemblyAudit.WindowsV1052FirstPassSha256).MatchesExpected);
        Assert.True(Result(
            GameAssemblyAudit.MacAssemblyCSharpSha256,
            GameAssemblyAudit.MacFirstPassSha256).MatchesExpected);
        Assert.True(Result(
            GameAssemblyAudit.MacV1052AssemblyCSharpSha256,
            GameAssemblyAudit.MacV1052FirstPassSha256).MatchesExpected);

        Assert.False(Result(
            GameAssemblyAudit.WindowsAssemblyCSharpSha256,
            GameAssemblyAudit.MacFirstPassSha256).MatchesExpected);
        Assert.False(Result(
            GameAssemblyAudit.MacAssemblyCSharpSha256,
            GameAssemblyAudit.WindowsFirstPassSha256).MatchesExpected);
        Assert.False(Result(
            GameAssemblyAudit.WindowsV1052AssemblyCSharpSha256,
            GameAssemblyAudit.MacFirstPassSha256).MatchesExpected);
        Assert.False(Result(new string('A', 64), new string('B', 64)).MatchesExpected);
    }

    [Fact]
    public void ResolvesOnlyPlatformRelativeWindowsAndMacManagedLayouts()
    {
        using var temporary = new TempDirectory();
        var root = temporary.Path;
        var windowsRoot = Path.Combine(root, "windows");
        var windowsManaged = Path.Combine(windowsRoot, "Orb Of Creation_Data", "Managed");
        CreateAssemblyPair(windowsManaged);
        AssertResolved(windowsRoot, windowsManaged);

        var macAppRoot = Path.Combine(root, "mac-app", "Orb Of Creation.app");
        var macAppManaged = Path.Combine(macAppRoot, "Contents", "Resources", "Data", "Managed");
        CreateAssemblyPair(macAppManaged);
        AssertResolved(macAppRoot, macAppManaged);

        var macInstallRoot = Path.Combine(root, "mac-install");
        var nestedAppManaged = Path.Combine(
            macInstallRoot,
            "Orb Of Creation.app",
            "Contents",
            "Resources",
            "Data",
            "Managed");
        CreateAssemblyPair(nestedAppManaged);
        AssertResolved(macInstallRoot, nestedAppManaged);


    }

    [Fact]
    public void MultipleCompleteLayoutsAreRejectedAsAmbiguous()
    {
        using var temporary = new TempDirectory();
        var root = temporary.Path;
        CreateAssemblyPair(Path.Combine(root, "Orb Of Creation_Data", "Managed"));
        CreateAssemblyPair(Path.Combine(root, "Contents", "Resources", "Data", "Managed"));

        Assert.False(GameAssemblyAudit.TryResolveManagedDirectory(root, out _, out var reason));
        Assert.Contains("more than one", reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(GameAssemblyAudit.Check(root).MatchesExpected);


    }

    private static AssemblyAuditResult Result(string main, string firstPass) => new(
        new AssemblyHashResult("main", main),
        new AssemblyHashResult("first-pass", firstPass));

    private static void AssertResolved(string gameRoot, string expectedManaged)
    {
        Assert.True(
            GameAssemblyAudit.TryResolveManagedDirectory(gameRoot, out var actual, out var reason),
            reason);
        Assert.Equal(Path.GetFullPath(expectedManaged), actual);
    }

    private static void CreateAssemblyPair(string managedDirectory)
    {
        Directory.CreateDirectory(managedDirectory);
        File.WriteAllBytes(Path.Combine(managedDirectory, "Assembly-CSharp.dll"), Array.Empty<byte>());
        File.WriteAllBytes(
            Path.Combine(managedDirectory, "Assembly-CSharp-firstpass.dll"),
            Array.Empty<byte>());
    }

}
