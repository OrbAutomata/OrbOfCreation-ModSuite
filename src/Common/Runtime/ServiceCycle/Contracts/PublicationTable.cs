using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

/// <summary>
/// Debug-projection view over a publication table. Rows are returned as boxed value copies, never as
/// the backing array or a mutable collection, so projection cannot mutate an already-pinned world.
/// </summary>
internal interface IPublicationTableProjection
{
    int ProjectionCount { get; }

    object ProjectionRow(int index);
}

/// <summary>
/// The one audited bounded container permitted inside an immutable service-cycle publication
/// (configuration, strategy, or action). Publications are otherwise forbidden from carrying arrays
/// or collections because a shared mutable backing store would let one consumer alter a value
/// another consumer has already pinned. This type closes that hole structurally rather than by
/// convention: the backing array is private, is copied from the caller's span at construction so no
/// external alias survives, and is never handed back as an array, collection, or mutable view.
/// </summary>
/// <remarks>
/// The shape is deliberately the same one <c>AutoBuyCycleFrame</c> already relies on — a private
/// array behind read-only accessors — promoted from a per-feature convention into a Common-owned
/// primitive the structural validator can recognize by name. Elements are values, so an element read
/// cannot alias the table, and <typeparamref name="T"/> is still walked under the full rules of the
/// role the table appears in: admitting the container does not admit its contents.
/// <para>
/// The residual gap is that the declaring type could mutate its own private array after publication.
/// C# 10 cannot express deep immutability (see <c>docs/runtime-architecture/architecture.md</c>), so
/// copy-on-construction plus the absence of any mutating member is what makes this safe in practice.
/// </para>
/// </remarks>
[ServiceCyclePublicationValue]
public sealed class PublicationTable<T> : IPublicationTableProjection
    where T : struct
{
    private static readonly T[] NoRows = new T[0];

    /// <summary>The shared empty table. Publications with no rows must not allocate.</summary>
    public static readonly PublicationTable<T> Empty = new(NoRows);

    private readonly T[] _rows;

    private PublicationTable(T[] rows) => _rows = rows;

    /// <summary>
    /// Copies <paramref name="rows"/> into a fresh private array. A span parameter cannot be
    /// retained by the table, and the table's array is never exposed, so the caller keeps no path
    /// to the published storage.
    /// </summary>
    public static PublicationTable<T> Create(ReadOnlySpan<T> rows)
    {
        if (rows.Length == 0) return Empty;
        var copy = new T[rows.Length];
        rows.CopyTo(copy);
        return new PublicationTable<T>(copy);
    }

    /// <summary>Convenience overload for callers holding an array; the contents are copied.</summary>
    public static PublicationTable<T> Create(T[] rows, int count)
    {
        if (rows is null) throw new ArgumentNullException(nameof(rows));
        if ((uint)count > (uint)rows.Length) throw new ArgumentOutOfRangeException(nameof(count));
        return Create(new ReadOnlySpan<T>(rows, 0, count));
    }

    public int Count => _rows.Length;

    /// <summary>Returns row <paramref name="index"/> by value; the caller cannot reach the array.</summary>
    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_rows.Length) throw new ArgumentOutOfRangeException(nameof(index));
            return _rows[index];
        }
    }

    /// <summary>
    /// A read-only view for hot worker scans that must not pay a per-row copy. This is a view over
    /// the private array, not a handle to it: <see cref="ReadOnlySpan{T}"/> cannot write, cannot be
    /// stored in a field, and cannot outlive the synchronous read.
    /// </summary>
    public ReadOnlySpan<T> AsSpan() => new(_rows);

    int IPublicationTableProjection.ProjectionCount => Count;

    object IPublicationTableProjection.ProjectionRow(int index) => this[index];
}
