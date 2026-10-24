using System;
using OrbModding.Common.Runtime.World;

namespace OrbMentor;

/// <summary>
/// Bounded main-thread history between the exact native mastery hooks and world collection.
/// </summary>
internal sealed class MentorMasteryEventJournal : IWorldMasteryExperienceSource
{
    internal const int DefaultCapacity = 256;

    private readonly Entry[] _entries;
    private int _start;
    private int _count;
    private long _epoch;
    private long _nextSequence;

    internal MentorMasteryEventJournal(int capacity = DefaultCapacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _entries = new Entry[capacity];
    }

    internal int Count => _count;
    internal long Overwritten { get; private set; }

    internal void Publish(
        long lifecycleEpoch,
        MasteryExperienceDomain domain,
        Guid sourceId,
        int sourceMastery,
        bool sourceEligible,
        MentorAmount amount)
    {
        if (lifecycleEpoch <= 0 || sourceId == Guid.Empty || !amount.IsValidPositive) return;
        if (_epoch != lifecycleEpoch) Reset(lifecycleEpoch);

        var observation = new WorldMasteryExperience(
            checked(++_nextSequence),
            domain,
            sourceId,
            sourceMastery,
            sourceEligible,
            new BigDouble(amount.Mantissa, amount.Exponent));
        if (_count == _entries.Length)
        {
            _start = (_start + 1) % _entries.Length;
            _count--;
            Overwritten++;
        }
        var index = (_start + _count) % _entries.Length;
        _entries[index] = new Entry(lifecycleEpoch, observation);
        _count++;
    }

    public void CopyTo(long lifecycleEpoch, WorldRelationBuffer<WorldMasteryExperience> destination)
    {
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        if (lifecycleEpoch != _epoch) return;
        for (var offset = 0; offset < _count; offset++)
        {
            ref readonly var entry = ref _entries[(_start + offset) % _entries.Length];
            if (entry.Epoch == lifecycleEpoch) destination.Append(entry.Observation);
        }
    }

    internal void Reset(long lifecycleEpoch)
    {
        Array.Clear(_entries, 0, _entries.Length);
        _start = 0;
        _count = 0;
        _epoch = lifecycleEpoch;
        _nextSequence = 0;
    }

    private readonly struct Entry
    {
        internal Entry(long epoch, WorldMasteryExperience observation)
        {
            Epoch = epoch;
            Observation = observation;
        }

        internal long Epoch { get; }
        internal WorldMasteryExperience Observation { get; }
    }
}
