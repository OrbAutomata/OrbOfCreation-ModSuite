using System;
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.Tests;

/// <summary>
/// The probe every zero-allocation assertion in the suite is measured through, held to the three
/// things that make such an assertion worth anything: it still catches a path that allocates, it
/// never reports a byte count it could not measure twice, and no window it cannot vouch for reaches
/// the caller as bytes.
/// </summary>
[Trait("Category", "PerformanceSimulation")]
public sealed class AllocationProbeTests
{
    [Fact]
    public void WarmedAllocationFreeWorkMeasuresExactlyZero()
    {
        var observed = 0;

        var allocated = AllocationProbe.MeasureRepeated(1_000, () => observed++);

        Assert.Equal(0, allocated);
        Assert.Equal(2_000, observed);
    }

    [Fact]
    public void WarmupDoesNotHideAPathThatGenuinelyAllocates()
    {
        var sink = Array.Empty<byte>();
        var calls = 0;

        var allocated = AllocationProbe.MeasureRepeated(64, () =>
        {
            calls++;
            sink = new byte[64];
        });

        Assert.Equal(64, sink.Length);
        Assert.Equal(192, calls);
        Assert.True(
            allocated >= 64 * 64,
            $"A path allocating 64 bytes 64 times measured {allocated} bytes after warm-up.");
    }

    /// <summary>
    /// One disturbed window is what the runtime hands out under load, and it is not a reason to
    /// fail a gate: the probe opens another window and reports the count two of them agree on.
    /// </summary>
    /// <remarks>
    /// The work here allocates on exactly one pass, which is the shape of the disturbance the probe
    /// exists for — the first window carries bytes the code did not allocate on any other pass, and
    /// the two after it agree.
    /// </remarks>
    [Fact]
    public void OneDisturbedWindowIsMeasuredAgainRatherThanFailed()
    {
        var passes = 0;
        var sink = Array.Empty<byte>();

        var allocated = AllocationProbe.MeasureRepeated(
            4,
            () =>
            {
                if (passes == 2) sink = new byte[512];
            },
            prepare: () => passes++);

        Assert.Equal(0, allocated);
        Assert.Equal(4, passes);
        Assert.Equal(512, sink.Length);
    }

    /// <summary>
    /// A number that never reproduces is still not a number, and the probe reports every window it
    /// opened rather than a byte count it cannot attribute.
    /// </summary>
    [Fact]
    public void AByteCountThatNeverReproducesIsReportedAsADisturbanceAndNeverAsBytes()
    {
        var passes = 0;

        var failure = Assert.Throws<AllocationProbeDisturbedWindowException>(() =>
            AllocationProbe.MeasureRepeated(
                4,
                () => GC.KeepAlive(new byte[512 * passes]),
                prepare: () => passes++));

        Assert.Equal(AllocationProbe.MaximumWindows, failure.Windows.Count);
        Assert.Equal(AllocationProbe.MaximumWindows + 1, passes);
        Assert.Equal(
            failure.Windows.Count,
            new System.Collections.Generic.HashSet<long>(failure.Windows).Count);
        Assert.Equal(
            $"An allocation probe measured {failure.Windows[0]}, {failure.Windows[1]}, " +
            $"{failure.Windows[2]} and {failure.Windows[3]} bytes in 4 windows over the same " +
            "warmed work and no two of them agreed, so it can attribute none of the numbers to " +
            "the code it measured. Under load this runtime charges a thread bytes it never " +
            "allocated; every such charge observed was positive, under 8,192 and a multiple of " +
            "eight, the shape of an allocation quantum's unused remainder. A byte count that does " +
            "not reproduce is that disturbance rather than an allocation, and the probe reports " +
            "no byte count it could not measure twice.",
            failure.Message);
    }

    [Fact]
    public void PreparationRunsOncePerPassAndOutsideTheMeasuredWindow()
    {
        var preparations = 0;
        var sink = Array.Empty<byte>();

        var allocated = AllocationProbe.MeasureRepeated(
            64,
            static () => { },
            prepare: () =>
            {
                preparations++;
                sink = new byte[4096];
            });

        Assert.Equal(2, preparations);
        Assert.Equal(4096, sink.Length);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void FailingWorkPropagatesOnItsFirstCallWithNoSecondAttempt()
    {
        var calls = 0;

        var failure = Assert.Throws<InvalidOperationException>(() =>
            AllocationProbe.MeasureRepeated(64, () =>
            {
                calls++;
                throw new InvalidOperationException("probe subject failed");
            }));

        Assert.Equal("probe subject failed", failure.Message);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void AnEmptyMeasurementWindowIsRejectedRatherThanReportedAsZero()
    {
        var calls = 0;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AllocationProbe.MeasureRepeated(0, () => calls++));

        Assert.Equal(0, calls);
    }
}
