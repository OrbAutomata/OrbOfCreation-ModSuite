using System;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

public sealed class WorldKeyedLookupTests
{
    [Theory]
    [InlineData(0, 0, 0, false)]
    [InlineData(1, 0, 2, true)]
    [InlineData(2, 2, 0, false)]
    [InlineData(3, 2, 1, true)]
    [InlineData(4, 3, 0, false)]
    [InlineData(5, 3, 2, true)]
    [InlineData(6, 5, 0, false)]
    public void RangeSearchFindsWholeRunsAndInsertionPoints(int key, int expectedStart, int expectedCount, bool found)
    {
        ReadOnlySpan<int> rows = new[] { 1, 1, 3, 5, 5 };
        Assert.Equal(found, WorldLookup.TryFindRange(rows, key,
            static (row, value) => row.CompareTo(value), out var start, out var count));
        Assert.Equal(expectedStart, start);
        Assert.Equal(expectedCount, count);
        Assert.Equal(expectedStart, WorldLookup.LowerBound(rows, key,
            static (row, value) => row.CompareTo(value)));
    }

    [Fact]
    public void EmptyTableHasNoRangeOrFirstRow()
    {
        Assert.False(WorldLookup.TryFindRange(ReadOnlySpan<int>.Empty, 1,
            static (row, key) => row.CompareTo(key), out var start, out var count));
        Assert.Equal(0, start);
        Assert.Equal(0, count);
        Assert.False(WorldLookup.TryFindFirst(ReadOnlySpan<int>.Empty, 1,
            static (row, key) => row.CompareTo(key), out _));
    }

    [Fact]
    public void CompositePrefixMatchDoesNotAdmitAnotherSecondKey()
    {
        ReadOnlySpan<(int Owner, int Kind)> rows = new[] { (1, 1), (1, 3), (2, 1) };
        Assert.False(WorldLookup.TryFindFirst(rows, (Owner: 1, Kind: 2),
            static (row, key) => row.Owner != key.Owner
                ? row.Owner.CompareTo(key.Owner) : row.Kind.CompareTo(key.Kind), out _));
        Assert.True(WorldLookup.TryFindFirst(rows, (Owner: 1, Kind: 3),
            static (row, key) => row.Owner != key.Owner
                ? row.Owner.CompareTo(key.Owner) : row.Kind.CompareTo(key.Kind), out var row));
        Assert.Equal((1, 3), row);
    }
}
