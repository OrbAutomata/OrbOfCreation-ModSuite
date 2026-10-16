using System;
using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace OrbModding.IlInspect.Tests;

public sealed class IlInspectTests
{
    private static readonly string FixtureAssembly = typeof(IlInspectTests).Assembly.Location;

    [Fact]
    public void HeaderPinsResolvedPathSha256AndMvid()
    {
        using var inspector = AssemblyInspector.Open(FixtureAssembly);
        using var output = new StringWriter();

        inspector.WriteHeader(output);

        var text = output.ToString();
        Assert.Contains($"assembly: {Path.GetFullPath(FixtureAssembly)}", text);
        Assert.Contains($"sha256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FixtureAssembly))).ToLowerInvariant()}", text);
        Assert.Contains($"mvid: {typeof(IlInspectTests).Module.ModuleVersionId:D}", text);
    }

    [Fact]
    public void TypeShowsBaseInterfacesAndMemberModifiers()
    {
        var text = Inspect("type", typeof(DeclaredImplementation).FullName!);

        Assert.Contains($"type {NameOf<DeclaredImplementation>()}", text);
        Assert.Contains(NameOf<FixtureBase>(), text);
        Assert.Contains($"{NameOf<IFixtureContract>()} [declared]", text);
        Assert.Contains("FixtureVirtual(System.Int32)", text);
        Assert.Contains("public static readonly field", text);
        Assert.Contains("public virtual final method", text);
    }

    [Fact]
    public void MethodPrintsReadableIlAndFullMemberNames()
    {
        var text = Inspect("method", $"{typeof(DeclaredImplementation).FullName}::CallTarget");

        Assert.Contains($"method {NameOf<DeclaredImplementation>()}.CallTarget()", text);
        Assert.Contains($"{NameOf<DeclaredImplementation>()}.TargetMethod(System.Int32)", text);
        Assert.Contains($"{NameOf<DeclaredImplementation>()}.TargetField", text);
        Assert.Contains("fixture literal", text);
        Assert.Contains("IL_", text);
    }

    [Fact]
    public void CallersFindsMethodAndFieldReferenceSites()
    {
        var methodText = Inspect("callers", $"{typeof(DeclaredImplementation).FullName}::TargetMethod");
        var fieldText = Inspect("callers", $"{typeof(DeclaredImplementation).FullName}::TargetField");

        Assert.Contains($"{NameOf<DeclaredImplementation>()}.CallTarget()", methodText);
        Assert.Contains(" call ", methodText);
        Assert.Contains($"{NameOf<DeclaredImplementation>()}.CallTarget()", fieldText);
        Assert.Contains("ldsfld", fieldText);
    }

    [Fact]
    public void CallersReportVirtualOverrideRelationships()
    {
        var overrideText = Inspect("callers", $"{typeof(VirtualDerived).FullName}::Dispatch");
        var baseText = Inspect("callers", $"{typeof(VirtualBase).FullName}::Dispatch");

        Assert.Contains(
            $"note: {NameOf<VirtualDerived>()}.Dispatch() overrides {NameOf<VirtualBase>()}.Dispatch();",
            overrideText);
        Assert.DoesNotContain("  (none)", overrideText);
        Assert.Contains($"{NameOf(typeof(VirtualCaller))}.Call({NameOf<VirtualBase>()})", baseText);
        Assert.Contains($"note: override declaration: {NameOf<VirtualDerived>()}.Dispatch()", baseText);
    }

    [Fact]
    public void CallersResolveGenericInstanceTokensToOpenDefinitions()
    {
        var text = Inspect("callers", $"{typeof(GenericFixture<>).FullName}::Echo");

        Assert.Contains($"{NameOf(typeof(GenericCaller))}.Call()", text);
        Assert.Contains("GenericFixture`1<System.Int32>.Echo(T)", text);
    }

    [Fact]
    public void TypeDistinguishesConstAndReadonlyFields()
    {
        var text = Inspect("type", typeof(FieldModifierFixture).FullName!);

        Assert.Contains($"public const field {NameOf<FieldModifierFixture>()}.Constant", text);
        Assert.Contains($"public static readonly field {NameOf<FieldModifierFixture>()}.SharedReadonly", text);
        Assert.Contains($"public readonly field {NameOf<FieldModifierFixture>()}.InstanceReadonly", text);
    }

    [Fact]
    public void ImplementersDistinguishesDeclarationFromInheritance()
    {
        var text = Inspect("implementers", typeof(IFixtureContract).FullName!);

        Assert.Contains($"{NameOf<DeclaredImplementation>()} [declared]", text);
        Assert.Contains($"{NameOf<InheritedImplementation>()} [inherited]", text);
    }

    [Fact]
    public void StringsListsEachContainingMethod()
    {
        var text = Inspect("strings", "fixture literal");

        Assert.Contains($"{NameOf<DeclaredImplementation>()}.CallTarget()", text);
        Assert.Contains("\"fixture literal\"", text);
    }

    [Fact]
    public void CommandLineUsesEnvironmentAndRejectsAssemblyTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), $"il-inspect-{Guid.NewGuid():N}");
        var managed = Path.Combine(root, "Orb Of Creation_Data", "Managed");
        Directory.CreateDirectory(managed);
        var target = Path.Combine(managed, "Assembly-CSharp.dll");
        File.Copy(FixtureAssembly, target);
        var alternateTarget = Path.Combine(managed, "Fixture.dll");
        File.Copy(FixtureAssembly, alternateTarget);
        try
        {
            var command = CommandLine.Parse(
                new[] { "strings", "fixture" },
                () => root);
            Assert.Equal(target, command.AssemblyPath);

            var alternate = CommandLine.Parse(
                new[] { "--game-dir", root, "--assembly", "Fixture.dll", "strings", "fixture" },
                () => null);
            Assert.Equal(alternateTarget, alternate.AssemblyPath);

            Assert.Throws<CommandLineException>(() => CommandLine.Parse(
                new[] { "--game-dir", root, "--assembly", "../other.dll", "strings", "fixture" },
                () => null));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A DLL the game ships outside <c>Managed</c> is named by its path from the game directory and
    /// inspected where it actually lives.
    /// </summary>
    /// <remarks>
    /// BepInEx ships under <c>BepInEx/core</c>, and <c>--assembly</c> used to mean "one file name
    /// directly under Managed" — so pinning a BepInEx contract needed a directory named
    /// <c>Managed</c> symlinked over <c>BepInEx/core</c>, which is a workaround standing between
    /// the inspector and the evidence it exists to produce. References still resolve from Managed,
    /// because that is where such an assembly's engine and game dependencies are.
    /// </remarks>
    [Fact]
    public void AnAssemblyTheGameShipsOutsideManagedIsInspectedWhereItLives()
    {
        var root = Path.Combine(Path.GetTempPath(), $"il-inspect-{Guid.NewGuid():N}");
        var managed = Path.Combine(root, "Orb Of Creation_Data", "Managed");
        var core = Path.Combine(root, "BepInEx", "core");
        Directory.CreateDirectory(managed);
        Directory.CreateDirectory(core);
        File.Copy(FixtureAssembly, Path.Combine(managed, "Assembly-CSharp.dll"));
        var shipped = Path.Combine(core, "BepInEx.dll");
        File.Copy(FixtureAssembly, shipped);
        try
        {
            var command = CommandLine.Parse(
                new[]
                {
                    "--game-dir", root,
                    "--assembly", "BepInEx/core/BepInEx.dll",
                    "strings", "fixture literal",
                },
                () => null);

            Assert.Equal(shipped, command.AssemblyPath);
            Assert.Equal(managed, command.ManagedDirectory);

            using var inspector = AssemblyInspector.Open(
                command.AssemblyPath, command.ManagedDirectory);
            using var output = new StringWriter();
            inspector.WriteHeader(output);
            inspector.Execute(command.Verb, command.Query, output);

            var text = output.ToString();
            Assert.Contains($"assembly: {shipped}", text);
            Assert.Contains("\"fixture literal\"", text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Inspect(string verb, string query)
    {
        using var inspector = AssemblyInspector.Open(FixtureAssembly);
        using var output = new StringWriter();
        inspector.Execute(verb, query, output);
        return output.ToString();
    }

    private static string NameOf<T>() => typeof(T).FullName!.Replace('+', '.');

    private static string NameOf(Type type) => type.FullName!.Replace('+', '.');

    private interface IFixtureContract
    {
    }

    private abstract class FixtureBase
    {
        public virtual int FixtureVirtual(int value) => value;
    }

    private class DeclaredImplementation : FixtureBase, IFixtureContract
    {
        public static readonly int TargetField = 3;

        public sealed override int FixtureVirtual(int value) => value;

        public int TargetMethod(int value) => value + 1;

        public int CallTarget()
        {
            Consume("fixture literal");
            return TargetMethod(TargetField);
        }

        private static void Consume(string value) => _ = value.Length;
    }

    private sealed class InheritedImplementation : DeclaredImplementation
    {
    }

    private abstract class VirtualBase
    {
        public abstract int Dispatch();
    }

    private sealed class VirtualDerived : VirtualBase
    {
        public override int Dispatch() => 7;
    }

    private static class VirtualCaller
    {
        public static int Call(VirtualBase value) => value.Dispatch();
    }

    private sealed class GenericFixture<T>
    {
        public T Echo(T value) => value;
    }

    private static class GenericCaller
    {
        public static int Call() => new GenericFixture<int>().Echo(11);
    }

    private sealed class FieldModifierFixture
    {
        public const int Constant = 1;
        public static readonly int SharedReadonly = 2;
        public readonly int InstanceReadonly = 3;
    }
}
