using System;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

public sealed class AutoBuyPurchaseNarrationTests
{
    private static readonly Guid CandidateId = Guid.Parse("11111111-2222-3333-4444-555555555555");

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

}
