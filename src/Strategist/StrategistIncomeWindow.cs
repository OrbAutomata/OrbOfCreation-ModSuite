using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.Strategy;

namespace OrbAutomata;

/// <summary>
/// The strategist's only retained state: each resource's lifetime gain sampled on the game's clock,
/// long enough back to cover one full cycle of the slowest running producer.
/// </summary>
/// <remarks>
/// <para>
/// Lifetime quantity is the game's own gross-gain counter: <c>ResourceSO.RegisterGain</c> adds every
/// non-splash gain to it, bursts included, so gain over a window is the difference of two readings and
/// a drain never hides it. The counter stops at the cap — a capped resource registers only the room it
/// had — so a window that touched the cap reads a lower bound.
/// </para>
/// <para>
/// Samples are kept at least a second apart, and further apart on long windows, so a long spell
/// recharge costs a bounded number of samples, not one per world.
/// </para>
/// </remarks>
internal sealed class StrategistIncomeWindow
{
    internal const double MinimumSeconds = 60d;
    private const int SamplesPerWindow = 120;

    private Guid[] _ids = new Guid[0];
    private Ring[] _rings = new Ring[0];
    private int _count;
    private double _lastTime = double.NaN;

    /// <summary>Takes one reading. A clock that ran backwards is a different run, so history goes.</summary>
    internal void Observe(double now, double requiredSeconds, ReadOnlySpan<Sample> readings)
    {
        if (!double.IsNaN(_lastTime) && now < _lastTime) Clear();
        _lastTime = now;
        var spacing = Math.Max(1d, requiredSeconds / SamplesPerWindow);
        for (var index = 0; index < readings.Length; index++)
            RingFor(readings[index].ResourceId).Add(now, readings[index], spacing, requiredSeconds);
    }

    internal StrategistWindowReading Read(double now, double requiredSeconds)
    {
        var rows = new StrategistWindowRow[_count];
        var written = 0;
        for (var index = 0; index < _count; index++)
        {
            if (_rings[index].TryRead(now, requiredSeconds, _ids[index], out var row))
                rows[written++] = row;
        }
        return new StrategistWindowReading(
            requiredSeconds, PublicationTable<StrategistWindowRow>.Create(rows, written));
    }

    private void Clear()
    {
        _ids = new Guid[0];
        _rings = new Ring[0];
        _count = 0;
    }

    private Ring RingFor(Guid id)
    {
        var low = 0;
        var high = _count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var comparison = _ids[middle].CompareTo(id);
            if (comparison == 0) return _rings[middle];
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        if (_count == _ids.Length)
        {
            Array.Resize(ref _ids, Math.Max(16, _count * 2));
            Array.Resize(ref _rings, _ids.Length);
        }
        Array.Copy(_ids, low, _ids, low + 1, _count - low);
        Array.Copy(_rings, low, _rings, low + 1, _count - low);
        _ids[low] = id;
        _rings[low] = new Ring();
        _count++;
        return _rings[low];
    }

    internal readonly struct Sample
    {
        internal Sample(Guid resourceId, BigDouble lifetime, bool atCap)
        {
            ResourceId = resourceId;
            Lifetime = lifetime;
            AtCap = atCap;
        }

        internal Guid ResourceId { get; }
        internal BigDouble Lifetime { get; }
        internal bool AtCap { get; }
    }

    private sealed class Ring
    {
        private double[] _times = new double[8];
        private BigDouble[] _lifetimes = new BigDouble[8];
        private bool[] _atCap = new bool[8];
        private int _start;
        private int _length;
        private BigDouble _latestLifetime;
        private double _latestTime;
        private bool _latestAtCap;
        private bool _hasLatest;

        internal void Add(double now, in Sample sample, double spacing, double requiredSeconds)
        {
            _latestTime = now;
            _latestLifetime = sample.Lifetime;
            _latestAtCap = sample.AtCap;
            _hasLatest = true;
            if (_length > 0)
            {
                var newest = (_start + _length - 1) % _times.Length;
                if (now - _times[newest] < spacing)
                {
                    _atCap[newest] |= sample.AtCap;
                    return;
                }
            }

            if (_length == _times.Length) Grow();
            var slot = (_start + _length) % _times.Length;
            _times[slot] = now;
            _lifetimes[slot] = sample.Lifetime;
            _atCap[slot] = sample.AtCap;
            _length++;
            while (_length > 1 && now - _times[(_start + 1) % _times.Length] >= requiredSeconds)
            {
                _start = (_start + 1) % _times.Length;
                _length--;
            }
        }

        /// <summary>
        /// Gain from the newest sample at least a window old, or from the oldest one while the
        /// window is still filling.
        /// </summary>
        internal bool TryRead(double now, double requiredSeconds, Guid id, out StrategistWindowRow row)
        {
            row = default;
            if (!_hasLatest || _length == 0) return false;
            var anchor = 0;
            for (var offset = 1; offset < _length; offset++)
            {
                if (now - _times[(_start + offset) % _times.Length] < requiredSeconds) break;
                anchor = offset;
            }

            var clipped = _latestAtCap;
            for (var offset = anchor; offset < _length; offset++)
                clipped |= _atCap[(_start + offset) % _times.Length];
            var slot = (_start + anchor) % _times.Length;
            var gained = _latestLifetime - _lifetimes[slot];
            if (gained < BigDouble.Zero) gained = BigDouble.Zero;
            row = new StrategistWindowRow(id, gained, _latestTime - _times[slot], clipped);
            return true;
        }

        private void Grow()
        {
            var size = _times.Length * 2;
            var times = new double[size];
            var lifetimes = new BigDouble[size];
            var atCap = new bool[size];
            for (var offset = 0; offset < _length; offset++)
            {
                var from = (_start + offset) % _times.Length;
                times[offset] = _times[from];
                lifetimes[offset] = _lifetimes[from];
                atCap[offset] = _atCap[from];
            }
            _times = times;
            _lifetimes = lifetimes;
            _atCap = atCap;
            _start = 0;
        }
    }
}
