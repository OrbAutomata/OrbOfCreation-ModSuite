using System;
using System.Collections.Generic;
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
}
