using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

public sealed partial class ServiceCycleArchitectureTests
{
    private static readonly string[] ForbiddenNamespacePrefixes =
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

    [Fact]
    public void NonpublicFieldsDoNotHideForbiddenDependenciesOrInterfaceDispatchedWriters()
    {
        var violations = new List<string>();
        foreach (var type in ServiceCycleTypes(publicOnly: false))
        {
            foreach (var field in type.GetFields(
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                         BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                AuditForbiddenDependency(field.FieldType, type.FullName + "." + field.Name, violations);
            }
        }

        var actionWriterFields = typeof(ServiceActionWriter<>).GetFields(
            BindingFlags.Instance | BindingFlags.NonPublic);
        var projectionWriterField = typeof(ServiceStateProjectionBuilder).GetFields(
            BindingFlags.Instance | BindingFlags.NonPublic).Single();
        Assert.NotEmpty(actionWriterFields);
        Assert.All(actionWriterFields, field => Assert.False(field.FieldType.IsInterface));
        Assert.False(projectionWriterField.FieldType.IsInterface);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void EveryMethodSignatureLocalAndMetadataOperandRemainsBehindNeutralBoundary()
    {
        var violations = new List<string>();
        foreach (var type in ServiceCycleTypes(publicOnly: false))
        {
            var callables = type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Cast<MethodBase>()
                .Concat(type.GetConstructors(
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            if (type.TypeInitializer is not null) callables = callables.Append(type.TypeInitializer);

            foreach (var callable in callables.Distinct())
            {
                var location = (type.FullName ?? type.Name) + "." + callable.Name;
                if (callable is MethodInfo method)
                    AuditForbiddenDependency(method.ReturnType, location + " return", violations);
                foreach (var parameter in callable.GetParameters())
                    AuditForbiddenDependency(parameter.ParameterType, location + " parameter", violations);
                var body = TryGetMethodBody(callable);
                if (body is null) continue;
                foreach (var local in body.LocalVariables)
                    AuditForbiddenDependency(local.LocalType, location + " local", violations);
                ServiceCycleIlDependencyScanner.Audit(callable, body, location, violations);
            }
        }
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void IlScannerDetectsStaticCallsAndLegacyTypeTokens()
    {
        var staticCall = typeof(ServiceCycleArchitectureTests).GetMethod(
            nameof(StaticVoidCall), BindingFlags.Static | BindingFlags.NonPublic)!;
        var staticViolations = new List<string>();
        ServiceCycleIlDependencyScanner.Audit(
            staticCall, staticCall.GetMethodBody()!, nameof(StaticVoidCall), staticViolations);

        var typeToken = typeof(ServiceCycleArchitectureTests).GetMethod(
            nameof(LegacyTypeToken), BindingFlags.Static | BindingFlags.NonPublic)!;
        var tokenViolations = new List<string>();
        ServiceCycleIlDependencyScanner.Audit(
            typeToken, typeToken.GetMethodBody()!, nameof(LegacyTypeToken), tokenViolations);

        Assert.Contains(staticViolations, violation =>
            violation.Contains("UnityEngine.Resources", StringComparison.Ordinal));
        Assert.Contains(tokenViolations, violation =>
            violation.Contains("UnityEngine.Object", StringComparison.Ordinal));
    }

    [Fact]
    public void CustomAttributeMetadataScannerDetectsForbiddenTypeArguments()
    {
        var violations = new List<string>();
        foreach (var methodName in new[]
                 {
                     nameof(ForbiddenTypeAttributeFixture),
                     nameof(ForbiddenJaggedTypeAttributeFixture),
                 })
        {
            var method = typeof(ServiceCycleArchitectureTests).GetMethod(
                methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
            AuditCustomAttributes(
                method.GetCustomAttributesData(),
                methodName,
                new[] { "OrbModding.Common.Runtime.ServiceCycle.Orchestration" },
                violations);
        }

        Assert.Equal(2, violations.Count(violation =>
            violation.Contains(nameof(SuiteFramePump), StringComparison.Ordinal)));
    }

    private static Type[] ServiceCycleTypes(bool publicOnly) =>
        typeof(ServiceCycleRegistry).Assembly.GetTypes()
            .Where(type => (!publicOnly || type.IsPublic) && type.Namespace?.StartsWith(
                "OrbModding.Common.Runtime.ServiceCycle", StringComparison.Ordinal) == true)
            .ToArray();

    private static void AuditLayerDependencies(
        string sourceNamespace,
        IReadOnlyList<string> forbiddenNamespacePrefixes,
        ICollection<string> violations)
    {
        var sourceTypes = ServiceCycleTypes(publicOnly: false)
            .Where(type => type.Namespace == sourceNamespace || type.Namespace?.StartsWith(
                sourceNamespace + ".", StringComparison.Ordinal) == true);
        foreach (var type in sourceTypes)
        {
            var typeLocation = type.FullName ?? type.Name;
            AuditCustomAttributes(
                type.GetCustomAttributesData(),
                typeLocation + " attribute",
                forbiddenNamespacePrefixes,
                violations);
            if (type.BaseType is not null)
            {
                AuditNamespaceReference(
                    type.BaseType,
                    typeLocation + " base type",
                    forbiddenNamespacePrefixes,
                    violations);
            }
            foreach (var contract in type.GetInterfaces())
            {
                AuditNamespaceReference(
                    contract,
                    typeLocation + " interface",
                    forbiddenNamespacePrefixes,
                    violations);
            }
            AuditGenericParameterConstraints(
                type.GetGenericArguments(),
                typeLocation + " generic constraint",
                forbiddenNamespacePrefixes,
                violations);

            foreach (var field in type.GetFields(
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                         BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                AuditCustomAttributes(
                    field.GetCustomAttributesData(),
                    typeLocation + "." + field.Name + " attribute",
                    forbiddenNamespacePrefixes,
                    violations);
                AuditNamespaceReference(
                    field.FieldType,
                    (type.FullName ?? type.Name) + "." + field.Name + " field",
                    forbiddenNamespacePrefixes,
                    violations);
            }

            foreach (var property in type.GetProperties(
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                         BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                AuditCustomAttributes(
                    property.GetCustomAttributesData(),
                    typeLocation + "." + property.Name + " attribute",
                    forbiddenNamespacePrefixes,
                    violations);
            }

            foreach (var eventInfo in type.GetEvents(
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                         BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                AuditCustomAttributes(
                    eventInfo.GetCustomAttributesData(),
                    typeLocation + "." + eventInfo.Name + " attribute",
                    forbiddenNamespacePrefixes,
                    violations);
            }

            var callables = type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Cast<MethodBase>()
                .Concat(type.GetConstructors(
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            if (type.TypeInitializer is not null) callables = callables.Append(type.TypeInitializer);

            foreach (var callable in callables.Distinct())
            {
                var location = (type.FullName ?? type.Name) + "." + callable.Name;
                AuditCustomAttributes(
                    callable.GetCustomAttributesData(),
                    location + " attribute",
                    forbiddenNamespacePrefixes,
                    violations);
                if (callable is MethodInfo genericMethod)
                {
                    AuditGenericParameterConstraints(
                        genericMethod.GetGenericArguments(),
                        location + " generic constraint",
                        forbiddenNamespacePrefixes,
                        violations);
                }
                if (callable is MethodInfo method)
                {
                    AuditCustomAttributes(
                        method.ReturnParameter.GetCustomAttributesData(),
                        location + " return attribute",
                        forbiddenNamespacePrefixes,
                        violations);
                    AuditNamespaceReference(
                        method.ReturnType,
                        location + " return",
                        forbiddenNamespacePrefixes,
                        violations);
                }
                foreach (var parameter in callable.GetParameters())
                {
                    AuditCustomAttributes(
                        parameter.GetCustomAttributesData(),
                        location + " parameter attribute",
                        forbiddenNamespacePrefixes,
                        violations);
                    AuditNamespaceReference(
                        parameter.ParameterType,
                        location + " parameter",
                        forbiddenNamespacePrefixes,
                        violations);
                }

                var body = TryGetMethodBody(callable);
                if (body is null) continue;
                foreach (var local in body.LocalVariables)
                {
                    AuditNamespaceReference(
                        local.LocalType,
                        location + " local",
                        forbiddenNamespacePrefixes,
                        violations);
                }
                ServiceCycleIlDependencyScanner.Audit(
                    callable,
                    body,
                    location,
                    violations,
                    forbiddenNamespacePrefixes);
            }
        }
    }

    private static void AuditGenericParameterConstraints(
        IEnumerable<Type> candidates,
        string location,
        IReadOnlyList<string> forbiddenNamespacePrefixes,
        ICollection<string> violations)
    {
        foreach (var candidate in candidates)
        {
            if (!candidate.IsGenericParameter) continue;
            AuditCustomAttributes(
                candidate.GetCustomAttributesData(),
                location + " attribute",
                forbiddenNamespacePrefixes,
                violations);
            foreach (var constraint in candidate.GetGenericParameterConstraints())
            {
                AuditNamespaceReference(
                    constraint,
                    location,
                    forbiddenNamespacePrefixes,
                    violations);
            }
        }
    }

    private static void AuditCustomAttributes(
        IEnumerable<CustomAttributeData> attributes,
        string location,
        IReadOnlyList<string> forbiddenNamespacePrefixes,
        ICollection<string> violations)
    {
        foreach (var attribute in attributes)
        {
            AuditNamespaceReference(
                attribute.AttributeType,
                location + " type",
                forbiddenNamespacePrefixes,
                violations);
            if (attribute.Constructor.DeclaringType is { } constructorType)
            {
                AuditNamespaceReference(
                    constructorType,
                    location + " constructor",
                    forbiddenNamespacePrefixes,
                    violations);
            }
            foreach (var argument in attribute.ConstructorArguments)
                AuditCustomAttributeArgument(argument, location, forbiddenNamespacePrefixes, violations);
            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.MemberInfo.DeclaringType is { } memberOwner)
                {
                    AuditNamespaceReference(
                        memberOwner,
                        location + " named member",
                        forbiddenNamespacePrefixes,
                        violations);
                }
                AuditCustomAttributeArgument(
                    argument.TypedValue,
                    location + " named value",
                    forbiddenNamespacePrefixes,
                    violations);
            }
        }
    }

    private static void AuditCustomAttributeArgument(
        CustomAttributeTypedArgument argument,
        string location,
        IReadOnlyList<string> forbiddenNamespacePrefixes,
        ICollection<string> violations)
    {
        AuditNamespaceReference(argument.ArgumentType, location + " argument", forbiddenNamespacePrefixes, violations);
        if (argument.Value is Type referencedType)
        {
            AuditNamespaceReference(referencedType, location + " typeof", forbiddenNamespacePrefixes, violations);
            return;
        }
        if (argument.Value is not IEnumerable<CustomAttributeTypedArgument> values) return;
        foreach (var value in values)
            AuditCustomAttributeArgument(value, location + " array", forbiddenNamespacePrefixes, violations);
    }

    private static MethodBody? TryGetMethodBody(MethodBase callable)
    {
        try { return callable.GetMethodBody(); }
        catch (InvalidOperationException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    private static void AuditPublicSurface(Type candidate, string location, ICollection<string> violations)
    {
        if (candidate.IsByRef || candidate.IsPointer || candidate.IsArray) candidate = candidate.GetElementType()!;
        if (candidate.IsGenericParameter || candidate == typeof(void)) return;
        if (typeof(Delegate).IsAssignableFrom(candidate)) violations.Add(location + " leaks delegate " + candidate.FullName);
        if (candidate != typeof(string) &&
            (typeof(IList).IsAssignableFrom(candidate) || ImplementsOpenGeneric(candidate, typeof(ICollection<>))))
            violations.Add(location + " leaks mutable collection " + candidate.FullName);
        var candidateNamespace = candidate.Namespace ?? string.Empty;
        if (ForbiddenNamespacePrefixes.Any(prefix => candidateNamespace.StartsWith(prefix, StringComparison.Ordinal)))
            violations.Add(location + " leaks forbidden type " + candidate.FullName);
        if (candidate.Name.Contains("ConfigEntry", StringComparison.Ordinal) ||
            candidate.Name.Contains("NativeObject", StringComparison.Ordinal))
            violations.Add(location + " leaks forbidden DTO " + candidate.FullName);
        if (!candidate.IsGenericType) return;
        foreach (var argument in candidate.GetGenericArguments()) AuditPublicSurface(argument, location, violations);
    }

    private static bool ImplementsOpenGeneric(Type candidate, Type openGeneric) =>
        candidate.IsGenericType && candidate.GetGenericTypeDefinition() == openGeneric ||
        candidate.GetInterfaces().Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == openGeneric);

    private static void AuditNamespaceReference(
        Type candidate,
        string location,
        IReadOnlyList<string> forbiddenNamespacePrefixes,
        ICollection<string> violations)
    {
        while (candidate.IsArray || candidate.IsByRef || candidate.IsPointer)
            candidate = candidate.GetElementType()!;
        if (candidate.IsGenericParameter) return;
        var candidateNamespace = candidate.Namespace ?? string.Empty;
        if (forbiddenNamespacePrefixes.Any(prefix =>
                candidateNamespace.StartsWith(prefix, StringComparison.Ordinal)))
            violations.Add(location + " references forbidden layer " + candidate.FullName);
        if (!candidate.IsGenericType) return;
        foreach (var argument in candidate.GetGenericArguments())
            AuditNamespaceReference(argument, location, forbiddenNamespacePrefixes, violations);
    }

    private static void AuditForbiddenDependency(Type candidate, string location, ICollection<string> violations)
    {
        if (candidate.IsArray || candidate.IsByRef || candidate.IsPointer) candidate = candidate.GetElementType()!;
        if (candidate.IsGenericParameter) return;
        if (typeof(Delegate).IsAssignableFrom(candidate)) violations.Add(location + " stores delegate " + candidate.FullName);
        var assembly = candidate.Assembly.GetName().Name ?? string.Empty;
        if (assembly is "Assembly-CSharp" or "Assembly-CSharp-firstpass" or "BepInEx" or "0Harmony" ||
            assembly.StartsWith("UnityEngine", StringComparison.Ordinal))
            violations.Add(location + " uses forbidden assembly type " + candidate.FullName);
        var candidateNamespace = candidate.Namespace ?? string.Empty;
        if (ForbiddenNamespacePrefixes.Any(prefix => candidateNamespace.StartsWith(prefix, StringComparison.Ordinal)) ||
            candidateNamespace.IndexOf(".Native", StringComparison.Ordinal) >= 0)
            violations.Add(location + " stores forbidden type " + candidate.FullName);
        if (candidate.Name.Contains("ConfigEntry", StringComparison.Ordinal) ||
            candidate.Name.EndsWith("Adapter", StringComparison.Ordinal))
            violations.Add(location + " stores forbidden boundary " + candidate.FullName);
        if (!candidate.IsGenericType) return;
        foreach (var argument in candidate.GetGenericArguments()) AuditForbiddenDependency(argument, location, violations);
    }

    private static void StaticVoidCall() =>
        UnityEngine.Resources.FindObjectsOfTypeAll(typeof(UnityEngine.Object));

    private static Type LegacyTypeToken() =>
        typeof(UnityEngine.Object);

    [ForbiddenTypeDependency(typeof(SuiteFramePump))]
    private static void ForbiddenTypeAttributeFixture() { }

    [ForbiddenTypeDependency(typeof(SuiteFramePump[][]))]
    private static void ForbiddenJaggedTypeAttributeFixture() { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    private sealed class ForbiddenTypeDependencyAttribute : Attribute
    {
        internal ForbiddenTypeDependencyAttribute(Type dependency) => Dependency = dependency;
        internal Type Dependency { get; }
    }
}
