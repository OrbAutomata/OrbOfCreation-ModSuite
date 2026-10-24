using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OrbModding.GameStubs;

/// <summary>Restores the shared native model between tests, including newly added registries.</summary>
public static class GameStubState
{
    private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    // Capture once before the first test changes a singleton. Closed generic bases matter:
    // AbstractListVariable<T>.All is a different registry for each concrete list element type.
    private static readonly FieldInfo[] Fields = ModelTypes()
        .SelectMany(type => type.GetFields(StaticFields))
        .Where(field => !field.IsLiteral && !field.FieldType.IsPointer &&
            (!field.IsInitOnly || !field.FieldType.IsValueType && field.FieldType != typeof(string)))
        .ToArray();
    private static readonly object?[] Defaults = Capture();

    public static void Reset()
    {
        var copies = new Dictionary<object, object>(ReferenceComparer.Instance);
        for (var index = 0; index < Fields.Length; index++)
        {
            var field = Fields[index];
            var value = Copy(Defaults[index], copies);
            if (field.IsInitOnly)
                Restore(field.GetValue(null), value);
            else
                field.SetValue(null, value);
        }
        // Registries are fixture input, even where a singleton initializer registers a default.
        foreach (var field in Fields.Where(IsRegistry))
        {
            if (field.GetValue(null) is IDictionary dictionary) dictionary.Clear();
            else if (field.GetValue(null) is IList list) list.Clear();
            else throw new InvalidOperationException($"Unknown stub registry {field.DeclaringType}.{field.Name}.");
        }
    }

    private static bool IsRegistry(FieldInfo field) =>
        field.Name is "All" or "RuntimeLookup" or "globalTooltips";

    private static IEnumerable<Type> ModelTypes()
    {
        var types = new HashSet<Type>();
        foreach (var type in typeof(GameManager).Assembly.GetTypes())
        {
            if (type.ContainsGenericParameters || type == typeof(GameStubState) ||
                type.DeclaringType == typeof(GameStubState) ||
                type.IsDefined(typeof(CompilerGeneratedAttribute), false) ||
                (type == typeof(BigDouble) || type.DeclaringType == typeof(BigDouble)))
                continue;
            for (var current = type; current is not null && current.Assembly == typeof(GameManager).Assembly;
                 current = current.BaseType)
                types.Add(current);
        }
        return types.OrderBy(type => type.FullName, StringComparer.Ordinal);
    }

    private static object?[] Capture()
    {
        // Force every initializer before cloning any default. One initializer may register a
        // variable in a field on another type.
        foreach (var field in Fields) _ = field.GetValue(null);
        var copies = new Dictionary<object, object>(ReferenceComparer.Instance);
        return Fields.Select(field => Copy(field.GetValue(null), copies)).ToArray();
    }

    private static object? Copy(object? source, Dictionary<object, object> copies)
    {
        if (source is null) return null;
        var type = source.GetType();
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
            type == typeof(Guid) || (type == typeof(BigDouble) || type.DeclaringType == typeof(BigDouble)) || source is Delegate || source is Type)
            return source;
        if (copies.TryGetValue(source, out var existing)) return existing;
        if (source is Array array)
        {
            var result = (Array)array.Clone();
            copies.Add(source, result);
            for (var index = 0; index < array.Length; index++) result.SetValue(Copy(array.GetValue(index), copies), index);
            return result;
        }
        if (source is IDictionary dictionary)
        {
            var result = (IDictionary)Activator.CreateInstance(type)!;
            copies.Add(source, result);
            foreach (DictionaryEntry entry in dictionary) result.Add(Copy(entry.Key, copies)!, Copy(entry.Value, copies));
            return result;
        }
        if (source is IList list)
        {
            var result = (IList)Activator.CreateInstance(type)!;
            copies.Add(source, result);
            foreach (var item in list) result.Add(Copy(item, copies));
            return result;
        }
        var clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(source, null)!;
        copies.Add(source, clone);
        foreach (var field in ObjectFields(type)) field.SetValue(clone, Copy(field.GetValue(source), copies));
        return clone;
    }

    private static IEnumerable<FieldInfo> ObjectFields(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            foreach (var field in current.GetFields(InstanceFields)) yield return field;
    }

    private static void Restore(object? target, object? value)
    {
        if (target is IDictionary dictionary && value is IDictionary defaults)
        {
            dictionary.Clear();
            foreach (DictionaryEntry entry in defaults) dictionary.Add(entry.Key, entry.Value);
        }
        else if (target is IList list && value is IList rows)
        {
            list.Clear();
            foreach (var row in rows) list.Add(row);
        }
        else if (target is not null && value is not null)
            foreach (var field in ObjectFields(target.GetType())) field.SetValue(target, field.GetValue(value));
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
    }
}
