using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

internal static class MethodBodyReferences
{
    public static MethodBodyDefinitionReference[] References(
        GameAssemblyMetadata assembly,
        string type,
        string method,
        params string[] parameterTypes) =>
        (parameterTypes.Length == 0
            ? assembly.GetMethodBodyDefinitionReferences(type, method)
                .Concat(assembly.GetMethodBodyMemberReferences(type, method))
            : assembly.GetMethodBodyDefinitionReferences(type, method, parameterTypes)
                .Concat(assembly.GetMethodBodyMemberReferences(type, method, parameterTypes)))
        .OrderBy(reference => reference.Offset)
        .ToArray();

    public static int Offset(
        IReadOnlyList<MethodBodyDefinitionReference> references,
        string declaringType,
        string memberName)
    {
        var matches = references
            .Where(reference =>
                reference.DeclaringType == declaringType && reference.MemberName == memberName)
            .ToArray();
        if (matches.Length == 1) return matches[0].Offset;
        throw new InvalidOperationException(
            (matches.Length == 0
                ? $"The method body never references {declaringType}.{memberName}."
                : $"The method body references {declaringType}.{memberName} {matches.Length} times.") +
            " Native refs: " + string.Join("; ", references.Select(Describe)));
    }

    public static string Describe(MethodBodyDefinitionReference reference) =>
        $"IL_{reference.Offset:X4} 0x{reference.Token:X8} {reference.Kind} " +
        $"{reference.DeclaringType}.{reference.MemberName}";

    public static void AssertMethod(
        GameAssemblyMetadata assembly,
        string typeName,
        string methodName,
        bool isStatic,
        string returnType,
        params string[] parameterTypes)
    {
        var matches = assembly.GetMethods(typeName, methodName);
        Assert.Contains(matches, method =>
            method.IsStatic == isStatic &&
            method.ReturnType == returnType &&
            method.ParameterTypes.SequenceEqual(parameterTypes));
    }
}
