using System;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

public sealed class AutoBuyPurchaseNarrationTests
{
    private static readonly Guid CandidateId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void DescribeWarning_CandidateUnavailable_IsActionable()
    {
        var submission = AutoBuyPurchaseSubmission.Rejected(AutoBuyPurchasePreflight.CandidateUnavailable);

        var warning = AutoBuyPurchaseNarration.DescribeWarning(
            AutoBuyCandidateKind.Structure, CandidateId, in submission);

        Assert.Equal(
            $"Auto Buy failed to purchase Structure {CandidateId:D}: candidate could not be resolved.",
            warning);
    }

    [Fact]
    public void DescribeWarning_AffordabilityUnavailable_IsActionable()
    {
        var submission = AutoBuyPurchaseSubmission.Rejected(
            AutoBuyPurchasePreflight.AffordabilityUnavailable);

        var warning = AutoBuyPurchaseNarration.DescribeWarning(
            AutoBuyCandidateKind.Structure, CandidateId, in submission);

        Assert.Equal(
            $"Auto Buy failed to purchase Structure {CandidateId:D}: live affordability could not be read.",
            warning);
    }

    [Fact]
    public void DescribeWarning_SuccessAndOrdinaryRefusalsAreSilent()
    {
        var verified = Attempted(before: 4, delta: 3, requested: 3);
        var unavailable = AutoBuyPurchaseSubmission.Rejected(
            AutoBuyPurchasePreflight.SingleBuyUnavailable);
        var refusalOwnedByResponder = AutoBuyPurchaseSubmission.Rejected(
            AutoBuyPurchasePreflight.NotAdmissible);

        Assert.Null(AutoBuyPurchaseNarration.DescribeWarning(
            AutoBuyCandidateKind.Upgrade, CandidateId, in verified));
        Assert.Null(AutoBuyPurchaseNarration.DescribeWarning(
            AutoBuyCandidateKind.Upgrade, CandidateId, in unavailable));
        Assert.Null(AutoBuyPurchaseNarration.DescribeWarning(
            AutoBuyCandidateKind.Upgrade, CandidateId, in refusalOwnedByResponder));
    }

    [Fact]
    public void DescribeWarning_UnverifiedPostcondition_IsActionable()
    {
        var unverified = Attempted(before: 4, delta: 0, requested: 3);

        var warning = AutoBuyPurchaseNarration.DescribeWarning(
            AutoBuyCandidateKind.Upgrade, CandidateId, in unverified);

        Assert.Equal(
            $"Auto Buy failed to purchase 3 levels for Upgrade {CandidateId:D}: native mutation did not apply.",
            warning);
    }

    [Fact]
    public void QueueRoomUnavailable_IsActionable()
    {
        var warning = AutoBuyPurchaseNarration.QueueRoomUnavailable(
            AutoBuyCandidateKind.Structure, CandidateId);

        Assert.Equal(
            $"Auto Buy failed to purchase Structure {CandidateId:D}: queue room unavailable.",
            warning);
    }

    /// <summary>
    /// The publication line names the run it belongs to and how much it admits.
    /// </summary>
    /// <remarks>
    /// Both numbers are the point. A topology stamped under the wrong run and one stamped correctly
    /// that admits nothing refuse every purchase identically, and the log has to tell an operator
    /// which of the two happened without a source reading.
    /// </remarks>
    [Fact]
    public void TopologyPublished_NamesTheRunAndWhatItAdmits()
    {
        Assert.Equal(
            "Auto Buy purchase-screen topology published for run 7: 42 candidate(s) admitted.",
            AutoBuyPurchaseNarration.TopologyPublished(7, 42));
    }

    private static AutoBuyPurchaseSubmission Attempted(int before, int delta, int requested)
    {
        var queued = before;
        var evidence = NativeMutationVerifier.Execute(
            "Auto Buy Upgrade",
            CandidateId.ToString(),
            $"delta in [1, {requested}]",
            () => queued,
            () => queued += delta,
            (start, end) => end > start && end <= start + requested);
        return AutoBuyPurchaseSubmission.Attempted(evidence, requested);
    }
}
