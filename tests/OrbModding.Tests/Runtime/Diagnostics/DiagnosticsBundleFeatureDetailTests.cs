using System;
using System.IO;
using System.IO.Compression;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.Diagnostics;

public sealed class DiagnosticsBundleFeatureDetailTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BundleManifestIncludesRedactedThinkingWithoutChangingHealthyStateOrEmptyDetailLines(bool withDetails)
    {
        using var temporary = new TempDirectory();
        var config = Path.Combine(temporary.Path, "suite.cfg");
        var log = Path.Combine(temporary.Path, "suite.log");
        var saves = Path.Combine(temporary.Path, "saves");
        var journals = Path.Combine(temporary.Path, "journal");
        Directory.CreateDirectory(saves);
        Directory.CreateDirectory(journals);
        File.WriteAllText(config, "Enabled=true\n");
        File.WriteAllText(log, "Ready.\n");
        File.WriteAllBytes(Path.Combine(saves, "ooc_save_0.sav"), new byte[] { 1, 2, 3 });
        var detail = withDetails
            ? "Dial one: cost 100; budget 500.\nDial two: drain 3/s; resource buffer 300s.\n" +
              "Dial three: Knowledge budget 800.\nDial four: mana-buffer-300 at /Users/Alice/game (Alice)."
            : string.Empty;
        var feature = new FeatureStatusSnapshot(new FeatureStatusKey(PluginIds.SuiteGuid, "AutoLevels"),
            "Auto Levels", true, FeatureStatusState.Operational, lifecycleGeneration: 9, detail: detail);
        var result = DiagnosticsBundleBuilder.Build(new DiagnosticsBundleBuildRequest(
            new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc), Path.Combine(temporary.Path, "output"),
            config, saves, log, "test", "audited fixture", new[] { feature },
            Array.Empty<RuntimeServiceDiagnosticsSnapshot>(),
            new AutomataDiagnosticsRuntimeEvidence(null, DecisionJournalStatus.Unavailable, journals, string.Empty),
            new DiagnosticsTextRedactor(new[] { "/Users/Alice" }, new[] { "Alice" })));
        using var archive = ZipFile.OpenRead(result.Path);
        using var manifestStream = archive.GetEntry("manifest.txt")!.Open();
        using var reader = new StreamReader(manifestStream);
        var manifest = reader.ReadToEnd();
        var originalLine = "- " + feature.Key + " | Auto Levels | configured=on | state=Operational | code=None | generation=9";
        if (withDetails)
        {
            Assert.Contains(originalLine + " | detail=Dial one: cost 100; budget 500.", manifest);
            Assert.Contains("Dial two: drain 3/s; resource buffer 300s.", manifest);
            Assert.Contains("Dial three: Knowledge budget 800.", manifest);
            Assert.Contains("Dial four: mana-buffer-300", manifest);
            Assert.DoesNotContain("Alice", manifest, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/Users/Alice", manifest, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains(originalLine + Environment.NewLine, manifest);
            Assert.DoesNotContain(" | detail=", manifest);
        }
        Assert.DoesNotContain(" | reason=", manifest);
    }
}
