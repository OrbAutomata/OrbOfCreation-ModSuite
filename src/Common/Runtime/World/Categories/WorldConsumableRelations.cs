using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One native family assigned to a consumable. A consumable may belong to more than one family, so
/// this is a relation rather than a flag on <see cref="WorldConsumable"/>.
/// </summary>
internal readonly struct WorldConsumableType
{
    internal WorldConsumableType(Guid consumableId, Guid typeId)
    {
        ConsumableId = consumableId;
        TypeId = typeId;
    }

    internal Guid ConsumableId { get; }
    internal Guid TypeId { get; }
}

internal static class WorldConsumableTypeLookup
{
    internal static bool TryFindRange(
        PublicationTable<WorldConsumableType> table,
        Guid consumableId,
        out int start,
        out int count)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (rows[middle].ConsumableId.CompareTo(consumableId) < 0) low = middle + 1;
            else high = middle - 1;
        }

        start = low;
        count = 0;
        while (start + count < rows.Length &&
               rows[start + count].ConsumableId == consumableId)
        {
            count++;
        }
        return count > 0;
    }
}

internal enum WorldConsumableCostKind
{
    Consume = 0,
    Usage = 1,
}

internal readonly struct WorldConsumableCost
{
    internal WorldConsumableCost(
        Guid consumableId,
        WorldConsumableCostKind kind,
        Guid resourceId,
        BigDouble amount)
    {
        ConsumableId = consumableId;
        Kind = kind;
        ResourceId = resourceId;
        Amount = amount;
    }

    internal Guid ConsumableId { get; }
    internal WorldConsumableCostKind Kind { get; }
    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
}

internal static class WorldConsumableCostLookup
{
    internal static bool TryFindRange(
        PublicationTable<WorldConsumableCost> table,
        Guid consumableId,
        WorldConsumableCostKind kind,
        out int start,
        out int count)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison =
                Compare(rows[middle].ConsumableId, rows[middle].Kind, consumableId, kind);
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        start = low;
        count = 0;
        while (start + count < rows.Length &&
               Compare(
                   rows[start + count].ConsumableId,
                   rows[start + count].Kind,
                   consumableId,
                   kind) == 0)
        {
            count++;
        }
        return count > 0;
    }

    private static int Compare(
        Guid leftConsumable,
        WorldConsumableCostKind leftKind,
        Guid rightConsumable,
        WorldConsumableCostKind rightKind)
    {
        var byConsumable = leftConsumable.CompareTo(rightConsumable);
        return byConsumable != 0
            ? byConsumable
            : ((int)leftKind).CompareTo((int)rightKind);
    }
}

/// <summary>
/// One native usage owned by a consumable. Pending usages are created at submission and become
/// engaged only after preparation and effect execution begin.
/// </summary>
internal readonly struct WorldConsumableUsage
{
    internal WorldConsumableUsage(
        Guid consumableId,
        Guid usageId,
        int level,
        bool engaged,
        BigDouble remainingDuration,
        BigDouble maximumDuration)
    {
        ConsumableId = consumableId;
        UsageId = usageId;
        Level = level;
        Engaged = engaged;
        RemainingDuration = remainingDuration;
        MaximumDuration = maximumDuration;
    }

    internal Guid ConsumableId { get; }
    internal Guid UsageId { get; }
    internal int Level { get; }
    internal bool Engaged { get; }
    internal bool Pending => !Engaged;
    internal BigDouble RemainingDuration { get; }
    internal BigDouble MaximumDuration { get; }
    internal bool Expired =>
        Engaged && RemainingDuration.CompareTo(BigDouble.Zero) <= 0;
}

internal static class WorldConsumableUsageLookup
{
    internal static bool TryFindRange(
        PublicationTable<WorldConsumableUsage> table,
        Guid consumableId,
        out int start,
        out int count)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (rows[middle].ConsumableId.CompareTo(consumableId) < 0) low = middle + 1;
            else high = middle - 1;
        }

        start = low;
        count = 0;
        while (start + count < rows.Length &&
               rows[start + count].ConsumableId == consumableId)
        {
            count++;
        }
        return count > 0;
    }
}

/// <summary>One levelled inventory bucket owned by a consumable.</summary>
internal readonly struct WorldConsumableCount
{
    internal WorldConsumableCount(Guid consumableId, int level, int quantity, int freeQuantity)
    {
        ConsumableId = consumableId;
        Level = level;
        Quantity = quantity;
        FreeQuantity = freeQuantity;
    }

    internal Guid ConsumableId { get; }
    internal int Level { get; }
    internal int Quantity { get; }
    internal int FreeQuantity { get; }
}

internal static class WorldConsumableCountLookup
{
    internal static bool TryFindRange(
        PublicationTable<WorldConsumableCount> table,
        Guid consumableId,
        out int start,
        out int count)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (rows[middle].ConsumableId.CompareTo(consumableId) < 0) low = middle + 1;
            else high = middle - 1;
        }

        start = low;
        count = 0;
        while (start + count < rows.Length &&
               rows[start + count].ConsumableId == consumableId)
        {
            count++;
        }
        return count > 0;
    }

    internal static bool TryGetStrongestOwnedLevel(
        PublicationTable<WorldConsumableCount> table,
        Guid consumableId,
        out int level)
    {
        level = 0;
        if (!TryFindRange(table, consumableId, out var start, out var count)) return false;
        for (var index = 0; index < count; index++)
        {
            var row = table[start + index];
            if (row.Quantity > 0 && row.Level > level) level = row.Level;
        }
        return level > 0;
    }

    internal static int CountAtOrAbove(
        PublicationTable<WorldConsumableCount> table,
        Guid consumableId,
        int level)
    {
        if (!TryFindRange(table, consumableId, out var start, out var count)) return 0;
        var total = 0;
        for (var index = 0; index < count; index++)
        {
            var row = table[start + index];
            if (row.Level >= level && row.Quantity > 0)
                total = checked(total + row.Quantity);
        }
        return total;
    }
}

internal sealed class WorldConsumableTypeBuffer
{
    private WorldConsumableType[] _samples = new WorldConsumableType[32];
    private int _count;

    internal int Count => _count;
    internal ref readonly WorldConsumableType this[int index] => ref _samples[index];
    internal void Reset() => _count = 0;

    internal void Append(in WorldConsumableType sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

internal sealed class WorldConsumableCostBuffer
{
    private WorldConsumableCost[] _samples = new WorldConsumableCost[32];
    private int _count;

    internal int Count => _count;
    internal ref readonly WorldConsumableCost this[int index] => ref _samples[index];
    internal void Reset() => _count = 0;

    internal void Append(in WorldConsumableCost sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

internal sealed class WorldConsumableUsageBuffer
{
    private WorldConsumableUsage[] _samples = new WorldConsumableUsage[16];
    private int _count;

    internal int Count => _count;
    internal ref readonly WorldConsumableUsage this[int index] => ref _samples[index];
    internal void Reset() => _count = 0;

    internal void Append(in WorldConsumableUsage sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

internal sealed class WorldConsumableCountBuffer
{
    private WorldConsumableCount[] _samples = new WorldConsumableCount[32];
    private int _count;

    internal int Count => _count;
    internal ref readonly WorldConsumableCount this[int index] => ref _samples[index];
    internal void Reset() => _count = 0;

    internal void Append(in WorldConsumableCount sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

internal static class WorldConsumableRelationDeriver
{
    internal static PublicationTable<WorldConsumableType> Build(WorldConsumableTypeBuffer buffer)
    {
        if (buffer.Count == 0) return PublicationTable<WorldConsumableType>.Empty;
        var rows = new WorldConsumableType[buffer.Count];
        var count = 0;
        for (var index = 0; index < buffer.Count; index++)
        {
            var row = buffer[index];
            if (row.ConsumableId == Guid.Empty || row.TypeId == Guid.Empty) continue;
            rows[count++] = row;
        }
        Array.Sort(rows, 0, count, ConsumableTypeComparer.Instance);
        return PublicationTable<WorldConsumableType>.Create(rows, count);
    }

    internal static PublicationTable<WorldConsumableCost> Build(WorldConsumableCostBuffer buffer)
    {
        if (buffer.Count == 0) return PublicationTable<WorldConsumableCost>.Empty;
        var rows = new WorldConsumableCost[buffer.Count];
        var count = 0;
        for (var index = 0; index < buffer.Count; index++)
        {
            var row = buffer[index];
            if (row.ConsumableId == Guid.Empty || row.ResourceId == Guid.Empty) continue;
            rows[count++] = row;
        }
        Array.Sort(rows, 0, count, ConsumableCostComparer.Instance);
        return PublicationTable<WorldConsumableCost>.Create(rows, count);
    }

    internal static PublicationTable<WorldConsumableUsage> Build(WorldConsumableUsageBuffer buffer)
    {
        if (buffer.Count == 0) return PublicationTable<WorldConsumableUsage>.Empty;
        var rows = new WorldConsumableUsage[buffer.Count];
        var identities = new HashSet<ConsumableUsageKey>();
        var count = 0;
        for (var index = 0; index < buffer.Count; index++)
        {
            var row = buffer[index];
            if (row.UsageId == Guid.Empty || row.Level <= 0 ||
                !identities.Add(new ConsumableUsageKey(row.ConsumableId, row.UsageId)))
                continue;
            rows[count++] = row;
        }
        Array.Sort(rows, 0, count, ConsumableUsageComparer.Instance);
        return PublicationTable<WorldConsumableUsage>.Create(rows, count);
    }

    internal static PublicationTable<WorldConsumableCount> Build(WorldConsumableCountBuffer buffer)
    {
        if (buffer.Count == 0) return PublicationTable<WorldConsumableCount>.Empty;
        var rows = new WorldConsumableCount[buffer.Count];
        var count = 0;
        for (var index = 0; index < buffer.Count; index++)
        {
            var row = buffer[index];
            if (row.ConsumableId == Guid.Empty || row.Level <= 0 ||
                row.Quantity < 0 || row.FreeQuantity < 0)
                continue;
            rows[count++] = row;
        }
        Array.Sort(rows, 0, count, ConsumableCountComparer.Instance);
        return PublicationTable<WorldConsumableCount>.Create(rows, count);
    }

    private sealed class ConsumableTypeComparer : IComparer<WorldConsumableType>
    {
        internal static readonly IComparer<WorldConsumableType> Instance =
            new ConsumableTypeComparer();
        public int Compare(WorldConsumableType left, WorldConsumableType right)
        {
            var byConsumable = left.ConsumableId.CompareTo(right.ConsumableId);
            return byConsumable != 0 ? byConsumable : left.TypeId.CompareTo(right.TypeId);
        }
    }

    private sealed class ConsumableCostComparer : IComparer<WorldConsumableCost>
    {
        internal static readonly IComparer<WorldConsumableCost> Instance =
            new ConsumableCostComparer();
        public int Compare(WorldConsumableCost left, WorldConsumableCost right)
        {
            var byConsumable = left.ConsumableId.CompareTo(right.ConsumableId);
            if (byConsumable != 0) return byConsumable;
            var byKind = ((int)left.Kind).CompareTo((int)right.Kind);
            return byKind != 0 ? byKind : left.ResourceId.CompareTo(right.ResourceId);
        }
    }

    private sealed class ConsumableUsageComparer : IComparer<WorldConsumableUsage>
    {
        internal static readonly IComparer<WorldConsumableUsage> Instance =
            new ConsumableUsageComparer();
        public int Compare(WorldConsumableUsage left, WorldConsumableUsage right)
        {
            var byConsumable = left.ConsumableId.CompareTo(right.ConsumableId);
            return byConsumable != 0 ? byConsumable : left.UsageId.CompareTo(right.UsageId);
        }
    }

    private readonly struct ConsumableUsageKey : IEquatable<ConsumableUsageKey>
    {
        internal ConsumableUsageKey(Guid consumableId, Guid usageId)
        {
            ConsumableId = consumableId;
            UsageId = usageId;
        }

        private Guid ConsumableId { get; }
        private Guid UsageId { get; }
        public bool Equals(ConsumableUsageKey other) =>
            ConsumableId == other.ConsumableId && UsageId == other.UsageId;
        public override bool Equals(object? value) =>
            value is ConsumableUsageKey other && Equals(other);
        public override int GetHashCode() =>
            unchecked((ConsumableId.GetHashCode() * 397) ^ UsageId.GetHashCode());
    }

    private sealed class ConsumableCountComparer : IComparer<WorldConsumableCount>
    {
        internal static readonly IComparer<WorldConsumableCount> Instance =
            new ConsumableCountComparer();
        public int Compare(WorldConsumableCount left, WorldConsumableCount right)
        {
            var byConsumable = left.ConsumableId.CompareTo(right.ConsumableId);
            return byConsumable != 0 ? byConsumable : left.Level.CompareTo(right.Level);
        }
    }
}

/// <summary>
/// Reads consumable scalars and relations in one pass. A row is published only when its family,
/// costs, usages, and levelled counts are all structurally coherent.
/// </summary>
internal sealed class WorldConsumableReader : IWorldCategoryReader
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Type? _nativeType;
    private readonly Type? _registryType;
    private readonly Type? _consumableTypeType;
    private readonly WorldConsumableBinder _binder = new();
    private readonly string _unavailable;
    private readonly Func<object, IList?>? _types;
    private readonly Func<object, Guid>? _typeId;
    private readonly Func<object, IList?>? _consumeCosts;
    private readonly Func<object, IList?>? _usageCosts;
    private readonly Func<object, Guid>? _costResourceId;
    private readonly Func<object, BigDouble>? _costAmount;
    private readonly Func<object, IList?>? _usages;
    private readonly Type? _usageType;
    private readonly Func<object, Guid>? _usageId;
    private readonly Func<object, bool>? _usageEngaged;
    private readonly Func<object, BigDouble>? _usageRemainingDuration;
    private readonly Func<object, BigDouble>? _usageMaximumDuration;
    private readonly Func<object, int>? _usageLevel;
    private readonly Func<object, IList?>? _counts;
    private readonly Type? _countType;
    private readonly Func<object, int>? _countLevel;
    private readonly Func<object, int>? _countQuantity;
    private readonly Func<object, int>? _countFreeQuantity;
    private readonly Func<object, Guid>? _maximumCarryLoadVariableId;

    private static readonly Guid GlobalConsumableTypeId =
        new("315471ca-0d15-455d-92da-f9d5f95a3c33");

    internal WorldConsumableReader(Type? nativeType, Type? registryType)
    {
        _nativeType = nativeType;
        _registryType = registryType;
        if (nativeType is null)
        {
            _unavailable = "the ConsumableSO type was not found on this build";
            return;
        }

        var scalarFailure = _binder.Bind(nativeType);
        var bind = new WorldMemberBinding(nativeType, "ConsumableSO");
        _types = bind.CollectionField("consumableTypes");
        var typeEntry = bind.CollectionElementType("consumableTypes");
        _consumableTypeType = typeEntry;
        _typeId = bind.Elements(typeEntry, "ConsumableSO.consumableTypes[]").Call<Guid>("GetGuid");
        _maximumCarryLoadVariableId = bind
            .Elements(typeEntry, "GlobalConsumableType")
            .ReferenceGuid("maximumCarryLoad");

        _consumeCosts = bind.Through("consumeCost").CollectionField("costs");
        _usageCosts = bind.Through("usageCost").CollectionField("costs");
        var consumeCostType = nativeType.GetField("consumeCost", Instance)?.FieldType;
        var usageCostType = nativeType.GetField("usageCost", Instance)?.FieldType;
        var costEntry = NativeAccessorBinder.CollectionElementType(consumeCostType, "costs");
        var costBind = bind.Elements(costEntry, "ResourceCostList.costs[]");
        _costResourceId = costBind.ReferenceGuid("resource");
        _costAmount = costBind.Field<BigDouble>("valueBig");

        _usages = bind.CollectionField("consumableUsages");
        _usageType = bind.CollectionElementType("consumableUsages");
        var usageBind = bind.Elements(_usageType, "ConsumableSO.consumableUsages[]");
        _usageId = usageBind.Call<Guid>("GetGuid");
        _usageEngaged = usageBind.Field<bool>("en");
        _usageRemainingDuration = usageBind.Field<BigDouble>("dr");
        _usageMaximumDuration = usageBind.Field<BigDouble>("maxDr");
        _usageLevel = usageBind.Through("baseSi").Call<int>("GetLevelInt");

        _counts = bind.CollectionField("consumableCounts");
        _countType = bind.CollectionElementType("consumableCounts");
        var countBind = bind.Elements(_countType, "ConsumableSO.consumableCounts[]");
        _countLevel = countBind.Call<int>("GetLevel");
        _countQuantity = countBind.Call<int>("GetQuantity");
        _countFreeQuantity = countBind.Field<int>("fr");

        var relationFailure = bind.Failure;
        if (consumeCostType != usageCostType)
        {
            relationFailure = AppendFailure(
                relationFailure,
                "ConsumableSO consumeCost and usageCost did not share one native type");
        }
        _unavailable = AppendFailure(scalarFailure, relationFailure);
    }

    public string Category => "consumables";
    public bool IsAvailable =>
        _nativeType is not null && _registryType is not null && _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        frame.Consumables.Reset();
        frame.ConsumableTypes.Reset();
        frame.ConsumableCosts.Reset();
        frame.ConsumableUsages.Reset();
        frame.ConsumableCounts.Reset();
        frame.ConsumableMaximumCarryLoadVariableId = Guid.Empty;
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var entities = NativeAccessorBinder.StaticList(_nativeType, "All");
        if (entities is null)
            return WorldCategoryReport.Missing(Category, "the ConsumableSO registry was unreadable");
        if (entities.Count == 0)
            return new WorldCategoryReport(
                Category, WorldCategoryOutcome.Collected, 0, 0, string.Empty);

        var registry = NativeAccessorBinder.StaticDictionary(_registryType, "RuntimeLookup");
        var globalType = registry?[GlobalConsumableTypeId];
        if (globalType is null || globalType.GetType() != _consumableTypeType)
            return WorldCategoryReport.Missing(
                Category, "the global Consumable type edge was unreadable");
        frame.ConsumableMaximumCarryLoadVariableId = _maximumCarryLoadVariableId!(globalType);
        if (frame.ConsumableMaximumCarryLoadVariableId == Guid.Empty)
            return WorldCategoryReport.Missing(
                Category, "the global maximum carry-load variable carried no identity");

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;
        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            if (entity is null || entity.GetType() != _nativeType)
            {
                Skip(ref skipped, ref firstFailure, "a registry entry had an unexpected native type");
                continue;
            }

            try
            {
                var sample = _binder.Read(entity);
                var id = sample.EntityId;
                if (id == Guid.Empty)
                {
                    Skip(ref skipped, ref firstFailure, "a consumable carried an empty identity");
                    continue;
                }
                if (!claimed.Add(id))
                {
                    Skip(ref skipped, ref firstFailure, $"entity {id} appeared more than once");
                    continue;
                }

                var types = _types!(entity);
                var consumeCosts = _consumeCosts!(entity);
                var usageCosts = _usageCosts!(entity);
                var usages = _usages!(entity);
                var counts = _counts!(entity);
                var typesValid = ValidateTypes(types, out var typeFailure);
                var consumeCostsValid = ValidateCosts(consumeCosts, out var consumeFailure);
                var usageCostsValid = ValidateCosts(usageCosts, out var usageFailure);
                var usagesValid = ValidateUsages(usages, out var usagesFailure);
                var countsValid = ValidateCounts(counts, out var countsFailure);
                if (!typesValid || !consumeCostsValid || !usageCostsValid || !usagesValid ||
                    !countsValid)
                {
                    Skip(
                        ref skipped,
                        ref firstFailure,
                        FirstFailure(
                            typeFailure,
                            consumeFailure,
                            usageFailure,
                            usagesFailure,
                            countsFailure));
                    continue;
                }

                frame.Consumables.Append(in sample);
                AppendTypes(id, types!, frame.ConsumableTypes);
                AppendCosts(
                    id,
                    WorldConsumableCostKind.Consume,
                    consumeCosts!,
                    frame.ConsumableCosts);
                AppendCosts(
                    id,
                    WorldConsumableCostKind.Usage,
                    usageCosts!,
                    frame.ConsumableCosts);
                AppendUsages(id, usages!, frame.ConsumableUsages);
                AppendCounts(id, counts!, frame.ConsumableCounts);
                sampled++;
            }
            catch (Exception ex)
            {
                Skip(
                    ref skipped,
                    ref firstFailure,
                    $"reading a ConsumableSO threw: {ex.GetBaseException().Message}");
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }

    private bool ValidateTypes(IList? entries, out string failure)
    {
        if (entries is null)
        {
            failure = "a consumable's type list was null";
            return false;
        }
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null)
            {
                failure = $"a consumable type at position {index} was null or unidentified";
                return false;
            }
        }
        failure = string.Empty;
        return true;
    }

    private bool ValidateCosts(IList? entries, out string failure)
    {
        if (entries is null)
        {
            failure = "a consumable cost list was null";
            return false;
        }
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null)
            {
                failure = $"a consumable cost at position {index} was null or unidentified";
                return false;
            }
        }
        failure = string.Empty;
        return true;
    }

    private bool ValidateUsages(IList? entries, out string failure)
    {
        if (entries is null)
        {
            failure = "a consumable's usage list was null";
            return false;
        }
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null || entry.GetType() != _usageType)
            {
                failure = $"a consumable usage at position {index} had an unexpected type";
                return false;
            }
            var id = _usageId!(entry);
            if (id == Guid.Empty || _usageLevel!(entry) <= 0)
            {
                failure = $"a consumable usage at position {index} had invalid identity or level";
                return false;
            }
            for (var prior = 0; prior < index; prior++)
            {
                if (_usageId!(entries[prior]!) != id) continue;
                failure = $"a consumable usage repeated identity {EntityIdentityFormatter.Format(id)}";
                return false;
            }
        }
        failure = string.Empty;
        return true;
    }

    private bool ValidateCounts(IList? entries, out string failure)
    {
        if (entries is null)
        {
            failure = "a consumable's levelled count list was null";
            return false;
        }
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null || entry.GetType() != _countType)
            {
                failure = $"a consumable count at position {index} carried invalid values";
                return false;
            }
        }
        failure = string.Empty;
        return true;
    }

    private void AppendTypes(
        Guid consumableId,
        IList entries,
        WorldConsumableTypeBuffer destination)
    {
        for (var index = 0; index < entries.Count; index++)
            destination.Append(new WorldConsumableType(consumableId, _typeId!(entries[index]!)));
    }

    private void AppendCosts(
        Guid consumableId,
        WorldConsumableCostKind kind,
        IList entries,
        WorldConsumableCostBuffer destination)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index]!;
            destination.Append(new WorldConsumableCost(
                consumableId,
                kind,
                _costResourceId!(entry),
                _costAmount!(entry)));
        }
    }

    private void AppendUsages(
        Guid consumableId,
        IList entries,
        WorldConsumableUsageBuffer destination)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index]!;
            destination.Append(new WorldConsumableUsage(
                consumableId,
                _usageId!(entry),
                _usageLevel!(entry),
                _usageEngaged!(entry),
                _usageRemainingDuration!(entry),
                _usageMaximumDuration!(entry)));
        }
    }

    private void AppendCounts(
        Guid consumableId,
        IList entries,
        WorldConsumableCountBuffer destination)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index]!;
            destination.Append(new WorldConsumableCount(
                consumableId,
                _countLevel!(entry),
                _countQuantity!(entry),
                _countFreeQuantity!(entry)));
        }
    }

    private static string AppendFailure(string first, string second)
    {
        if (first.Length == 0) return second;
        if (second.Length == 0) return first;
        return $"{first}; {second}";
    }

    private static string FirstFailure(params string[] failures)
    {
        for (var index = 0; index < failures.Length; index++)
        {
            if (failures[index].Length != 0) return failures[index];
        }
        return "a consumable relation was unreadable";
    }

    private static void Skip(ref int skipped, ref string firstFailure, string reason)
    {
        skipped++;
        if (firstFailure.Length == 0) firstFailure = reason;
    }
}
