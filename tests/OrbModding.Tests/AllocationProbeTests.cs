using System;
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.Tests;

/// <summary>
/// The probe every zero-allocation assertion in the suite is measured through, held to the two
/// things that make such an assertion worth anything: it still catches a path that allocates, and it
/// has no way to try again when it does.
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

        var allocated = AllocationProbe.MeasureRepeated(64, () => sink = new byte[64]);

        Assert.Equal(64, sink.Length);
        Assert.True(
            allocated >= 64 * 64,
            $"A path allocating 64 bytes 64 times measured {allocated} bytes after warm-up.");
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
