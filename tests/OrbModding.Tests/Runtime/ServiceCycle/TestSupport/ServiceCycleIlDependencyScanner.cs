using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal static class ServiceCycleIlDependencyScanner
{
    private static readonly OpCode[] SingleByteOpCodes = BuildOpCodeTable(multiByte: false);
    private static readonly OpCode[] MultiByteOpCodes = BuildOpCodeTable(multiByte: true);
    private static readonly string[] ForbiddenLegacyRuntimePrefixes =
    {
        "UnityEngine",
        "BepInEx",
        "HarmonyLib",
        "OrbModding.Common.Runtime.Host",
        "OrbModding.Common.Runtime.Lanes",
        "OrbModding.Common.Runtime.Process",
        "OrbModding.Common.Runtime.Kernel",
        "OrbModding.Common.Runtime.Telemetry",
        "OrbModding.Common.Runtime.Views",
    };

    internal static void Audit(
        MethodBase callable,
        MethodBody body,
        string location,
        ICollection<string> violations) =>
        Audit(callable, body, location, violations, ForbiddenLegacyRuntimePrefixes);

    internal static void Audit(
        MethodBase callable,
        MethodBody body,
        string location,
        ICollection<string> violations,
        IReadOnlyList<string> forbiddenNamespacePrefixes) =>
        Audit(callable, body, location, violations, forbiddenNamespacePrefixes, Array.Empty<string>());

    internal static void Audit(
        MethodBase callable,
        MethodBody body,
        string location,
        ICollection<string> violations,
        IReadOnlyList<string> forbiddenNamespacePrefixes,
        IReadOnlyList<string> allowedNamespacePrefixes) =>
        Audit(
            callable,
            body,
            location,
            violations,
            candidate => new NamespaceBoundary(forbiddenNamespacePrefixes, allowedNamespacePrefixes)
                .IsForbidden(candidate.Namespace ?? string.Empty));

    internal static void Audit(
        MethodBase callable,
        MethodBody body,
        string location,
        ICollection<string> violations,
        Func<Type, bool> isForbidden)
    {
        var il = body.GetILAsByteArray();
        if (il is null) return;
        var offset = 0;
        while (offset < il.Length)
        {
            var instructionOffset = offset;
            var first = il[offset++];
            OpCode opCode;
            if (first == 0xfe)
            {
                if (offset >= il.Length)
                {
                    violations.Add(location + " has a truncated multi-byte IL opcode");
                    return;
                }
                opCode = MultiByteOpCodes[il[offset++]];
            }
            else
            {
                opCode = SingleByteOpCodes[first];
            }

            if (opCode.Size == 0)
            {
                violations.Add(location + " has an unknown IL opcode at " + instructionOffset);
                return;
            }

            switch (opCode.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    if (!TryAdvance(il, ref offset, 1, location, violations)) return;
                    break;
                case OperandType.InlineVar:
                    if (!TryAdvance(il, ref offset, 2, location, violations)) return;
                    break;
                case OperandType.InlineBrTarget:
                case OperandType.InlineI:
                case OperandType.ShortInlineR:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                    if (!TryAdvance(il, ref offset, 4, location, violations)) return;
                    break;
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    if (offset > il.Length - 4)
                    {
                        violations.Add(location + " has a truncated metadata token");
                        return;
                    }
                    var token = BitConverter.ToInt32(il, offset);
                    offset += 4;
                    AuditMetadataToken(
                        callable,
                        token,
                        location + " IL " + opCode.Name,
                        violations,
                        isForbidden);
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    if (!TryAdvance(il, ref offset, 8, location, violations)) return;
                    break;
                case OperandType.InlineSwitch:
                    if (offset > il.Length - 4)
                    {
                        violations.Add(location + " has a truncated switch operand");
                        return;
                    }
                    var count = BitConverter.ToInt32(il, offset);
                    offset += 4;
                    if (count < 0 || count > (il.Length - offset) / 4)
                    {
                        violations.Add(location + " has an invalid switch operand");
                        return;
                    }
                    offset += count * 4;
                    break;
                default:
                    violations.Add(location + " uses unsupported IL operand " + opCode.OperandType);
                    return;
            }
        }
    }

    private static OpCode[] BuildOpCodeTable(bool multiByte)
    {
        var table = new OpCode[256];
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode) continue;
            var value = unchecked((ushort)opCode.Value);
            if (multiByte)
            {
                if ((value & 0xff00) == 0xfe00) table[value & 0xff] = opCode;
            }
            else if (value < 0x100)
            {
                table[value] = opCode;
            }
        }
        return table;
    }

    private static bool TryAdvance(
        byte[] il,
        ref int offset,
        int count,
        string location,
        ICollection<string> violations)
    {
        if (offset > il.Length - count)
        {
            violations.Add(location + " has a truncated IL operand");
            return false;
        }
        offset += count;
        return true;
    }

    private static void AuditMetadataToken(
        MethodBase callable,
        int token,
        string location,
        ICollection<string> violations,
        Func<Type, bool> isForbidden)
    {
        MemberInfo? member;
        try
        {
            member = callable.Module.ResolveMember(
                token,
                callable.DeclaringType?.GetGenericArguments(),
                callable.IsGenericMethod ? callable.GetGenericArguments() : null);
        }
        catch (ArgumentException exception)
        {
            violations.Add(location + " has an unresolved metadata token: " + exception.Message);
            return;
        }

        switch (member)
        {
            case Type referencedType:
                AuditType(referencedType, location + " type", violations, isForbidden);
                break;
            case FieldInfo field:
                if (field.DeclaringType is not null)
                    AuditType(field.DeclaringType, location + " field owner", violations, isForbidden);
                AuditType(field.FieldType, location + " field type", violations, isForbidden);
                break;
            case MethodBase method:
                if (method.DeclaringType is not null)
                    AuditType(method.DeclaringType, location + " method owner", violations, isForbidden);
                if (method is MethodInfo methodInfo)
                    AuditType(methodInfo.ReturnType, location + " method return", violations, isForbidden);
                foreach (var parameter in method.GetParameters())
                    AuditType(parameter.ParameterType, location + " method parameter", violations, isForbidden);
                if (method.IsGenericMethod)
                {
                    foreach (var argument in method.GetGenericArguments())
                        AuditType(argument, location + " method generic argument", violations, isForbidden);
                }
                break;
            case null:
                violations.Add(location + " resolved to no metadata member");
                break;
        }
    }

    internal static void AuditType(
        Type candidate,
        string location,
        ICollection<string> violations,
        Func<Type, bool> isForbidden)
    {
        while (candidate.IsArray || candidate.IsByRef || candidate.IsPointer)
            candidate = candidate.GetElementType()!;
        if (candidate.IsGenericParameter) return;
        if (isForbidden(candidate))
        {
            violations.Add(location + " references forbidden dependency type " + candidate.FullName);
        }
        if (!candidate.IsGenericType) return;
        foreach (var argument in candidate.GetGenericArguments())
            AuditType(argument, location, violations, isForbidden);
    }

    private static bool IsNamespaceOrChild(string candidate, string expected) =>
        string.Equals(candidate, expected, StringComparison.Ordinal) ||
        candidate.StartsWith(expected + ".", StringComparison.Ordinal);

    private readonly struct NamespaceBoundary
    {
        private readonly IReadOnlyList<string> _forbidden;
        private readonly IReadOnlyList<string> _allowed;

        public NamespaceBoundary(IReadOnlyList<string> forbidden, IReadOnlyList<string> allowed)
        {
            _forbidden = forbidden;
            _allowed = allowed;
        }

        public bool IsForbidden(string candidate) =>
            !_allowed.Any(prefix => IsNamespaceOrChild(candidate, prefix)) &&
            _forbidden.Any(prefix => IsNamespaceOrChild(candidate, prefix));
    }
}
