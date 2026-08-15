using System;
using OrbAutomata;
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Native;

public sealed class AutoHarvestStableIdAccessorTests
{
    [Fact]
    public void BoundAccessorReadsOnlyTheValidatedExactType()
    {
        var expected = Guid.NewGuid();
        var accessor = AutoHarvestStableIdAccessor.Bind(typeof(StableEntity));

        Assert.True(accessor.TryRead(new StableEntity(expected), out var actual));
        Assert.Equal(expected, actual);
        Assert.False(accessor.TryRead(new ForeignEntity(expected), out _));
    }

    [Fact]
    public void WarmedNativeIdentityReadsDoNotAllocate()
    {
        var entity = new StableEntity(Guid.NewGuid());
        var accessor = AutoHarvestStableIdAccessor.Bind(typeof(StableEntity));
        Assert.True(accessor.TryRead(entity, out _));

        var allocated = AllocationProbe.MeasureRepeated(
            1_000,
            () => accessor.TryRead(entity, out _));

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void BindingFailsWithoutAStableIdentityMember()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AutoHarvestStableIdAccessor.Bind(typeof(object)));
    }

    private sealed class StableEntity : IdScriptableObject
    {
        public StableEntity(Guid value) => SetGuid(value);
    }

    private sealed class ForeignEntity : IdScriptableObject
    {
        public ForeignEntity(Guid value) => SetGuid(value);
    }
}
