using OrbModding.TestSupport;
using System;
using System.IO;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

/// <summary>
/// The load gate is the guard that makes owning the game's economy math safe: the ported arithmetic
/// is only valid for an audited or explicitly accepted assembly pair. Unknown complete pairs admit
/// diagnostics in quarantine; incomplete installs still refuse completely.
/// </summary>
public sealed class SuiteLoadGateTests
{
    [Fact]
    public void AnUnknownCompleteBuildLoadsOnlyTheQuarantinedControlPlane()
    {
        using var install = new FakeInstall(
            assemblyCSharp: "not the audited assembly",
            firstPass: "nor is this");

        var decision = SuiteLoadGate.Evaluate(install.Root);

        Assert.False(decision.ShouldLoad);
        Assert.True(decision.CanLoadControlPlane);
        Assert.True(decision.IsQuarantined);
        Assert.Empty(decision.BaselineId);
        Assert.Equal(129, decision.ObservedBuildFingerprint.Length);
    }

    [Fact]
    public void AQuarantineWarningNamesTheObservedHashesAndTheAuditedBaselines()
    {
        // The acknowledgement is bound to the pair named here, so the warning must carry both
        // observed identities and the supported baselines.
        using var install = new FakeInstall("unknown", "unknown");

        var decision = SuiteLoadGate.Evaluate(install.Root);

        Assert.Contains("Gameplay runtime quarantined", decision.Message, StringComparison.Ordinal);
        Assert.Contains(GameAssemblyAudit.WindowsBaselineId, decision.Message, StringComparison.Ordinal);
        Assert.Contains(GameAssemblyAudit.WindowsV1052BaselineId, decision.Message, StringComparison.Ordinal);
        Assert.Contains(GameAssemblyAudit.MacBaselineId, decision.Message, StringComparison.Ordinal);
        Assert.Contains(GameAssemblyAudit.MacV1052BaselineId, decision.Message, StringComparison.Ordinal);
        Assert.Contains("Observed Assembly-CSharp=", decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingInstallIsRefusedRatherThanTreatedAsUnverifiable()
    {
        // An audit that cannot run is not an audit that passed.
        using var temporary = new TempDirectory();
        var decision = SuiteLoadGate.Evaluate(temporary.Path);

        Assert.False(decision.ShouldLoad);
        Assert.False(decision.CanLoadControlPlane);
        Assert.Contains("Refusing to load", decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyGameRootIsRefused()
    {
        var decision = SuiteLoadGate.Evaluate(string.Empty);
        Assert.False(decision.ShouldLoad);
        Assert.False(decision.CanLoadControlPlane);
    }

    [Fact]
    public void PartialAssemblyPairsAreRefused()
    {
        // One matching assembly is not a matching build; admission requires a complete pair.
        using var install = new FakeInstall(
            assemblyCSharp: "unknown",
            firstPass: null);

        Assert.False(SuiteLoadGate.Evaluate(install.Root).ShouldLoad);
        Assert.False(SuiteLoadGate.Evaluate(install.Root).CanLoadControlPlane);
    }

    /// <summary>A throwaway managed-assembly layout the audit will discover and hash.</summary>
    private sealed class FakeInstall : IDisposable
    {
        private readonly TempDirectory _temporary = new();
        internal FakeInstall(string? assemblyCSharp, string? firstPass)
        {
            Root = _temporary.Path;
            var managed = Path.Combine(Root, "Orb Of Creation_Data", "Managed");
            Directory.CreateDirectory(managed);

            if (assemblyCSharp is not null)
                File.WriteAllText(Path.Combine(managed, "Assembly-CSharp.dll"), assemblyCSharp);
            if (firstPass is not null)
                File.WriteAllText(Path.Combine(managed, "Assembly-CSharp-firstpass.dll"), firstPass);
        }

        internal string Root { get; }

        public void Dispose() => _temporary.Dispose();
    }
}
