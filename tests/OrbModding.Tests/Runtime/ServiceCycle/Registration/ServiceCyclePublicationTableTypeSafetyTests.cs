using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.TestSupport.ServiceCycleTypeSafetyFixtures;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

/// <summary>
/// <see cref="PublicationTable{T}"/> is the single audited exception to the rule that immutable
/// publications carry no arrays or collections. These tests pin both halves of that bargain: the
/// table is accepted where a bare array is not, and admitting the container does not admit
/// unaudited contents, other containers, or a way back to mutable storage.
/// </summary>
public sealed class ServiceCyclePublicationTableTypeSafetyTests
{
    [Fact]
    public void PublicationTableIsAcceptedInsideAConfigurationWhereABareArrayIsNot()
    {
        // The control: the same rows as a plain array remain rejected.
        AssertConfigurationRejected(new BareArrayConfig(new[] { new StanceRow(1, 2) }));

        var table = new TableConfig(PublicationTable<StanceRow>.Create(
            stackalloc StanceRow[] { new StanceRow(1, 2), new StanceRow(3, 4) }));
        AssertConfigurationAccepted(table);
        Assert.Equal(2, table.Rows.Count);
    }

    [Fact]
    public void PublicationTableRowsAreStillWalkedUnderTheImmutablePublicationRules() =>
        // A row carrying a Unity object is rejected even though the container is audited: the
        // exception admits the table, never its contents.
        AssertConfigurationRejected(new UnsafeRowTableConfig(PublicationTable<UnityBearingRow>.Empty));

    [Fact]
    public void OtherContainersAreNotAdmittedAlongsideThePublicationTable()
    {
        AssertConfigurationRejected(new ListConfig(new List<StanceRow>()));
        AssertConfigurationRejected(new LookalikeConfig(new LookalikeTable<StanceRow>()));
    }

    [Fact]
    public void TheAdmissionAttributeIsHonoredWhereverItIsDeclared()
    {
        // That premise is retired: "declared in Common" becomes true of every type in the suite
        // once it ships as one DLL, so admission is the attribute alone and the review gate
        // moved to ServiceCycleAuditedTypeAllowlistTests, which pins the exact bearer set and fails
        // naming any newcomer. The fixture below is attributed and declared out here, and it is
        // admitted — the runtime no longer asks where a type lives, only whether it was reviewed.
        var declared = new SelfDeclaredConfig(new SelfDeclaredTable());
        AssertConfigurationAccepted(declared);
        Assert.Equal(1, declared.Rows.Count);
    }

    [Fact]
    public void CreateCopiesSoTheCallerRetainsNoPathToPublishedRows()
    {
        var source = new[] { new StanceRow(1, 1), new StanceRow(2, 2) };
        var table = PublicationTable<StanceRow>.Create(source, source.Length);

        source[0] = new StanceRow(99, 99);

        Assert.Equal(1, table[0].ResourceOrdinal);
        Assert.Equal(2, table.Count);
    }

    [Fact]
    public void EmptyTablesShareOneInstanceAndOutOfRangeReadsThrow()
    {
        Assert.Same(PublicationTable<StanceRow>.Empty, PublicationTable<StanceRow>.Create(ReadOnlySpan<StanceRow>.Empty));
        Assert.Same(PublicationTable<StanceRow>.Empty, PublicationTable<StanceRow>.Create(new StanceRow[4], 0));
        Assert.Equal(0, PublicationTable<StanceRow>.Empty.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => PublicationTable<StanceRow>.Empty[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => PublicationTable<StanceRow>.Create(new StanceRow[1], 2));
        Assert.Throws<ArgumentNullException>(() => PublicationTable<StanceRow>.Create(null!, 0));
    }

    [Fact]
    public void SpanAndIndexerAgreeAcrossEveryRow()
    {
        var table = PublicationTable<StanceRow>.Create(
            stackalloc StanceRow[] { new StanceRow(1, 10), new StanceRow(2, 20), new StanceRow(3, 30) });

        var span = table.AsSpan();
        Assert.Equal(table.Count, span.Length);
        for (var index = 0; index < table.Count; index++)
        {
            Assert.Equal(table[index].ResourceOrdinal, span[index].ResourceOrdinal);
            Assert.Equal(table[index].Value, span[index].Value);
        }
    }

    public readonly struct StanceRow
    {
        public StanceRow(int resourceOrdinal, long value)
        {
            ResourceOrdinal = resourceOrdinal;
            Value = value;
        }

        public int ResourceOrdinal { get; }
        public long Value { get; }
    }

    private readonly struct UnityBearingRow
    {
        private readonly UnityEngine.Object? _native;
        internal UnityBearingRow(UnityEngine.Object? native) => _native = native;
    }

    private sealed class TableConfig
    {
        private readonly PublicationTable<StanceRow> _rows;
        internal TableConfig(PublicationTable<StanceRow> rows) => _rows = rows;
        public PublicationTable<StanceRow> Rows => _rows;
    }

    private sealed class UnsafeRowTableConfig
    {
        private readonly PublicationTable<UnityBearingRow> _rows;
        internal UnsafeRowTableConfig(PublicationTable<UnityBearingRow> rows) => _rows = rows;
    }

    private sealed class BareArrayConfig
    {
        private readonly StanceRow[] _rows;
        internal BareArrayConfig(StanceRow[] rows) => _rows = rows;
    }

    private sealed class ListConfig
    {
        private readonly List<StanceRow> _rows;
        internal ListConfig(List<StanceRow> rows) => _rows = rows;
    }

    /// <summary>A hand-rolled table with the same shape; only the exact Common type is admitted.</summary>
    private sealed class LookalikeTable<T>
        where T : struct
    {
        private readonly T[] _rows = new T[0];
        public int Count => _rows.Length;
    }

    private sealed class LookalikeConfig
    {
        private readonly LookalikeTable<StanceRow> _rows;
        internal LookalikeConfig(LookalikeTable<StanceRow> rows) => _rows = rows;
    }

    /// <summary>A type declared outside the runtime, wearing the admission attribute.</summary>
    [ServiceCyclePublicationValue]
    private sealed class SelfDeclaredTable
    {
        private readonly StanceRow[] _rows = new StanceRow[1];
        public int Count => _rows.Length;
    }

    private sealed class SelfDeclaredConfig
    {
        private readonly SelfDeclaredTable _rows;
        internal SelfDeclaredConfig(SelfDeclaredTable rows) => _rows = rows;
        public SelfDeclaredTable Rows => _rows;
    }
}
