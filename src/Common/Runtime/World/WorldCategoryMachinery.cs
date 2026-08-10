using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// Anything that describes one game entity — a raw reading or a published row — carries that
/// entity's identity.
/// </summary>
/// <remarks>
/// <para>
/// The interface exists so one binary search, one traversal, and one report serve every category
/// instead of thirty copies of each. It never appears as storage — publications forbid interface
/// fields, and rightly so. It is only ever a generic constraint, so <c>value.EntityId</c> compiles to
/// a constrained call on a struct: no boxing, no indirection, nothing the structural validator has
/// cause to reject.
/// </para>
/// <para>
/// Implementations must expose <see cref="EntityId"/> publicly, because C# requires that of interface
/// members. That is harmless here: the validator objects to public <em>setters</em> on publication
/// surfaces, not to public reads.
/// </para>
/// </remarks>
internal interface IWorldEntity
{
    /// <summary>The entity's stable UUID, unique across every category in one snapshot.</summary>
    Guid EntityId { get; }
}

/// <summary>Construction of one category's published table, with the invariants lookups depend on.</summary>
internal static class WorldTable
{
    /// <summary>
    /// Sorts <paramref name="rows"/> by identity in place, then publishes a copy of the first
    /// <paramref name="count"/> of them.
    /// </summary>
    /// <remarks>
    /// The two rejections here are the ones a binary search cannot make for itself. An unidentified
    /// row is indistinguishable from an uninitialized one, and a duplicate makes a lookup return an
    /// arbitrary member of the pair — silently, and differently depending on table size. Both are
    /// authoring errors, so they surface at construction where the offending row is still in hand,
    /// rather than as a wrong purchase several cycles later.
    /// </remarks>
    internal static PublicationTable<TRow> Create<TRow>(TRow[] rows, int count)
        where TRow : struct, IWorldEntity
    {
        if (rows is null) throw new ArgumentNullException(nameof(rows));
        if (count == 0) return PublicationTable<TRow>.Empty;

        Array.Sort(rows, 0, count, WorldRowComparer<TRow>.ById);

        for (var index = 0; index < count; index++)
        {
            var id = rows[index].EntityId;
            if (id == Guid.Empty)
                throw new ArgumentException("A world row must carry a non-empty entity identity.", nameof(rows));

            // Duplicates are adjacent once sorted, so this costs one comparison per row rather than a
            // second hash set.
            if (index > 0 && rows[index - 1].EntityId == id)
                throw new ArgumentException($"Entity {id} was sampled more than once.", nameof(rows));
        }

        return PublicationTable<TRow>.Create(rows, count);
    }

    /// <summary>Convenience for fixtures and single-shot construction.</summary>
    internal static PublicationTable<TRow> Create<TRow>(params TRow[] rows)
        where TRow : struct, IWorldEntity =>
        Create(rows, rows?.Length ?? throw new ArgumentNullException(nameof(rows)));
}

/// <summary>
/// Holds one comparison delegate per row type. A generic static field is instantiated once per
/// closed type, so sorting never allocates a comparer or a closure.
/// </summary>
internal static class WorldRowComparer<TRow>
    where TRow : struct, IWorldEntity
{
    internal static readonly IComparer<TRow> ById = new IdComparer();

    private sealed class IdComparer : IComparer<TRow>
    {
        public int Compare(TRow left, TRow right) => left.EntityId.CompareTo(right.EntityId);
    }
}

/// <summary>Identity lookup over a sorted category table.</summary>
internal static class WorldLookup
{
    /// <summary>
    /// Binary search by entity identity. Rows are sorted at build time, so this is logarithmic rather
    /// than the linear probe a per-candidate scan would cost — with hundreds of entities across
    /// thirty categories, consulted by every service every cycle, that difference is the whole reason
    /// the tables are sorted at all.
    /// </summary>
    internal static bool TryFind<TRow>(PublicationTable<TRow> table, Guid entityId, out TRow row)
        where TRow : struct, IWorldEntity
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = rows[middle].EntityId.CompareTo(entityId);
            if (comparison == 0)
            {
                row = rows[middle];
                return true;
            }

            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        row = default;
        return false;
    }
}

/// <summary>
/// Resolves one category's native accessors and turns one game object into one raw reading.
/// </summary>
/// <remarks>
/// <para>
/// This is the only per-category code that has to be written by hand. Traversal, identity claiming,
/// per-entity failure handling, buffer growth, sorting, and table construction are all generic — so
/// adding a category is a row type and a binder, not another copy of the collector.
/// </para>
/// <para>
/// The sample and row types stay separate even where they hold the same fields, because the boundary
/// they mark is real: <see cref="Read"/> runs on the Unity thread and may do nothing but read, while
/// <see cref="Derive"/> runs on a worker and may compute freely. Collapsing them would make it
/// invisible when a derivation quietly migrated onto the main thread.
/// </para>
/// </remarks>
internal abstract class WorldRowBinder<TSample, TRow>
    where TSample : struct, IWorldEntity
    where TRow : struct, IWorldEntity
{
    /// <summary>Short human name used in reports, e.g. "resources".</summary>
    internal abstract string Category { get; }

    /// <summary>The game type to resolve by name, e.g. "ResourceSO".</summary>
    internal abstract string TypeName { get; }

    /// <summary>The static registry member holding every instance. Every category the game ships uses "All".</summary>
    internal virtual string RegistryMember => "All";

    /// <summary>
    /// Compiles the accessors this binder needs. Returns an empty string on success, or a message
    /// naming the members that could not be bound.
    /// </summary>
    internal abstract string Bind(Type type);

    /// <summary>Reads one entity. Runs on the Unity thread and must not write game state.</summary>
    internal abstract TSample Read(object entity);

    /// <summary>
    /// The pure half of this category, as a separate object so the worker can hold it without
    /// holding this binder.
    /// </summary>
    /// <remarks>
    /// Derivation used to be a method here, which made "the worker never touches a native accessor" a
    /// convention rather than a fact: a worker holding the binder in order to derive would also hold
    /// <see cref="Read"/> and every compiled game accessor behind it, and nothing but care would stop
    /// it being called. Splitting the two means the worker is handed a type that has no native
    /// surface to reach for.
    /// </remarks>
}

/// <summary>Turns one raw reading into a published row. Runs off-thread; pure and total.</summary>
internal abstract class WorldRowDeriver<TSample, TRow>
    where TSample : struct, IWorldEntity
    where TRow : struct, IWorldEntity
{
    internal abstract TRow Derive(in TSample sample);
}

/// <summary>
/// The derivation of a category that needs none: the reading is already the row.
/// </summary>
/// <remarks>
/// One shared instance per closed row type. A generic static is instantiated once per closed type, so
/// the thirty categories that derive nothing cost thirty references and no allocation per cycle.
/// </remarks>
internal sealed class WorldIdentityDeriver<TRow> : WorldRowDeriver<TRow, TRow>
    where TRow : struct, IWorldEntity
{
    internal static readonly WorldIdentityDeriver<TRow> Shared = new();

    private WorldIdentityDeriver()
    {
    }

    internal override TRow Derive(in TRow sample) => sample;
}

/// <summary>
/// A category whose reading needs no derivation: the value the game holds is already the fact worth
/// publishing.
/// </summary>
/// <remarks>
/// Most categories are like this — a level, a discovery flag, a mastery count. Keeping a separate
/// sample type for them would double the code to express that nothing happens between the two, so
/// they publish one struct and the boundary is marked by which method touches it: <c>Read</c> runs on
/// the Unity thread, and derivation is the identity.
/// </remarks>
internal abstract class WorldPlainBinder<TRow> : WorldRowBinder<TRow, TRow>
    where TRow : struct, IWorldEntity
{
}

/// <summary>
/// One category's readings, held where a service cycle can own them: in the frame.
/// </summary>
/// <remarks>
/// <para>
/// The buffer lives here rather than inside the reader because the reader holds compiled game
/// accessors, and a service frame may not hold delegates — the structural validator rejects them, and
/// rightly, since a frame crosses to a worker. Separating the storage from the machinery that fills
/// it is what lets the same readings be captured on the Unity thread and derived off it.
/// </para>
/// <para>
/// Reused across cycles and never resized down, so a steady-state cycle allocates nothing. That is
/// the same bargain every service frame makes, and it is sound for the same reason: one frame belongs
/// to one half-duplex cycle, so no two passes are ever writing here at once.
/// </para>
/// </remarks>
internal sealed class WorldSampleBuffer<TSample, TRow>
    where TSample : struct, IWorldEntity
    where TRow : struct, IWorldEntity
{
    private const int InitialCapacity = 32;

    private TSample[] _samples = new TSample[InitialCapacity];

    /// <summary>
    /// Where derivation writes before the published table copies out of it. Reused so that the copy
    /// is the only array a cycle leaves behind; see W1.
    /// </summary>
    private TRow[] _derived = new TRow[InitialCapacity];

    private int _count;

    internal int Count => _count;

    internal ref readonly TSample this[int index] => ref _samples[index];

    internal void Reset() => _count = 0;

    internal void Append(in TSample sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }

    /// <summary>
    /// Derives every reading into a sorted, published table. The worker half: no game access, and the
    /// sort is what makes <see cref="WorldLookup.TryFind{TRow}"/> a binary search.
    /// </summary>
    /// <remarks>
    /// Derives into scratch this buffer keeps, so the only array that outlives the cycle is the one
    /// <see cref="PublicationTable{T}.Create(T[], int)"/> copies into. Deriving into a fresh array
    /// each time would allocate twice per category per cycle for no benefit: the intermediate is
    /// sorted in place and then copied out of, and nothing ever sees it.
    /// </remarks>
    internal PublicationTable<TRow> Build(WorldRowDeriver<TSample, TRow> deriver)
    {
        if (deriver is null) throw new ArgumentNullException(nameof(deriver));
        if (_count == 0) return PublicationTable<TRow>.Empty;

        if (_derived.Length < _count) _derived = new TRow[_samples.Length];
        for (var index = 0; index < _count; index++) _derived[index] = deriver.Derive(in _samples[index]);

        return WorldTable.Create(_derived, _count);
    }
}

/// <summary>
/// Binds one category's members and remembers which ones failed, so a binder states each member name
/// exactly once.
/// </summary>
/// <remarks>
/// The earlier shape had every binder repeat its member names — once to bind, once in a list handed
/// to the failure message. That second list is pure duplication and drifts silently: a renamed
/// binding still reports the old name, so the diagnostic points at the wrong member precisely when
/// someone is relying on it to find the right one. Recording the name at the moment it is used
/// removes the possibility.
/// </remarks>
internal sealed class WorldMemberBinding
{
    private readonly Type _type;
    private readonly string _typeName;
    private readonly Failures _failures;

    /// <summary>
    /// Reads the object every accessor here is rooted at, or <see langword="null"/> when that object
    /// is the entity itself. Set only by <see cref="Through"/>.
    /// </summary>
    private readonly Func<object, object?>? _root;

    /// <summary>
    /// Whether this binding sits below the entity, so a failure has to say which level it is at. Two
    /// nested types both missing a <c>phase</c> field would otherwise report "phase, phase".
    /// </summary>
    private readonly bool _nested;

    internal WorldMemberBinding(Type type, string typeName)
        : this(type, typeName, new Failures(), root: null, nested: false)
    {
    }

    private WorldMemberBinding(
        Type type,
        string typeName,
        Failures failures,
        Func<object, object?>? root,
        bool nested)
    {
        _type = type;
        _typeName = typeName;
        _failures = failures;
        _root = root;
        _nested = nested;
    }

    /// <summary>
    /// Binds against an object held in a field rather than against the entity, so a type that owns
    /// another entity outright can be read without a second copy of that entity's member list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>HarvestElementSO</c> is why this exists. Its <c>harvestResource</c> is a full
    /// <c>ResourceSO</c> the element creates for itself and never registers, so the resource registry
    /// cannot reach it and the only way to it is through its owner.
    /// </para>
    /// <para>
    /// Failures are recorded against the shared parent, prefixed with the field, so one message names
    /// every member that did not bind whichever level it sits at. A field that is absent or is not a
    /// reference type yields a binding that fails every member rather than a null one, which keeps
    /// the caller's code free of a second failure shape.
    /// </para>
    /// <para>
    /// A null object at read time yields the member's default. The element creates its resource in
    /// <c>ResetData()</c>, so the field is only null before the game has initialised — a state the
    /// suite reads as "nothing yet" everywhere else too.
    /// </para>
    /// </remarks>
    internal WorldMemberBinding Through(string fieldName)
    {
        var field = _type?.GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (field is null || field.FieldType.IsValueType)
        {
            _failures.Add(fieldName);
            return new WorldMemberBinding(
                null!, $"{_typeName}.{fieldName}", _failures, root: null, nested: true);
        }

        var read = NativeAccessorBinder.Reference(_type, fieldName);
        if (read is null)
        {
            _failures.Add(fieldName);
            return new WorldMemberBinding(
                null!, $"{_typeName}.{fieldName}", _failures, root: null, nested: true);
        }

        var outer = _root;
        Func<object, object?> composed = outer is null
            ? read
            : source =>
            {
                var owner = outer(source);
                return owner is null ? null : read(owner);
            };

        return new WorldMemberBinding(
            field.FieldType, $"{_typeName}.{fieldName}", _failures, composed, nested: true);
    }

    /// <summary>
    /// Binds against the elements of a collection rather than against the entity, recording failures
    /// against the same parent so one message still names every member that did not bind.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Through"/> there is no root to compose: a collection's elements are reached
    /// by the caller iterating the list, so the accessors this yields take an element directly. A
    /// null element type — an absent or non-generic field — yields a binding that fails every member,
    /// which is the same shape <see cref="Through"/> gives a missing field.
    /// </remarks>
    internal WorldMemberBinding Elements(Type? elementType, string elementName) =>
        new(elementType!, $"{elementName}", _failures, root: null, nested: true);

    /// <summary>Empty when every member bound, otherwise a message naming the ones that did not.</summary>
    internal string Failure =>
        _failures.IsEmpty ? string.Empty : $"{_typeName} did not expose {_failures} on this build";

    internal Func<object, TValue>? Field<TValue>(string name) =>
        Record(name, NativeAccessorBinder.Field<TValue>(_type, name));

    internal Func<object, TValue>? Call<TValue>(string name) =>
        Record(name, NativeAccessorBinder.Call<TValue>(_type, name));

    internal Func<object, object?>? CallObject(string name, Type? exactReturnType) =>
        Record(name, NativeAccessorBinder.CallObject(_type, name, exactReturnType));

    internal Func<object, IList?>? CallList(string name, Type? exactElementType) =>
        Record(name, NativeAccessorBinder.CallList(_type, name, exactElementType));

    internal Func<object, TArgument, TValue>? Call<TArgument, TValue>(string name) =>
        Record(name, NativeAccessorBinder.Call<TArgument, TValue>(_type, name));

    internal Func<object, TFirst, TSecond, TValue>? Call<TFirst, TSecond, TValue>(string name) =>
        Record(name, NativeAccessorBinder.Call<TFirst, TSecond, TValue>(_type, name));

    internal Func<object, TArgument, object?>? CallObject<TArgument>(
        string name,
        Type? exactReturnType) =>
        Record(name, NativeAccessorBinder.CallObject<TArgument>(
            _type, name, exactReturnType));

    internal Func<object, TFirst, TSecond, object?>? CallObject<TFirst, TSecond>(
        string name,
        Type? exactReturnType) =>
        Record(name, NativeAccessorBinder.CallObject<TFirst, TSecond>(
            _type, name, exactReturnType));

    internal Func<object, object, TValue>? CallWithObjectArgument<TValue>(
        string name,
        Type? argumentType) =>
        Record(name, NativeAccessorBinder.CallWithObjectArgument<TValue>(
            _type, name, argumentType));

    internal Func<object, int>? EnumField(string name) =>
        Record(name, NativeAccessorBinder.EnumField(_type, name));

    /// <summary>
    /// Binds how many elements a collection field holds. The elements stay behind; the count is a
    /// scalar and is usually the fact a consumer wanted anyway.
    /// </summary>
    internal Func<object, int>? CollectionCount(string name) =>
        Record(name, NativeAccessorBinder.CollectionCount(_type, name));

    /// <summary>Binds a collection field as the list itself, for the few categories that need one.</summary>
    internal Func<object, IList?>? CollectionField(string name) =>
        Record(name, NativeAccessorBinder.CollectionField(_type, name));

    /// <summary>The element type of a collection field, for binding accessors onto its entries.</summary>
    internal Type? CollectionElementType(string name) =>
        NativeAccessorBinder.CollectionElementType(_type, name);

    /// <summary>
    /// Binds a field inside a field. Reported against the nested name, because the outer field is
    /// usually the one that still exists.
    /// </summary>
    internal Func<object, TValue>? NestedField<TValue>(string fieldName, string nestedName) =>
        Record(
            $"{fieldName}.{nestedName}",
            NativeAccessorBinder.NestedField<TValue>(_type, fieldName, nestedName));

    /// <summary>
    /// Binds a modifier record as the value the game would compute for it, folded from its base
    /// value and modifier sets rather than read out of its cache. See
    /// <see cref="NativeAccessorBinder.ModifierRecord"/>.
    /// </summary>
    /// <remarks>
    /// Reported against <c>field.baseValue</c> when it does not bind, because the base value is the
    /// input whose absence means the record cannot be folded at all — and because naming the field
    /// alone would read as "the record is gone" when usually it is one member inside it.
    /// </remarks>
    internal Func<object, BigDouble>? ModifierRecord(string fieldName) =>
        Record($"{fieldName}.baseValue", NativeAccessorBinder.ModifierRecord(_type, fieldName));

    /// <summary>Binds an enum field inside a field, as its underlying integer.</summary>
    internal Func<object, int>? NestedEnumField(string fieldName, string nestedName) =>
        Record(
            $"{fieldName}.{nestedName}",
            NativeAccessorBinder.NestedEnumField(_type, fieldName, nestedName));

    /// <summary>Binds an edge to another entity as that entity's identity. See D17.</summary>
    internal Func<object, Guid>? ReferenceGuid(string name) =>
        Record(name, NativeAccessorBinder.ReferenceGuid(_type, name));

    internal Func<object, object?>? Reference(string name, Type? exactFieldType) =>
        Record(name, NativeAccessorBinder.Reference(_type, name, exactFieldType));

    /// <summary>The same edge, where the game exposes it as an accessor rather than as a field.</summary>
    internal Func<object, Guid>? CallReferenceGuid(string name) =>
        Record(name, NativeAccessorBinder.CallReferenceGuid(_type, name));

    /// <summary>Binds the count of a collection held inside a field — a modifier record's active set.</summary>
    internal Func<object, int>? NestedCollectionCount(string fieldName, string nestedName) =>
        Record(
            $"{fieldName}.{nestedName}",
            NativeAccessorBinder.NestedCollectionCount(_type, fieldName, nestedName));

    private Func<object, TValue>? Record<TValue>(string name, Func<object, TValue>? accessor)
    {
        if (accessor is null)
        {
            _failures.Add(Qualify(name));
            return null;
        }

        var root = _root;
        if (root is null) return accessor;

        // Rooted at a field: read the owner first, and answer the member's default when it is absent.
        return source =>
        {
            var owner = root(source);
            return owner is null ? default! : accessor(owner);
        };
    }

    private Func<object, TArgument, TValue>? Record<TArgument, TValue>(
        string name,
        Func<object, TArgument, TValue>? accessor)
    {
        if (accessor is null)
        {
            _failures.Add(Qualify(name));
            return null;
        }

        var root = _root;
        if (root is null) return accessor;

        return (source, argument) =>
        {
            var owner = root(source);
            return owner is null ? default! : accessor(owner, argument);
        };
    }

    private Func<object, TFirst, TSecond, TValue>? Record<TFirst, TSecond, TValue>(
        string name,
        Func<object, TFirst, TSecond, TValue>? accessor)
    {
        if (accessor is null)
        {
            _failures.Add(Qualify(name));
            return null;
        }

        var root = _root;
        if (root is null) return accessor;

        return (source, first, second) =>
        {
            var owner = root(source);
            return owner is null ? default! : accessor(owner, first, second);
        };
    }

    private string Qualify(string name) => _nested ? $"{_typeName}.{name}" : name;

    /// <summary>
    /// The member names that did not bind, shared by a binding and everything derived from it with
    /// <see cref="Through"/> so one <see cref="Failure"/> covers every level.
    /// </summary>
    private sealed class Failures
    {
        private StringBuilder? _missing;

        internal bool IsEmpty => _missing is null;

        internal void Add(string name)
        {
            _missing ??= new StringBuilder();
            if (_missing.Length > 0) _missing.Append(", ");
            _missing.Append(name);
        }

        public override string ToString() => _missing?.ToString() ?? string.Empty;
    }
}

/// <summary>
/// The non-generic face of a category, so the collector can hold thirty differently-typed readers in
/// one list. Interfaces are free here: a collector is not a publication.
/// </summary>
internal interface IWorldCategoryReader
{
    string Category { get; }

    /// <summary>Whether this category's accessors resolved on the running build.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Walks the category's registry, reading every entity it can into <paramref name="frame"/>.
    /// Identities are claimed against <paramref name="claimed"/>, which spans every category, because
    /// the game keys all entities in one UUID space.
    /// </summary>
    WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame);
}

/// <summary>
/// Generic traversal for one category: registry walk, identity claiming, per-entity failure capture,
/// and buffer management.
/// </summary>
/// <remarks>
/// Buffers are reused across collections and grow by doubling. Safety comes from single ownership —
/// one reader belongs to one collector belongs to one half-duplex cycle — the same guarantee that
/// makes a service's cycle frame reusable.
/// </remarks>
internal sealed class WorldCategoryReader<TSample, TRow> : IWorldCategoryReader
    where TSample : struct, IWorldEntity
    where TRow : struct, IWorldEntity
{
    private readonly WorldRowBinder<TSample, TRow> _binder;
    private readonly Type? _nativeType;
    private readonly string _unavailable;

    /// <summary>
    /// Finds this category's readings inside whichever frame is being filled. Held as a selector
    /// rather than as the buffer itself so one reader serves every frame the runtime creates, which
    /// matters because binding compiles an accessor per member and is far too expensive to repeat per
    /// frame.
    /// </summary>
    private readonly Func<GameWorldCycleFrame, WorldSampleBuffer<TSample, TRow>> _buffer;

    /// <summary>
    /// Reads this category's registry. Resolved once with the rest of the binding rather than looked
    /// up per pass: the member does not move within a lifecycle, and rediscovering it four times a
    /// second for every category is a fixed cost paid for nothing.
    /// </summary>
    private readonly Func<IList?>? _registry;

    internal WorldCategoryReader(
        WorldRowBinder<TSample, TRow> binder,
        Type? nativeType,
        Func<GameWorldCycleFrame, WorldSampleBuffer<TSample, TRow>> buffer)
    {
        _binder = binder ?? throw new ArgumentNullException(nameof(binder));
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _nativeType = nativeType;
        _unavailable = nativeType is null
            ? $"the {binder.TypeName} type was not found on this build"
            : binder.Bind(nativeType);
        _registry = NativeAccessorBinder.StaticListAccessor(nativeType, binder.RegistryMember);
    }

    public string Category => _binder.Category;

    public bool IsAvailable => _nativeType is not null && _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        var buffer = _buffer(frame);
        buffer.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var entities = _registry?.Invoke();
        if (entities is null)
        {
            return WorldCategoryReport.Missing(
                Category, $"the {_binder.TypeName} registry was unreadable");
        }

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;

        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            if (entity is null)
            {
                Skip(ref skipped, ref firstFailure, "a registry entry was null");
                continue;
            }

            try
            {
                // The identity comes off the sample, never off a derived row: derivation belongs to
                // the worker, and calling it here to learn a Guid would quietly move it back onto the
                // Unity thread — and would run it twice per entity into the bargain.
                var sample = _binder.Read(entity);
                var id = sample.EntityId;

                if (id == Guid.Empty)
                {
                    Skip(ref skipped, ref firstFailure, "an entity carried an empty identity");
                    continue;
                }

                if (!claimed.Add(id))
                {
                    Skip(ref skipped, ref firstFailure, $"entity {id} appeared more than once");
                    continue;
                }

                buffer.Append(in sample);
                sampled++;
            }
            catch (Exception ex)
            {
                Skip(
                    ref skipped,
                    ref firstFailure,
                    $"reading a {_binder.TypeName} threw: {ex.GetBaseException().Message}");
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }

    private static void Skip(ref int skipped, ref string firstFailure, string reason)
    {
        skipped++;
        if (firstFailure.Length == 0) firstFailure = reason;
    }
}
