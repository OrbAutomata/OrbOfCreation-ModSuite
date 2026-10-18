using OrbAutomata;
using OrbModding.Common.Runtime;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Runtime.ServiceCycle;

public sealed class AutoHarvestNativeLifecycleTests
{
    [Theory]
    [InlineData(6, 7UL)]
    [InlineData(7, 6UL)]
    [InlineData(long.MaxValue, 9223372036854775808UL)]
    public void NativeLifecycleMustExactlyMatchTheCaptureOrActionCycle(
        long nativeLifecycle,
        ulong plannedLifecycle)
    {
        Assert.False(AutoHarvestNativeLifecycle.Matches(
            nativeLifecycle,
            new LifecycleGeneration(plannedLifecycle)));
    }

    [Fact]
    public void PositiveMatchingLifecycleIsAccepted()
    {
        Assert.True(AutoHarvestNativeLifecycle.Matches(
            7,
            new LifecycleGeneration(7)));
    }
}
