using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

/// <summary>
/// What the suite does when the game refuses a purchase the worker planned: pass price refusals
/// through, back a disputed candidate off, and hold the lifecycle only for the impossible refusal.
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
    /// A refusal every readable term says cannot happen holds Auto Buy back for the lifecycle: one
    /// bundle, one error line, and the latch. The player's saved mode is not the responder's to write.
    /// </summary>
    [Fact]
    public void AnImpossibleRefusalTripsTheLifecycleLatch()
    {
        var latch = new AutoBuyInvariantLatch();
        var responder = Responder(latch, out var bundles, out var logged);

        var answered = responder.Answer(Report(RefusedByAnUnreadableTerm()), Refused);

        var message = Assert.Single(logged);
        Assert.True(latch.IsTripped);
        Assert.Equal(message, latch.Summary);
        Assert.Contains("Upgrade 99a0da45-0000-0000-0000-000000000000", message);
        Assert.Contains(AutoBuyAdmissionDiagnosis.RefusedByAnUnreadableTerm, message);
        Assert.Contains("holds off until the next load, reset or prestige", message);
        Assert.Single(Directory.EnumerateFiles(bundles));
        Assert.False(answered.BacksOffTarget);
    }

    /// <summary>
    /// The game saying the candidate is at its level cap while the world said below it is a
    /// disagreement about one candidate, most often a level the player queued by hand since
    /// collection, so that candidate backs off quietly and the rest of Auto Buy runs.
    /// </summary>
    [Fact]
    public void ALevelRefusalBacksTheCandidateOffQuietlyWithoutABundle()
    {
        var latch = new AutoBuyInvariantLatch();
        var responder = Responder(latch, out var bundles, out var logged);

        var answered = responder.Answer(Report(RefusedOnTheQueuedLevelCap()), Refused);

        Assert.True(answered.BacksOffTarget);
        Assert.True(answered.QuietFirstFailure);
        Assert.Equal(AutoBuyServicePolicies.RefusalBackoffCap, answered.TargetBackoffCap);
        Assert.Equal(Refused.Reason, answered.Reason);
        Assert.False(latch.IsTripped);
        Assert.Empty(logged);
        Assert.False(Directory.Exists(bundles));
    }

    /// <summary>
    /// A gate the game shut since planning is the fact the action boundary exists to revalidate. A
    /// prestige shuts a great many at once, so the first refusal backs off quietly and only a
    /// refusal again from a fresher world is announced.
    /// </summary>
    [Fact]
    public void AGateTheGameShutSincePlanningBacksTheCandidateOffQuietly()
    {
        var latch = new AutoBuyInvariantLatch();
        var responder = Responder(latch, out var bundles, out var logged);

        var answered = responder.Answer(Report(RefusedOnAvailability()), Refused);

        Assert.True(answered.BacksOffTarget);
        Assert.True(answered.QuietFirstFailure);
        Assert.Equal(AutoBuyServicePolicies.RefusalBackoffCap, answered.TargetBackoffCap);
        Assert.False(latch.IsTripped);
        Assert.Empty(logged);
        Assert.False(Directory.Exists(bundles));
    }

    [Fact]
    public void AffordabilityRefusalIsAnOrdinaryRefusal()
    {
        var latch = new AutoBuyInvariantLatch();
        var responder = Responder(latch, out var bundles, out var logged);
        var skipped = ServiceActionResult.Skipped(CommonActionResultCodes.Skipped, "refused by price");

        var answered = responder.Answer(Report(RefusedOnAffordability()), skipped);

        Assert.False(answered.BacksOffTarget);
        Assert.Equal(ServiceActionDisposition.Skipped, answered.Disposition);
        Assert.False(latch.IsTripped);
        Assert.False(Directory.Exists(bundles));
        Assert.Empty(logged);
    }

    [Fact]
    public void TheLoudLineNamesTheBundleItWrote()
    {
        var responder = Responder(new AutoBuyInvariantLatch(), out var bundles, out var logged);

        responder.Answer(Report(RefusedByAnUnreadableTerm()), Refused);

        var written = Assert.Single(Directory.EnumerateFiles(bundles));
        Assert.Contains(written, Assert.Single(logged));
        Assert.Equal("autobuy-refusal-20260725-134506789.txt", Path.GetFileName(written));
    }

    /// <summary>
    /// A bundle that cannot be written does not stop the latch, and does not throw into the action
    /// that is already going wrong.
    /// </summary>
    [Fact]
    public void AnUnwritableBundleStillTripsTheLatchAndSaysSo()
    {
        var latch = new AutoBuyInvariantLatch();
        var logged = new List<string>();
        var responder = new AutoBuyRefusalResponder(latch, new FailingBundles(), logged.Add, () => WrittenAt);

        responder.Answer(Report(RefusedByAnUnreadableTerm()), Refused);

        Assert.True(latch.IsTripped);
        Assert.Contains("unavailable", Assert.Single(logged));
    }

    /// <summary>
    /// The batch ends on the first refusal anyway, so a second impossible refusal before the latch
    /// clears would only say the same thing twice.
    /// </summary>
    [Fact]
    public void ARefusalWhileTheLatchIsTrippedWritesNoSecondBundle()
    {
        var latch = new AutoBuyInvariantLatch();
        var responder = Responder(latch, out var bundles, out var logged);

        responder.Answer(Report(RefusedByAnUnreadableTerm()), Refused);
        responder.Answer(Report(RefusedByAnUnreadableTerm(), worldGeneration: 5), Refused);

        Assert.Single(logged);
        Assert.Single(Directory.EnumerateFiles(bundles));
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

    /// <summary>
    /// The whole bundle is the plan set against the game and the readings it was pinned to, and
    /// nothing else.
    /// </summary>
    [Fact]
    public void TheBundleIsThePlanAgainstTheGameAndThePinnedReadings()
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

        var text = AutoBuyRefusalBundle.Render(Report(diagnosis), WrittenAt);

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "Auto Buy planned a purchase the game refused.",
                "Written 2026-07-25 13:45:06Z",
                "",
                "Candidate: Upgrade 99a0da45-0000-0000-0000-000000000000",
                "Levels requested: 1",
                "Verdict: refused by GetPurchaseCost().HasEnough()",
                "Classification: AffordabilityChanged",
                "",
                "Live diagnostic terms, read one at a time after the type-specific CanPurchase() refused:",
                "  IsAvailable(): passed",
                "  IsMaxLevel(): passed",
                "  IsMaxQueuedLevel(): passed",
                "  GetPurchaseCost().HasEnough(): REFUSED",
                "",
                "Live cost rows at refusal:",
                "  [1] Resource: abcdef01-0000-0000-0000-000000000000",
                "      IsBandwidth: false",
                "      Cost: 2e1",
                "      Available: 1.9e1 (live TrueQuantity)",
                "  [2] Resource: abcdef02-0000-0000-0000-000000000000",
                "      IsBandwidth: true",
                "      Cost: 4e0",
                "      Available: 3e0 (live room below the ceiling)",
                "",
                "What the plan believed, from the world it was made from:",
                "  IsAvailable: true",
                "  HasFiniteLevels: true",
                "  IsMaxLevel: false",
                "  IsMaxQueuedLevel: false",
                "  CurrentLevel: 1",
                "  QueuedLevels: 0",
                "  Cost rows: 1 resource(s), 1 priced above nought",
                "  CostRatio: 0.25",
                "",
                "The resource that set that ratio:",
                "  Resource: abcdef01-0000-0000-0000-000000000000",
                "  IsBandwidth: false",
                "  Cost: 2e0",
                "  Available: 8e0 (planned spendable TrueQuantity)",
                "  Reserve floor applied: 0e0",
                "",
                "Readings this decision was pinned to:",
                "  World generation: 4",
                "  World collected at epoch: 12",
                "  Config generation: 3",
                "  Lifecycle generation: 2",
                "  Cycle: 41",
                ""),
            text);
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

    private static readonly ServiceActionResult Refused =
        ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected, "refused by the game");

    private AutoBuyRefusalResponder Responder(
        AutoBuyInvariantLatch latch,
        out string bundleDirectory,
        out List<string> logged)
    {
        bundleDirectory = _directory;
        var messages = new List<string>();
        logged = messages;
        var directory = _directory;
        return new AutoBuyRefusalResponder(
            latch,
            new AutoBuyRefusalBundleWriter(() => directory),
            messages.Add,
            () => WrittenAt);
    }

    private static AutoBuyAdmissionDiagnosis RefusedByAnUnreadableTerm() =>
        new(
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Passed);

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
}
