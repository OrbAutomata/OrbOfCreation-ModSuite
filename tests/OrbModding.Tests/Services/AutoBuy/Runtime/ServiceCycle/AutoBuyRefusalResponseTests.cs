using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

/// <summary>
/// What the suite does when the game refuses a purchase the worker planned: write down both halves
/// and separate expected affordability staleness from structural contradictions.
/// </summary>
public sealed class AutoBuyRefusalResponseTests : IDisposable
{
    private static readonly Guid CandidateId = Guid.Parse("99a0da45-0000-0000-0000-000000000000");
    private static readonly Guid ResourceId = Guid.Parse("abcdef01-0000-0000-0000-000000000000");
    private static readonly DateTime WrittenAt = new(2026, 7, 25, 13, 45, 6, 789, DateTimeKind.Utc);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "orb-autobuy-refusal-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// A refusal turns Auto Buy's own setting off, through the write path the toggle uses.
    /// </summary>
    /// <remarks>
    /// The ordinary setting rather than a private quarantine flag, so the Mod Config screen shows it
    /// off and turning it back on is the one click an operator already knows. Nothing here re-enables
    /// it.
    /// </remarks>
    [Fact]
    public void ARefusalDisablesAutoBuyThroughItsOwnSetting()
    {
        var config = new EditableConfig(AutoBuyOperationMode.Active);
        var responder = Responder(config, out _, out var logged);

        responder.ObserveRefusal(Report(RefusedOnTheQueuedLevelCap()));

        Assert.Equal(AutoBuyOperationMode.Disabled, config.Current.AutoBuy.Mode);
        var message = Assert.Single(logged);
        Assert.Contains("Upgrade 99a0da45-0000-0000-0000-000000000000", message);
        Assert.Contains("refused by IsMaxQueuedLevel()", message);
        Assert.Contains("Auto Buy disabled itself; re-enable in Mod Config after reviewing", message);
    }

    [Fact]
    public void ProductionStandDownPublishesConfigurationOnceThroughTheCentralJoin()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.TryTakeUnpublishedChange(out _);
        using var statuses = new AutomataFeatureStatuses(
            config.Current,
            lifecycleGeneration: 1,
            new FeatureStatusRegistry());
        var publications = 0;
        var store = new AutomataConfigurationStore(
            config,
            (snapshot, generation) =>
            {
                publications++;
                statuses.ObserveConfiguration(snapshot, generation);
            });
        var responder = new AutoBuyRefusalResponder(
            () => store.Current.AutoBuy.Mode == AutoBuyOperationMode.Active,
            summary =>
            {
                if (!store.DisableAutoBuy()) return;
                statuses.ObserveAutoBuyInvariantStandDown(
                    summary,
                    store.CurrentGeneration);
            },
            new AutoBuyRefusalBundleWriter(() => _directory),
            _ => { },
            () => WrittenAt);

        responder.ObserveRefusal(Report(RefusedOnTheQueuedLevelCap()));

        Assert.Equal(1, publications);
        Assert.Equal(AutoBuyOperationMode.Disabled, store.Current.AutoBuy.Mode);
        Assert.False(statuses.AutoBuy.Current.ConfiguredEnabled);
        Assert.Equal(
            FeatureStatusReasonCode.InvariantViolation,
            statuses.AutoBuy.Current.Reason.Code);
        Assert.Equal(store.CurrentGeneration, statuses.AutoBuy.ConfigurationGeneration);
    }

    [Fact]
    public void AffordabilityRefusalStaysLoudWithoutWritingABundle()
    {
        var config = new EditableConfig(AutoBuyOperationMode.Active);
        var responder = Responder(config, out var bundles, out var logged);

        responder.ObserveRefusal(Report(RefusedOnAffordability()));

        Assert.Equal(AutoBuyOperationMode.Active, config.Current.AutoBuy.Mode);
        Assert.False(Directory.Exists(bundles));
        var message = Assert.Single(logged);
        Assert.Contains("skipped a purchase whose live resources had moved", message);
        Assert.Contains("Auto Buy remains enabled", message);
        Assert.DoesNotContain("Diagnostic bundle", message);
    }

    /// <summary>
    /// A gate the game shut since planning is the fact the action boundary exists to revalidate,
    /// not a contradiction. A prestige shuts a great many at once, and the suite switched the whole
    /// feature off over one candidate the game was right about, for the rest of the session.
    /// </summary>
    [Fact]
    public void AGateTheGameShutSincePlanningSkipsTheCandidateAndKeepsAutoBuyOn()
    {
        var config = new EditableConfig(AutoBuyOperationMode.Active);
        var responder = Responder(config, out var bundles, out var logged);

        responder.ObserveRefusal(Report(RefusedOnAvailability()));

        Assert.Equal(AutoBuyOperationMode.Active, config.Current.AutoBuy.Mode);
        Assert.False(Directory.Exists(bundles));
        var message = Assert.Single(logged);
        Assert.Contains("skipped a purchase the game had shut since planning", message);
        Assert.Contains("refused by IsAvailable()", message);
        Assert.Contains("Auto Buy remains enabled", message);
        Assert.DoesNotContain("Diagnostic bundle", message);
    }

    /// <summary>
    /// Skipping is not retrying. A world collected after the first refusal had its chance to read
    /// the shut gate; reading it open and being refused again is the suite disagreeing with the
    /// game about an entity, which is what the stand-down is for.
    /// </summary>
    [Fact]
    public void TheSameCandidateRefusedAgainFromAFresherWorldStandsAutoBuyDown()
    {
        var config = new EditableConfig(AutoBuyOperationMode.Active);
        var responder = Responder(config, out _, out var logged);

        responder.ObserveRefusal(Report(RefusedOnAvailability()));
        responder.ObserveRefusal(Report(RefusedOnAvailability(), worldGeneration: 5));

        Assert.Equal(AutoBuyOperationMode.Disabled, config.Current.AutoBuy.Mode);
        Assert.Equal(2, logged.Count);
        Assert.Contains("Auto Buy disabled itself", logged[1]);
    }

    /// <summary>
    /// The same world says nothing new: a batch's later actions were all planned before the gate
    /// shut, so the capture never had a chance to see it.
    /// </summary>
    [Fact]
    public void TheSameCandidateRefusedTwiceFromOneWorldIsStillOneStaleReading()
    {
        var config = new EditableConfig(AutoBuyOperationMode.Active);
        var responder = Responder(config, out _, out var logged);

        responder.ObserveRefusal(Report(RefusedOnAvailability()));
        responder.ObserveRefusal(Report(RefusedOnAvailability()));

        Assert.Equal(AutoBuyOperationMode.Active, config.Current.AutoBuy.Mode);
        Assert.Equal(2, logged.Count);
        Assert.All(logged, message => Assert.Contains("Auto Buy remains enabled", message));
    }

    [Fact]
    public void TheLoudLineNamesTheBundleItWrote()
    {
        var config = new EditableConfig(AutoBuyOperationMode.Active);
        var responder = Responder(config, out var bundles, out var logged);

        responder.ObserveRefusal(Report(RefusedOnTheQueuedLevelCap()));

        var written = Assert.Single(Directory.EnumerateFiles(bundles));
        Assert.Contains(written, Assert.Single(logged));
        Assert.Equal("autobuy-refusal-20260725-134506789.txt", Path.GetFileName(written));
    }

    /// <summary>
    /// A bundle that cannot be written does not stop the stand-down, and does not throw into the
    /// action that is already going wrong.
    /// </summary>
    [Fact]
    public void AnUnwritableBundleStillDisablesAndSaysSo()
    {
        var config = new EditableConfig(AutoBuyOperationMode.Active);
        var logged = new List<string>();
        var responder = new AutoBuyRefusalResponder(
            config.IsActive,
            _ => config.DisableAutoBuy(),
            new FailingBundles(),
            logged.Add,
            () => WrittenAt);

        responder.ObserveRefusal(Report(RefusedOnTheQueuedLevelCap()));

        Assert.Equal(AutoBuyOperationMode.Disabled, config.Current.AutoBuy.Mode);
        Assert.Contains("unavailable", Assert.Single(logged));
    }

    /// <summary>
    /// Auto Buy that is already off stands down no further. The batch cascade-terminates on the first
    /// refusal anyway, so a second bundle would only say the same thing twice.
    /// </summary>
    [Fact]
    public void ARefusalWhileAlreadyDisabledDoesNothing()
    {
        var config = new EditableConfig(AutoBuyOperationMode.Disabled);
        var responder = Responder(config, out var bundles, out var logged);

        responder.ObserveRefusal(Report(RefusedOnTheQueuedLevelCap()));

        Assert.Empty(logged);
        Assert.False(Directory.Exists(bundles) && Directory.EnumerateFiles(bundles).Any());
    }

    /// <summary>The bundle carries both halves, named, so a reader can see where they disagree.</summary>
    [Fact]
    public void TheBundleNamesTheLiveTermsAndThePlansBeliefs()
    {
        var text = AutoBuyRefusalBundle.Render(Report(RefusedOnTheQueuedLevelCap()), WrittenAt);

        Assert.Contains("Candidate: Upgrade 99a0da45-0000-0000-0000-000000000000", text);
        Assert.Contains("Verdict: refused by IsMaxQueuedLevel()", text);
        Assert.Contains("  IsAvailable(): passed", text);
        Assert.Contains("  IsMaxQueuedLevel(): REFUSED", text);
        Assert.Contains("  IsMaxLevel(): could not be read", text);
        Assert.Contains("  CostRatio: 0.25", text);
        Assert.Contains("  Resource: abcdef01-0000-0000-0000-000000000000", text);
        Assert.Contains("(planned spendable TrueQuantity)", text);
        Assert.Contains("World generation: 4", text);
        Assert.Contains("World collected at epoch: 12", text);
        Assert.Contains("Config generation: 3", text);
    }

    [Fact]
    public void TheBundleNamesEveryLiveRowEarlierOverlapAndTimingDeltas()
    {
        var otherResource = Guid.Parse("abcdef02-0000-0000-0000-000000000000");
        var live = AutoBuyLiveCostSnapshot.Complete(
            new[]
            {
                new AutoBuyLiveCostRow(ResourceId, false, new BigDouble(2.0, 1), new BigDouble(1.9, 1)),
                new AutoBuyLiveCostRow(otherResource, true, new BigDouble(4.0, 0), new BigDouble(3.0, 0)),
            });
        var diagnosis = new AutoBuyAdmissionDiagnosis(
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Refused,
            in live);
        var earlierCosts = AutoBuyLiveCostSnapshot.Complete(
            new[]
            {
                new AutoBuyLiveCostRow(ResourceId, false, new BigDouble(1.0, 1), new BigDouble(3.0, 1)),
            });
        var baseReport = Report(diagnosis);
        var report = new AutoBuyRefusalReport(
            baseReport.Kind,
            baseReport.Uuid,
            baseReport.RequestedLevels,
            baseReport.Belief,
            baseReport.Diagnosis,
            baseReport.WorldGeneration,
            baseReport.CollectedAtEpoch,
            baseReport.ConfigGeneration,
            baseReport.LifecycleGeneration,
            baseReport.CycleId,
            batchId: 8,
            actionIndex: 3,
            worldCollectedAt: new MonotonicTimestamp(100),
            admissionAttemptedAt: new MonotonicTimestamp(TimeSpan.TicksPerMillisecond * 2 + 100),
            latestWorldGenerationReadable: true,
            latestWorldGeneration: 6,
            earlierPurchases: new[]
            {
                new AutoBuyEarlierPurchase(
                    AutoBuyCandidateKind.Structure,
                    Guid.Parse("99a0da46-0000-0000-0000-000000000000"),
                    actionIndex: 1,
                    committedLevels: 2,
                    in earlierCosts),
            });

        var text = AutoBuyRefusalBundle.Render(in report, WrittenAt);

        Assert.Contains("Classification: AffordabilityChanged", text);
        Assert.Contains($"[1] Resource: {ResourceId:D}", text);
        Assert.Contains($"[2] Resource: {otherResource:D}", text);
        Assert.Contains("Action 1: Structure 99a0da46-0000-0000-0000-000000000000, committed 2 level(s)", text);
        Assert.Contains("Collection-to-admission elapsed milliseconds: 2", text);
        Assert.Contains("Latest world generation at admission: 6", text);
        Assert.Contains("World generations elapsed: 2", text);
    }

    /// <summary>
    /// Nought priced rows out of several is the shape of the game's uncooked boot prices, where every
    /// structure reads as free — the other live fault this bundle exists to make visible.
    /// </summary>
    [Fact]
    public void TheBundleSaysWhenNothingWasPricedAtAll()
    {
        var belief = new AutoBuyPlanBelief(
            isAvailable: true,
            hasFiniteLevels: false,
            isMaxLevel: false,
            isMaxQueuedLevel: false,
            currentLevel: 0,
            queuedLevels: 0,
            costResourceCount: 3,
            pricedResourceCount: 0,
            costRatio: 0.0,
            bindingResourceId: Guid.Empty,
            bindingIsBandwidth: false,
            bindingCost: default,
            bindingAvailable: default,
            bindingReserveFloor: default);

        var text = AutoBuyRefusalBundle.Render(
            new AutoBuyRefusalReport(
                AutoBuyCandidateKind.Structure,
                CandidateId,
                requestedLevels: 1,
                belief,
                RefusedOnTheQueuedLevelCap(),
                worldGeneration: 4,
                collectedAtEpoch: 12,
                configGeneration: 3,
                lifecycleGeneration: 2,
                cycleId: 41),
            WrittenAt);

        Assert.Contains("3 resource(s), 0 priced above nought", text);
        Assert.Contains("every published cost row for this candidate priced at nought", text);
    }

    /// <summary>Bundles accumulate, so a write keeps the newest eight and drops the rest.</summary>
    [Fact]
    public void TheWriterKeepsTheNewestEightBundles()
    {
        Directory.CreateDirectory(_directory);
        for (var index = 0; index < 12; index++)
            File.WriteAllText(Path.Combine(_directory, $"autobuy-refusal-2026010{index:D1}-000000000.txt"), "old");
        File.WriteAllText(Path.Combine(_directory, "someone-elses-file.txt"), "keep me");

        var writer = new AutoBuyRefusalBundleWriter(() => _directory);
        Assert.True(writer.TryWrite("newest", WrittenAt, out var path));

        var bundles = Directory
            .EnumerateFiles(_directory, "autobuy-refusal-*.txt")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(8, bundles.Length);
        Assert.Contains(Path.GetFileName(path), bundles);
        Assert.True(File.Exists(Path.Combine(_directory, "someone-elses-file.txt")));
    }

    [Fact]
    public void TheWriterKeepsOwnedBundlesWithinOneMiB()
    {
        Directory.CreateDirectory(_directory);
        var oldContents = new string('x', 300 * 1024);
        for (var index = 1; index <= 4; index++)
            File.WriteAllText(
                Path.Combine(_directory, $"autobuy-refusal-2026010{index}-000000000.txt"),
                oldContents);

        var writer = new AutoBuyRefusalBundleWriter(() => _directory);
        Assert.True(writer.TryWrite("newest", WrittenAt, out var path));

        var bundles = Directory.EnumerateFiles(_directory, "autobuy-refusal-*.txt").ToArray();
        Assert.True(bundles.Sum(file => new FileInfo(file).Length) <= AutoBuyRefusalBundleWriter.RetainedBytes);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void AnOversizedBundleIsRefusedWithoutTouchingExistingEvidence()
    {
        Directory.CreateDirectory(_directory);
        var existing = Path.Combine(_directory, "autobuy-refusal-20260101-000000000.txt");
        File.WriteAllText(existing, "existing");
        var writer = new AutoBuyRefusalBundleWriter(() => _directory);

        Assert.False(writer.TryWrite(
            new string('x', checked((int)AutoBuyRefusalBundleWriter.RetainedBytes + 1)),
            WrittenAt,
            out var path));

        Assert.Equal(string.Empty, path);
        Assert.Equal("existing", File.ReadAllText(existing));
    }

    private AutoBuyRefusalResponder Responder(
        EditableConfig config,
        out string bundleDirectory,
        out List<string> logged)
    {
        bundleDirectory = _directory;
        var messages = new List<string>();
        logged = messages;
        var directory = _directory;
        return new AutoBuyRefusalResponder(
            config.IsActive,
            _ => config.DisableAutoBuy(),
            new AutoBuyRefusalBundleWriter(() => directory),
            messages.Add,
            () => WrittenAt);
    }

    private static AutoBuyAdmissionDiagnosis RefusedOnTheQueuedLevelCap() =>
        new(
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Unread,
            AutoBuyAdmissionTerm.Refused,
            AutoBuyAdmissionTerm.Passed);

    private static AutoBuyAdmissionDiagnosis RefusedOnAvailability() =>
        new(
            AutoBuyAdmissionTerm.Refused,
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Passed);

    private static AutoBuyAdmissionDiagnosis RefusedOnAffordability()
    {
        var live = AutoBuyLiveCostSnapshot.Complete(
            new[]
            {
                new AutoBuyLiveCostRow(
                    ResourceId,
                    false,
                    new BigDouble(2.0, 0),
                    new BigDouble(1.0, 0)),
            });
        return new AutoBuyAdmissionDiagnosis(
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Refused,
            in live);
    }

    private static AutoBuyRefusalReport Report(
        AutoBuyAdmissionDiagnosis diagnosis,
        ulong worldGeneration = 4) =>
        new(
            AutoBuyCandidateKind.Upgrade,
            CandidateId,
            requestedLevels: 1,
            new AutoBuyPlanBelief(
                isAvailable: true,
                hasFiniteLevels: true,
                isMaxLevel: false,
                isMaxQueuedLevel: false,
                currentLevel: 1,
                queuedLevels: 0,
                costResourceCount: 1,
                pricedResourceCount: 1,
                costRatio: 0.25,
                bindingResourceId: ResourceId,
                bindingIsBandwidth: false,
                bindingCost: new BigDouble(2.0, 0),
                bindingAvailable: new BigDouble(8.0, 0),
                bindingReserveFloor: default),
            diagnosis,
            worldGeneration,
            collectedAtEpoch: 12,
            configGeneration: 3,
            lifecycleGeneration: 2,
            cycleId: 41);

    private sealed class FailingBundles : IAutoBuyRefusalBundlePort
    {
        public bool TryWrite(string contents, DateTime utcNow, out string path)
        {
            path = string.Empty;
            return false;
        }
    }

    private sealed class EditableConfig
    {
        public EditableConfig(AutoBuyOperationMode mode) => Current = Build(mode);

        public SuiteRuntimeConfiguration Current { get; private set; }

        public bool IsActive() =>
            Current.AutoBuy.Mode == AutoBuyOperationMode.Active;

        public void DisableAutoBuy() => Current = Build(AutoBuyOperationMode.Disabled);

        private static SuiteRuntimeConfiguration Build(AutoBuyOperationMode mode) =>
            new SuiteRuntimeConfiguration
            {
                General = new SuiteGeneralConfiguration { Enabled = true },
                AutoBuy = new AutoBuyConfiguration { Mode = mode },
            };
    }
}
