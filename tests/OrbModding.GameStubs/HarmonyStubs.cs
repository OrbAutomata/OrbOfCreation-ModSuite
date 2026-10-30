using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class HarmonyPatchAttribute : Attribute
    {
    }

    public sealed class HarmonyMethod : Attribute
    {
        public HarmonyMethod(Type type, string methodName)
        {
        }
    }

    public sealed class Harmony
    {
        public Harmony(string id)
        {
        }

        public void PatchAll(Assembly assembly)
        {
        }

        public PatchClassProcessor CreateClassProcessor(Type type) => new(this, type);

        public void Patch(MethodBase original, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null, HarmonyMethod? transpiler = null, HarmonyMethod? finalizer = null)
        {
        }

        public void UnpatchSelf()
        {
        }
    }

    public sealed class PatchClassProcessor
    {
        public PatchClassProcessor(Harmony instance, Type type)
        {
        }

        public List<MethodInfo> Patch() => new();
    }

    public sealed class CodeInstruction
    {
        public CodeInstruction(System.Reflection.Emit.OpCode opcode, object? operand = null)
        {
            this.opcode = opcode;
            this.operand = operand;
        }

        public System.Reflection.Emit.OpCode opcode;
        public object? operand;
    }

    public delegate object FastInvokeHandler(object target, params object[] parameters);

    public static class MethodInvoker
    {
        public static FastInvokeHandler GetHandler(MethodInfo methodInfo, bool directBoxValueAccess = false) =>
            (target, parameters) => methodInfo.Invoke(target, parameters)!;
    }

    public static class AccessTools
    {
        public static MethodInfo? Method(string typeColonMethod, Type[] parameters)
        {
            var separator = typeColonMethod.IndexOf(':');
            if (separator <= 0 || separator == typeColonMethod.Length - 1) return null;
            return TypeByName(typeColonMethod.Substring(0, separator))?.GetMethod(
                typeColonMethod.Substring(separator + 1),
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                parameters,
                null);
        }

        public static MethodInfo? Method(string typeColonMethod)
        {
            var separator = typeColonMethod.IndexOf(':');
            if (separator <= 0 || separator == typeColonMethod.Length - 1)
            {
                return null;
            }

            var type = TypeByName(typeColonMethod.Substring(0, separator));
            var methodName = typeColonMethod.Substring(separator + 1);
            return type?.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method => method.Name == methodName);
        }

        public static Type? TypeByName(string name)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var preferred = assemblies.FirstOrDefault(assembly =>
                assembly.GetName().Name == "Assembly-CSharp");
            if (preferred is not null)
            {
                var preferredType = FindType(preferred, name);
                if (preferredType is not null)
                {
                    return preferredType;
                }
            }

            foreach (var assembly in assemblies)
            {
                var match = FindType(assembly, name);
                if (match is not null)
                {
                    return match;
                }
            }

            return null;
        }

        private static Type? FindType(Assembly assembly, string name)
        {
            try
            {
                return assembly.GetTypes().FirstOrDefault(type => type.Name == name || type.FullName == name);
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types
                    .Where(type => type is not null)
                    .FirstOrDefault(type => type!.Name == name || type.FullName == name);
            }
        }

        public static List<MethodInfo> GetDeclaredMethods(Type type)
        {
            return new List<MethodInfo>(type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        }
    }
}
