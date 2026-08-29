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

    [Fact]
    public void AByteCountThatDoesNotReproduceIsReportedAsADisturbanceAndNeverAsBytes()
    {
        var passes = 0;
        var sink = Array.Empty<byte>();

        var failure = Assert.Throws<AllocationProbeDisturbedWindowException>(() =>
            AllocationProbe.MeasureRepeated(
                4,
                () =>
                {
                    if (passes == 2) sink = new byte[512];
                },
                prepare: () => passes++));

        Assert.Equal(3, passes);
        Assert.Equal(512, sink.Length);
        Assert.Equal(0, failure.Confirmation);
        Assert.True(
            failure.Measured >= 4 * 512,
            $"A window that allocated 512 bytes four times measured {failure.Measured} bytes.");
        Assert.Equal(
            $"An allocation probe measured {failure.Measured} bytes and then 0 bytes for the same " +
            "warmed work, so it can attribute neither number to the code it measured. Under load " +
            "this runtime charges a thread bytes it never allocated; every such charge observed " +
            "was positive, under 8,192 and a multiple of eight, the shape of an allocation " +
            "quantum's unused remainder. A byte count that does not reproduce is that disturbance " +
            "rather than an allocation, and the probe reports no byte count it could not measure " +
            "twice.",
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
