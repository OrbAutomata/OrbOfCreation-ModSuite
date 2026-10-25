using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services;

public sealed class FeatureDependencyTests
{
    private const BindingFlags DeclaredMembers = BindingFlags.Public | BindingFlags.NonPublic |
                                                 BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private static readonly Assembly Production = typeof(SuiteRuntimeConfiguration).Assembly;
    private static readonly ProductionTypeSources Sources = new();

    // This is the deliberate composition schema, not permission to call another feature's engine.
    // Spell Leveling consumes Auto Buy's options through this same suite publication.
    private static readonly HashSet<Type> ConfigurationSections = typeof(SuiteRuntimeConfiguration)
        .GetProperties(DeclaredMembers).Select(property => property.PropertyType).ToHashSet();

    [Fact]
    public void EveryProductionTypeHasUnambiguousSourceOwnership()
    {
        foreach (var type in Production.GetTypes().Where(type => type.DeclaringType is null &&
                     !type.IsDefined(typeof(CompilerGeneratedAttribute), false)))
            Assert.NotNull(Sources.Module(type));
    }

    [Fact]
    public void CommonInfrastructureAndFeatureServiceCycleCodeDoNotDependOnOtherFeatures()
    {
        var modules = FeatureModules();
        var violations = new List<string>();
        foreach (var type in Production.GetTypes())
        {
            var module = Sources.Module(type);
            if (module != "Common" && (module is null || !modules.Contains(module))) continue;
            // Root feature contributions and UI controls compose other owners deliberately. This
            // rule protects the shared infrastructure and each feature's ServiceCycle machinery.
            if (module != "Common" && !IsOrdinaryWorker(type) && !Sources.Files(type).Any(path =>
                    path.Contains("/ServiceCycle/", StringComparison.Ordinal) &&
                    !path.Contains("/Composition/", StringComparison.Ordinal))) continue;
            // This one aggregate is the explicit root of the composed configuration. Its defaults
            // holder refers only to the aggregate; all other Common code obeys the dependency rule.
            if (type == typeof(SuiteRuntimeConfiguration)) continue;
            Audit(type, candidate => IsForbiddenModuleReference(module!, candidate, modules), violations);
        }
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations.Distinct()));
    }

    [Fact]
    public void OrdinaryFeatureWorkersAndTheirApplicationAndDomainCodeDoNotTouchNativeApis()
    {
        var modules = FeatureModules();
        var workers = Production.GetTypes().Where(IsOrdinaryWorker).ToHashSet();
        Assert.NotEmpty(workers);
        var audited = Production.GetTypes().Where(type => IsWorkerOrNested(type, workers) ||
            modules.Contains(Sources.Module(type) ?? string.Empty) && Sources.Files(type).Any(path =>
                path.Contains("/Application/", StringComparison.Ordinal) ||
                path.Contains("/Domain/", StringComparison.Ordinal))).ToArray();
        Assert.Contains(audited, type => type.Name == "AutoScribeCycleEvaluator");
        var violations = new List<string>();
        foreach (var type in audited) Audit(type, IsNativeType, violations);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations.Distinct()));
    }

    [Theory]
    [InlineData(nameof(UnityStaticCall), "UnityEngine.Resources")]
    [InlineData(nameof(GameTypeToken), "AlchemyInstance")]
    public void WorkerRuleDetectsFieldFreeNativeDependencies(string methodName, string expected)
    {
        var method = typeof(FeatureDependencyTests).GetMethod(methodName, DeclaredMembers)!;
        var violations = new List<string>();
        ServiceCycleIlDependencyScanner.Audit(method, method.GetMethodBody()!, methodName, violations, IsNativeType);
        Assert.Contains(violations, violation => violation.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Common")]
    [InlineData("AutoItems")]
    public void ModuleRuleDetectsActualFeatureDependenciesDespiteSharedNamespace(string sourceModule)
    {
        var violations = new List<string>();
        var modules = FeatureModules();
        Audit(typeof(FeatureDependencyFixture), candidate =>
            IsForbiddenModuleReference(sourceModule, candidate, modules), violations);
        Assert.Contains(violations, violation => violation.Contains("AutoBuyWorkerDefinition", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("AutoBuyCycleAction", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("AutoScribeWorkerDefinition", StringComparison.Ordinal));
    }

    [Fact]
    public void ModuleRuleAllowsOwnFeatureAndExplicitConfigurationCompositionContracts()
    {
        var modules = FeatureModules();
        Assert.False(IsNativeType(typeof(BigDouble)));
        Assert.False(IsForbiddenModuleReference("AutoBuy", typeof(OrbAutomata.AutoBuyWorkerDefinition), modules));
        Assert.False(IsForbiddenModuleReference("SpellLeveling", typeof(OrbAutomata.AutoBuyConfiguration), modules));
        Assert.True(IsForbiddenModuleReference("SpellLeveling", typeof(OrbAutomata.AutoBuyWorkerDefinition), modules));
    }

    private HashSet<string> FeatureModules() => Production.GetTypes().Where(IsOrdinaryWorker)
        .Select(type => Sources.Module(type) ?? throw new InvalidOperationException("Unmapped worker: " + type.FullName))
        .Where(module => module is not "Common" and not "Automata" and not "<root>")
        .ToHashSet(StringComparer.Ordinal);

    private static bool IsWorkerOrNested(Type type, HashSet<Type> workers) =>
        workers.Contains(type) || type.DeclaringType is not null && IsWorkerOrNested(type.DeclaringType, workers);

    private static bool IsOrdinaryWorker(Type type) => type.GetInterfaces().Any(contract =>
        contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IServiceCycleWorkerDefinition<,>));

    private bool IsForbiddenModuleReference(string sourceModule, Type candidate, HashSet<string> featureModules)
    {
        if (candidate.Assembly != Production || ConfigurationSections.Contains(candidate)) return false;
        var target = Sources.Module(candidate);
        return target is not null && featureModules.Contains(target) && target != sourceModule;
    }

    private static bool IsNativeType(Type type)
    {
        // Portable stubs place this game-independent numeric value beside the game types.
        if (type == typeof(BigDouble)) return false;
        var assembly = type.Assembly.GetName().Name ?? string.Empty;
        var ns = type.Namespace ?? string.Empty;
        return assembly is "Assembly-CSharp" or "Assembly-CSharp-firstpass" or "BepInEx" or "0Harmony" ||
               assembly.StartsWith("UnityEngine", StringComparison.Ordinal) ||
               ns is "UnityEngine" or "BepInEx" or "HarmonyLib" ||
               ns.StartsWith("UnityEngine.", StringComparison.Ordinal) ||
               ns.StartsWith("BepInEx.", StringComparison.Ordinal) ||
               ns.StartsWith("HarmonyLib.", StringComparison.Ordinal) ||
               type.Assembly == typeof(UnityEngine.Object).Assembly && ns.Length == 0;
    }

    private static void Audit(Type type, Func<Type, bool> forbidden, ICollection<string> violations)
    {
        void Reference(Type candidate, string location) =>
            ServiceCycleIlDependencyScanner.AuditType(candidate, location, violations, forbidden);
        var owner = type.FullName ?? type.Name;
        if (type.BaseType is not null) Reference(type.BaseType, owner + " base");
        foreach (var contract in type.GetInterfaces()) Reference(contract, owner + " interface");
        foreach (var field in type.GetFields(DeclaredMembers)) Reference(field.FieldType, owner + "." + field.Name);
        var methods = type.GetMethods(DeclaredMembers).Cast<MethodBase>().Concat(type.GetConstructors(DeclaredMembers));
        if (type.TypeInitializer is not null) methods = methods.Append(type.TypeInitializer);
        foreach (var method in methods.Distinct())
        {
            var location = owner + "." + method.Name;
            if (method is MethodInfo info) Reference(info.ReturnType, location + " return");
            foreach (var parameter in method.GetParameters()) Reference(parameter.ParameterType, location + " parameter");
            var body = method.GetMethodBody();
            if (body is null) continue;
            foreach (var local in body.LocalVariables) Reference(local.LocalType, location + " local");
            ServiceCycleIlDependencyScanner.Audit(method, body, location, violations, forbidden);
        }
    }

    private static void UnityStaticCall() => UnityEngine.Resources.FindObjectsOfTypeAll(typeof(UnityEngine.Object));
    private static Type GameTypeToken() => typeof(global::AlchemyInstance);

    private sealed class FeatureDependencyFixture
    {
        // A signature/storage dependency, including a generic and array wrapper, and a field-free
        // executable dependency. Both go through the same scanner used on production types above.
        internal List<OrbAutomata.AutoBuyCycleAction[][]> Actions = new();
        internal static object Create() => new OrbAutomata.AutoBuyWorkerDefinition();
        internal static Type Scribe() => typeof(OrbAutomata.AutoScribeWorkerDefinition);
    }
}
