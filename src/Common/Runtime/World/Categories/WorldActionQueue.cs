using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One of the game's action queues as read: how many slots it holds, how many are in use, and
/// whether the game's own occupancy answer agrees with the slots that were walked.
/// </summary>
/// <remarks>
/// <para>
/// A queue is an entity — the list variable carries its own uuid — so this is a normal identity-keyed
/// table. What it is <em>not</em> is an admission oracle: a reading taken at collection time is stale
/// within a generation, because a consumer's own submissions eat the room it reports. The queue is
/// published so a worker can shape a plan; the action boundary stays the authority on whether one
/// more action fits.
/// </para>
/// <para>
/// The capacity half of the attribute queue is already published: its maximum is an
/// <c>IntVariable</c>, and that registry is collected whole. The row carries the edge to it rather
/// than a copy of the number, so the link is the one the game states.
/// </para>
/// </remarks>
internal readonly struct WorldActionQueue : IWorldEntity
{
    internal WorldActionQueue(
        Guid queueId,
        Guid maxQueuedItemsId,
        int slotCount,
        int usedSlots,
        int emptySlots,
        bool hasEmptySlot,
        bool consistent)
    {
        QueueId = queueId;
        MaxQueuedItemsId = maxQueuedItemsId;
        SlotCount = slotCount;
        UsedSlots = usedSlots;
        EmptySlots = emptySlots;
        HasEmptySlot = hasEmptySlot;
        Consistent = consistent;
    }

    public Guid EntityId => QueueId;

    internal Guid QueueId { get; }

    /// <summary>
    /// The <c>IntVariable</c> holding how many entries the queue admits, or <see cref="Guid.Empty"/>
    /// for a queue whose capacity is the length of its own list rather than a variable.
    /// </summary>
    internal Guid MaxQueuedItemsId { get; }

    /// <summary>
    /// How many entries the queue's own list holds, occupied or not. That is the capacity of a queue
    /// with a fixed slot count; for one that grows against <see cref="MaxQueuedItemsId"/> it is only
    /// how long the list currently is.
    /// </summary>
    internal int SlotCount { get; }

    /// <summary>Occupancy derived from the queue list captured in the same pass.</summary>
    internal int UsedSlots { get; }

    /// <summary>
    /// How many slots are free: how many answered <c>IsEmpty()</c> where the slots are walked, and
    /// the rest of the list otherwise.
    /// </summary>
    internal int EmptySlots { get; }

    /// <summary>Whether the captured slots or the collected maximum leave room.</summary>
    internal bool HasEmptySlot { get; }

    /// <summary>
    /// Whether the captured list shape is internally coherent. Retained in the published shape for
    /// compatibility; the derived occupancy and emptiness now share one raw source.
    /// </summary>
    internal bool Consistent { get; }
}

/// <summary>Raw queue shape; occupancy is derived from the same list walk that produced it.</summary>
internal readonly struct RawWorldActionQueue : IWorldEntity
{
    internal RawWorldActionQueue(
        Guid queueId,
        Guid maxQueuedItemsId,
        int slotCount,
        int emptySlots,
        bool slotsWereWalked)
    {
        QueueId = queueId;
        MaxQueuedItemsId = maxQueuedItemsId;
        SlotCount = slotCount;
        EmptySlots = emptySlots;
        SlotsWereWalked = slotsWereWalked;
    }

    public Guid EntityId => QueueId;
    internal Guid QueueId { get; }
    internal Guid MaxQueuedItemsId { get; }
    internal int SlotCount { get; }
    internal int EmptySlots { get; }
    internal bool SlotsWereWalked { get; }
}

internal sealed class WorldActionQueueDeriver : WorldRowDeriver<RawWorldActionQueue, WorldActionQueue>
{
    private readonly PublicationTable<WorldNumberVariable> _intVariables;

    internal WorldActionQueueDeriver(PublicationTable<WorldNumberVariable> intVariables) =>
        _intVariables = intVariables;

    internal override WorldActionQueue Derive(in RawWorldActionQueue sample)
    {
        var used = sample.SlotsWereWalked
            ? sample.SlotCount - sample.EmptySlots
            : sample.SlotCount;
        var hasEmpty = sample.SlotsWereWalked
            ? sample.EmptySlots > 0
            : !WorldLookup.TryFind(_intVariables, sample.MaxQueuedItemsId, out var maximum) ||
              used < maximum.Value.ToInt();
        return new WorldActionQueue(
            sample.QueueId,
            sample.MaxQueuedItemsId,
            sample.SlotCount,
            used,
            sample.EmptySlots,
            hasEmpty,
            used >= 0 && used <= sample.SlotCount);
    }
}

/// <summary>One slot of one queue, and what is running in it.</summary>
/// <remarks>
/// <para>
/// A slot has no identity: it is a position in a list, and the same position holds a different action
/// a moment later. It is keyed by its queue and its index, which is why it is its own table rather
/// than rows on <see cref="WorldActionQueue"/>, and why the identity walk skips it. The full list of
/// skipped tables is <c>NotIdentityTables</c> in <c>WorldIdentityWalkTests</c>; note that
/// <see cref="WorldActionQueue"/> itself is not on it — a queue carries a uuid of its own, so it is
/// walked like any other entity.
/// </para>
/// <para>
/// Only the plot-action queue produces slots. Its occupants are the pairs Auto Harvest already reads
/// one by one at its action boundary; a queue whose occupancy is effectively an integer has nothing
/// per-slot worth publishing, and inventing it would be describing a shape nobody has asked the game
/// about.
/// </para>
/// </remarks>
internal readonly struct WorldActionQueueSlot
{
    internal WorldActionQueueSlot(
        Guid queueId,
        int index,
        bool empty,
        Guid plotNodeId,
        Guid plotNodeActionId,
        int quantity,
        bool engaged)
    {
        QueueId = queueId;
        Index = index;
        Empty = empty;
        PlotNodeId = plotNodeId;
        PlotNodeActionId = plotNodeActionId;
        Quantity = quantity;
        Engaged = engaged;
    }

    internal Guid QueueId { get; }

    /// <summary>The slot's position in its queue's list.</summary>
    internal int Index { get; }

    internal bool Empty { get; }

    /// <summary>Which plot the slot runs on, or <see cref="Guid.Empty"/> when it is empty.</summary>
    internal Guid PlotNodeId { get; }

    /// <summary>Which action the slot runs, or <see cref="Guid.Empty"/> when it is empty.</summary>
    internal Guid PlotNodeActionId { get; }

    /// <summary>How much of the action the slot is running.</summary>
    internal int Quantity { get; }

    /// <summary>Whether the slot's action is under way rather than merely occupying the slot.</summary>
    internal bool Engaged { get; }
}

/// <summary>
/// Range lookup over the queue-slot table, which is sorted by queue and then position.
/// </summary>
/// <remarks>
/// A slot has no identity, so <see cref="WorldLookup"/> cannot reach one. What a consumer asks is
/// "what is in this queue", and a queue's slots are contiguous, so a binary search for its first row
/// plus a forward walk answers that in <c>O(log n + k)</c> — the shape purchase costs already use for
/// the same reason.
/// </remarks>
internal static class WorldActionQueueSlotLookup
{
    /// <summary>
    /// The half-open row range belonging to <paramref name="queueId"/>. Both indices are zero when
    /// the queue published no slots, which is the honest reading of a queue whose slots are not
    /// collected — not a queue with none.
    /// </summary>
    internal static bool TryFindRange(
        PublicationTable<WorldActionQueueSlot> table,
        Guid queueId,
        out int start,
        out int count)
    {
        start = 0;
        count = 0;

        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = rows[middle].QueueId.CompareTo(queueId);
            if (comparison == 0)
            {
                // Keep going left: the search must land on the queue's *first* slot, or the forward
                // walk below starts in the middle of the queue and reports a partial reading.
                found = middle;
                high = middle - 1;
                continue;
            }

            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        if (found < 0) return false;

        start = found;
        while (start + count < rows.Length && rows[start + count].QueueId == queueId) count++;
        return true;
    }
}

/// <summary>Every queue slot as read, held where a cycle can own them.</summary>
internal sealed class WorldActionQueueSlotBuffer
{
    private const int InitialCapacity = 32;

    private WorldActionQueueSlot[] _samples = new WorldActionQueueSlot[InitialCapacity];
    private int _count;

    internal int Count => _count;

    internal ref readonly WorldActionQueueSlot this[int index] => ref _samples[index];

    internal void Reset() => _count = 0;

    internal void Append(in WorldActionQueueSlot sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

/// <summary>
/// Publishes the slot readings, sorted by queue and then position, so one queue's slots are
/// contiguous and in the order the game holds them.
/// </summary>
internal static class WorldActionQueueSlotDeriver
{
    internal static PublicationTable<WorldActionQueueSlot> Build(WorldActionQueueSlotBuffer buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Count == 0) return PublicationTable<WorldActionQueueSlot>.Empty;

        var derived = new WorldActionQueueSlot[buffer.Count];
        for (var index = 0; index < buffer.Count; index++) derived[index] = buffer[index];

        Array.Sort(derived, 0, derived.Length, SlotComparer.ByQueueThenIndex);
        return PublicationTable<WorldActionQueueSlot>.Create(derived, derived.Length);
    }

    private sealed class SlotComparer : IComparer<WorldActionQueueSlot>
    {
        internal static readonly IComparer<WorldActionQueueSlot> ByQueueThenIndex = new SlotComparer();

        public int Compare(WorldActionQueueSlot left, WorldActionQueueSlot right)
        {
            var byQueue = left.QueueId.CompareTo(right.QueueId);
            return byQueue != 0 ? byQueue : left.Index.CompareTo(right.Index);
        }
    }
}

/// <summary>
/// Reads the game's action queues.
/// </summary>
/// <remarks>
/// <para>
/// Not a registry walk. A list variable's <c>All</c> is declared on its generic base, so the concrete
/// queue type has no registry of its own; the queues are reached by uuid through the identity
/// registry every other lookup in the suite already goes through, which also avoids the action
/// manager singleton entirely.
/// </para>
/// <para>
/// The two queues are read differently on purpose. The plot-action queue holds a fixed row of slots
/// whose occupants Auto Harvest already reads one at a time, so every slot is published; the
/// attribute queue Auto Buy competes for answers occupancy and names the variable holding its
/// maximum, and nothing else about it is asked, because nothing else about it is known.
/// </para>
/// </remarks>
internal sealed class WorldActionQueueReader : IWorldCategoryReader
{
    private readonly Type? _registryType;
    private readonly RuntimeIdentityRegistryBinding _registryBinding;
    private readonly Type? _slotQueueType;
    private readonly Type? _occupancyQueueType;
    private readonly Type? _slotType;
    private readonly string _unavailable;

    private readonly Func<object, Guid>? _slotQueueId;
    private readonly Func<object, IList?>? _slotQueueSlots;
    private readonly Func<object, bool>? _slotEmpty;
    private readonly Func<object, bool>? _slotEngaged;
    private readonly Func<object, int>? _slotQuantity;
    private readonly Func<object, Guid>? _slotPlotId;
    private readonly Func<object, Guid>? _slotActionId;

    private readonly Func<object, Guid>? _occupancyQueueId;
    private readonly Func<object, IList?>? _occupancyQueueEntries;
    private readonly Func<object, Guid>? _occupancyQueueMaximumId;

    internal WorldActionQueueReader(
        Type? registryType,
        Type? slotQueueType,
        Type? occupancyQueueType)
    {
        _registryType = registryType;
        _registryBinding = new RuntimeIdentityRegistryBinding(
            () => registryType, requireStableIdentityContract: false);
        _slotQueueType = slotQueueType;
        _occupancyQueueType = occupancyQueueType;
        if (registryType is null)
        {
            _unavailable = "the IdScriptableObject type was not found on this build";
            return;
        }

        if (slotQueueType is null)
        {
            _unavailable = "the PlotNodeActionInstanceListVariable type was not found on this build";
            return;
        }

        if (occupancyQueueType is null)
        {
            _unavailable = "the ActionableListVariable type was not found on this build";
            return;
        }

        var plotActions = new WorldMemberBinding(slotQueueType, "PlotNodeActionInstanceListVariable");
        _slotQueueId = plotActions.Call<Guid>("GetGuid");
        _slotQueueSlots = plotActions.CollectionField("value");

        _slotType = plotActions.CollectionElementType("value");
        var slot = plotActions.Elements(_slotType, "PlotNodeActionInstance");
        _slotEmpty = slot.Call<bool>("IsEmpty");
        _slotEngaged = slot.Call<bool>("IsEngaged");
        _slotQuantity = slot.Call<int>("GetActualQuantity");
        _slotPlotId = slot.CallReferenceGuid("GetElement");
        _slotActionId = slot.CallReferenceGuid("GetAction");

        var actionables = new WorldMemberBinding(occupancyQueueType, "ActionableListVariable");
        _occupancyQueueId = actionables.Call<Guid>("GetGuid");
        _occupancyQueueEntries = actionables.CollectionField("value");
        _occupancyQueueMaximumId = actionables.ReferenceGuid("maxQueuedItems");

        _unavailable = plotActions.Failure.Length != 0 ? plotActions.Failure : actionables.Failure;
    }

    public string Category => "action queues";

    public bool IsAvailable =>
        _registryType is not null &&
        _slotQueueType is not null &&
        _occupancyQueueType is not null &&
        _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        var queues = frame.ActionQueues;
        var slots = frame.ActionQueueSlots;
        queues.Reset();
        slots.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var source = _registryBinding.Read();
        if (!source.IsReady || source.Registry is null)
            return WorldCategoryReport.Missing(Category, source.Reason);
        var registry = source.Registry;

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;

        // A queue the registry does not hold yet is a fact about the save, not about the read: the
        // game registers its list variables during initialisation, and a pass before that has
        // nothing to report rather than a shortfall to report.
        var plotActions = registry[KnownEntities.ActivePlotNodeActions.Uuid];
        if (plotActions is not null)
        {
            try
            {
                Record(
                    ReadSlotted(plotActions, claimed, queues, slots),
                    "plot-action",
                    ref sampled,
                    ref skipped,
                    ref firstFailure);
            }
            catch (Exception ex)
            {
                Skip(ref skipped, ref firstFailure, Threw("plot-action", ex));
            }
        }

        var actionables = registry[KnownEntities.ActiveActionables.Uuid];
        if (actionables is not null)
        {
            try
            {
                Record(
                    ReadOccupancy(actionables, claimed, queues),
                    "attribute",
                    ref sampled,
                    ref skipped,
                    ref firstFailure);
            }
            catch (Exception ex)
            {
                Skip(ref skipped, ref firstFailure, Threw("attribute", ex));
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }

    private static void Record(
        string failure,
        string name,
        ref int sampled,
        ref int skipped,
        ref string firstFailure)
    {
        if (failure.Length == 0) sampled++;
        else Skip(ref skipped, ref firstFailure, $"the {name} queue {failure}");
    }

    private static string Threw(string name, Exception ex) =>
        $"reading the {name} queue threw: {ex.GetBaseException().Message}";

    /// <summary>
    /// Reads the queue whose slots are published one by one, and rejects a reading it cannot make
    /// sense of rather than publishing half of one.
    /// </summary>
    /// <remarks>
    /// The rejection is the action boundary's own, ported: an entry of another type is not a slot
    /// this reader can read, and every accessor here is bound against the type it is not. A hole is
    /// different — it is a slot nothing is running in, which is what an empty slot is — and a game
    /// that counts it as used says so through <c>Consistent</c> rather than by losing the reading.
    /// </remarks>
    private string ReadSlotted(
        object queue,
        HashSet<Guid> claimed,
        WorldSampleBuffer<RawWorldActionQueue, WorldActionQueue> queues,
        WorldActionQueueSlotBuffer slots)
    {
        var queueId = _slotQueueId!(queue);
        if (queueId == Guid.Empty) return "carried no identity";

        var values = _slotQueueSlots!(queue);
        var slotCount = values?.Count ?? 0;
        for (var index = 0; index < slotCount; index++)
        {
            var entry = values![index];
            if (entry is not null && entry.GetType() != _slotType)
                return "held an entry that is not a plot-action instance";
        }

        if (!claimed.Add(queueId)) return $"identity {queueId} appeared more than once";

        var empty = 0;
        for (var index = 0; index < slotCount; index++)
        {
            var instance = values![index];
            if (instance is null || _slotEmpty!(instance))
            {
                empty++;
                slots.Append(new WorldActionQueueSlot(
                    queueId, index, true, Guid.Empty, Guid.Empty, 0, false));
                continue;
            }

            slots.Append(new WorldActionQueueSlot(
                queueId,
                index,
                false,
                _slotPlotId!(instance),
                _slotActionId!(instance),
                _slotQuantity!(instance),
                _slotEngaged!(instance)));
        }

        queues.Append(new RawWorldActionQueue(
            queueId,
            Guid.Empty,
            slotCount,
            empty,
            slotsWereWalked: true));
        return string.Empty;
    }

    /// <summary>
    /// Reads the queue that is effectively an integer: how many entries are in it, whether the game
    /// says another fits, and which variable holds how many it admits.
    /// </summary>
    /// <remarks>
    /// Its entries are actionables of every kind the game queues, and what one of them is doing is a
    /// question nobody has asked the game yet. Occupancy is what a plan can be shaped by, so
    /// occupancy is what is published.
    /// </remarks>
    private string ReadOccupancy(
        object queue,
        HashSet<Guid> claimed,
        WorldSampleBuffer<RawWorldActionQueue, WorldActionQueue> queues)
    {
        var queueId = _occupancyQueueId!(queue);
        if (queueId == Guid.Empty) return "carried no identity";
        if (!claimed.Add(queueId)) return $"identity {queueId} appeared more than once";

        var entryCount = _occupancyQueueEntries!(queue)?.Count ?? 0;
        queues.Append(new RawWorldActionQueue(
            queueId,
            _occupancyQueueMaximumId!(queue),
            entryCount,
            emptySlots: 0,
            slotsWereWalked: false));
        return string.Empty;
    }

    private static void Skip(ref int skipped, ref string firstFailure, string reason)
    {
        skipped++;
        if (firstFailure.Length == 0) firstFailure = reason;
    }
}
