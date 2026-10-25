using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One cross-table authored result, retained only for the structural read it derives from.</summary>
internal sealed class WorldAuthoredValue<T>
{
    private long _generation;
    private T _value = default!;
    private bool _hasValue;

    internal T Of(long generation, Func<T> derive)
    {
        if (_hasValue && _generation == generation) return _value;
        var value = derive();
        _generation = generation;
        _hasValue = true;
        return _value = value;
    }
}
