using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace OrbModding.Common;

public static class ReflectionUtil
{
    public const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly object LoadedTypeGate = new object();
    private static readonly Dictionary<string, Type> LoadedTypes =
        new Dictionary<string, Type>(StringComparer.Ordinal);

    public static Type? FindLoadedType(string typeName)
    {
        lock (LoadedTypeGate)
        {
            if (LoadedTypes.TryGetValue(typeName, out var cached))
            {
                return cached;
            }
        }

        var assemblyQualified = Type.GetType($"{typeName}, Assembly-CSharp", throwOnError: false);
        if (assemblyQualified is not null)
        {
            CacheLoadedType(typeName, assemblyQualified);
            return assemblyQualified;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? found = null;
            try
            {
                found = assembly.GetTypes().FirstOrDefault(type => string.Equals(type.Name, typeName, StringComparison.Ordinal));
            }
            catch (ReflectionTypeLoadException ex)
            {
                found = ex.Types.Where(type => type is not null).FirstOrDefault(type => string.Equals(type!.Name, typeName, StringComparison.Ordinal));
            }

            if (found is not null)
            {
                CacheLoadedType(typeName, found);
                return found;
            }
        }

        return null;
    }

    private static void CacheLoadedType(string typeName, Type type)
    {
        lock (LoadedTypeGate)
        {
            LoadedTypes[typeName] = type;
        }
    }

    public static bool TryReadBool(object instance, out bool value, params string[] names)
    {
        foreach (var name in names)
        {
            var member = ReadMember(instance, name);
            if (member is bool boolValue)
            {
                value = boolValue;
                return true;
            }

            var invoked = InvokeNoArgs(instance, name);
            if (invoked is bool invokedBool)
            {
                value = invokedBool;
                return true;
            }
        }

        value = false;
        return false;
    }

    public static bool TryReadNumeric(object instance, out double value, params string[] names)
    {
        foreach (var name in names)
        {
            var member = ReadMember(instance, name) ?? InvokeNoArgs(instance, name);
            if (member is null)
            {
                continue;
            }

            try
            {
                value = Convert.ToDouble(member, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException)
            {
            }
        }

        value = 0.0;
        return false;
    }

    public static bool TryInvokeBool(object instance, out bool value, params string[] names)
    {
        foreach (var name in names)
        {
            if (InvokeNoArgs(instance, name) is bool boolValue)
            {
                value = boolValue;
                return true;
            }
        }

        value = false;
        return false;
    }

    public static object? InvokeNoArgs(object instance, string name)
    {
        var method = instance.GetType().GetMethod(name, InstanceFlags, null, Type.EmptyTypes, null);
        if (method is null)
        {
            return null;
        }

        try
        {
            return method.Invoke(instance, Array.Empty<object>());
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
            return null;
        }
    }

    public static object? ReadMember(object instance, string name)
    {
        var type = instance.GetType();
        var field = type.GetField(name, InstanceFlags);
        if (field is not null)
        {
            return field.GetValue(instance);
        }

        var property = type.GetProperty(name, InstanceFlags);
        if (property is null || property.GetIndexParameters().Length > 0)
        {
            return null;
        }

        try
        {
            return property.GetValue(instance);
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
            return null;
        }
    }

    public static IEnumerable<(string Name, object? Value)> ReadAllMembers(object instance)
    {
        var type = instance.GetType();
        foreach (var field in type.GetFields(InstanceFlags))
        {
            yield return (field.Name, field.GetValue(instance));
        }

        foreach (var property in type.GetProperties(InstanceFlags))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? value = null;
            try
            {
                value = property.GetValue(instance);
            }
            catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
            {
            }

            yield return (property.Name, value);
        }
    }

    public static IEnumerable<object> ReadLikelyCollectionMembers(object instance)
    {
        foreach (var member in ReadAllMembers(instance))
        {
            if (member.Value is null || member.Value is string)
            {
                continue;
            }

            if (member.Name.Contains("cost", StringComparison.OrdinalIgnoreCase) ||
                member.Name.Contains("resource", StringComparison.OrdinalIgnoreCase) ||
                member.Name.Contains("list", StringComparison.OrdinalIgnoreCase))
            {
                yield return member.Value;
            }
        }
    }

    public static string? ReadStableId(object instance)
    {
        foreach (var name in new[] { "uuid", "Uuid", "UUID", "guid", "Guid", "GUID", "id", "Id", "ID" })
        {
            var member = ReadMember(instance, name);
            if (member is null)
            {
                continue;
            }

            var text = member.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        foreach (var methodName in new[] { "GetUuid", "GetUUID", "GetGuid", "GetId" })
        {
            var value = InvokeNoArgs(instance, methodName);
            var text = value?.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

}
