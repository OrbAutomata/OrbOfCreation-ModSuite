using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

public sealed class AutoBuyPurchaseNarrationTests
{
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
