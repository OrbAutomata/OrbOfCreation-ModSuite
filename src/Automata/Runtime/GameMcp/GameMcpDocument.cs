#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections;
using System.Collections.Generic;

namespace OrbAutomata.GameMcp;

/// <summary>
/// A native-free immutable result document produced by a Unity-frame operation. The model has no
/// JSON dependency; the HTTP transport is the only layer that converts it to a wire representation.
/// </summary>
internal abstract class GameMcpValue
{
    internal static GameMcpValue From(object? value) => value switch
    {
        null => GameMcpNull.Instance,
        GameMcpValue structured => structured,
        GameMcpObjectBuilder objectBuilder => objectBuilder.Freeze(),
        GameMcpArrayBuilder arrayBuilder => arrayBuilder.Freeze(),
        string text => new GameMcpScalar(text),
        bool boolean => new GameMcpScalar(boolean),
        byte number => new GameMcpScalar((ulong)number),
        sbyte number => new GameMcpScalar((long)number),
        short number => new GameMcpScalar((long)number),
        ushort number => new GameMcpScalar((ulong)number),
        int number => new GameMcpScalar((long)number),
        uint number => new GameMcpScalar((ulong)number),
        long number => new GameMcpScalar(number),
        ulong number => new GameMcpScalar(number),
        float number => new GameMcpScalar((double)number),
        double number => new GameMcpScalar(number),
        decimal number => new GameMcpScalar(number),
        Guid identity => new GameMcpScalar(identity.ToString("D")),
        DateTime instant => new GameMcpScalar(instant.ToUniversalTime().ToString("O")),
        Enum enumeration => new GameMcpScalar(enumeration.ToString()),
        IEnumerable enumerable => FromEnumerable(enumerable),
        _ => new GameMcpDomainValue(value),
    };

    private static GameMcpValue FromEnumerable(IEnumerable source)
    {
        var values = new List<GameMcpValue>();
        foreach (var item in source) values.Add(From(item));
        return new GameMcpArray(values);
    }
}

internal sealed class GameMcpObjectBuilder
{
    private readonly List<GameMcpMutableProperty> _properties = new();
    private readonly Dictionary<string, int> _positions = new(StringComparer.Ordinal);

    internal int Count => _properties.Count;

    internal object? this[string name]
    {
        get => _positions.TryGetValue(name, out var index)
            ? _properties[index].Value
            : null;
        set => Set(name, value);
    }

    internal void Add(string name, object? value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A result property name is required.", nameof(name));
        if (_positions.ContainsKey(name))
            throw new InvalidOperationException("Result property '" + name + "' was assigned twice.");
        _positions.Add(name, _properties.Count);
        _properties.Add(new GameMcpMutableProperty(name, value));
    }

    internal void Set(string name, object? value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A result property name is required.", nameof(name));
        if (_positions.TryGetValue(name, out var index))
        {
            _properties[index] = new GameMcpMutableProperty(name, value);
            return;
        }
        Add(name, value);
    }

    internal void CopyFrom(GameMcpObjectBuilder source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        for (var index = 0; index < source._properties.Count; index++)
        {
            var property = source._properties[index];
            Set(property.Name, property.Value);
        }
    }

    internal void CopyFrom(GameMcpObject source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        for (var index = 0; index < source.Properties.Count; index++)
        {
            var property = source.Properties[index];
            Set(property.Name, property.Value);
        }
    }

    internal GameMcpObject Freeze()
    {
        var result = new GameMcpProperty[_properties.Count];
        for (var index = 0; index < result.Length; index++)
        {
            var property = _properties[index];
            result[index] = new GameMcpProperty(
                property.Name,
                GameMcpValue.From(property.Value));
        }
        return new GameMcpObject(result);
    }
}

internal sealed class GameMcpArrayBuilder
{
    private readonly List<GameMcpValue> _items = new();

    internal GameMcpArrayBuilder() { }

    internal GameMcpArrayBuilder(params object?[] items)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));
        for (var index = 0; index < items.Length; index++) Add(items[index]);
    }

    internal int Count => _items.Count;
    internal void Add(object? value) => _items.Add(GameMcpValue.From(value));
    internal GameMcpArray Freeze() => new(_items);
}

internal readonly struct GameMcpMutableProperty
{
    internal GameMcpMutableProperty(string name, object? value)
    {
        Name = name;
        Value = value;
    }

    internal string Name { get; }
    internal object? Value { get; }
}

internal readonly struct GameMcpProperty
{
    internal GameMcpProperty(string name, GameMcpValue value)
    {
        Name = name;
        Value = value;
    }

    internal string Name { get; }
    internal GameMcpValue Value { get; }
}

internal sealed class GameMcpObject : GameMcpValue
{
    private readonly GameMcpProperty[] _properties;

    internal GameMcpObject(IReadOnlyList<GameMcpProperty> properties)
    {
        _properties = new GameMcpProperty[properties.Count];
        for (var index = 0; index < properties.Count; index++)
            _properties[index] = properties[index];
    }

    internal IReadOnlyList<GameMcpProperty> Properties => _properties;
}

internal sealed class GameMcpArray : GameMcpValue
{
    private readonly GameMcpValue[] _items;

    internal GameMcpArray(IReadOnlyList<GameMcpValue> items)
    {
        _items = new GameMcpValue[items.Count];
        for (var index = 0; index < items.Count; index++) _items[index] = items[index];
    }

    internal IReadOnlyList<GameMcpValue> Items => _items;
}

internal sealed class GameMcpScalar : GameMcpValue
{
    internal GameMcpScalar(object value) => Value = value ?? throw new ArgumentNullException(nameof(value));
    internal object Value { get; }
}

internal sealed class GameMcpDomainValue : GameMcpValue
{
    internal GameMcpDomainValue(object value) => Value = value ?? throw new ArgumentNullException(nameof(value));
    internal object Value { get; }
}

internal sealed class GameMcpProjectedDomainValue : GameMcpValue
{
    internal GameMcpProjectedDomainValue(
        object value,
        string[] paths,
        string category,
        string nativeType,
        bool addressable = true,
        bool tableRow = true)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Paths = paths is null ? Array.Empty<string>() : (string[])paths.Clone();
        Category = category ?? string.Empty;
        NativeType = nativeType ?? string.Empty;
        Addressable = addressable;
        TableRow = tableRow;
    }

    internal object Value { get; }
    internal string[] Paths { get; }
    internal string Category { get; }
    internal string NativeType { get; }
    internal bool Addressable { get; }

    /// <summary>
    /// Whether this projection is a row of a table, where the declared paths are the header's
    /// promise and every one of them appears whether or not this row filled it. A detail block is
    /// not a table: it has no header to keep honest and no siblings to line up with, so a path the
    /// row carries nothing under is simply absent there, which is what absence means everywhere
    /// outside a table.
    /// </summary>
    internal bool TableRow { get; }

    internal GameMcpProjectedDomainValue WithoutAddressableIdentity() =>
        new(Value, Paths, Category, NativeType, addressable: false, tableRow: TableRow);
}

internal sealed class GameMcpNull : GameMcpValue
{
    internal static readonly GameMcpNull Instance = new();
    private GameMcpNull() { }
}
#endif
