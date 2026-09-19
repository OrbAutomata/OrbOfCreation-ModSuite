using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace OrbModding.IlInspect;

internal sealed class AssemblyInspector : IDisposable
{
    private readonly string assemblyPath;
    private readonly string sha256;
    private readonly ReadOnlyAssemblyResolver resolver;
    private readonly AssemblyDefinition assembly;

    private AssemblyInspector(
        string assemblyPath,
        string sha256,
        ReadOnlyAssemblyResolver resolver,
        AssemblyDefinition assembly)
    {
        this.assemblyPath = assemblyPath;
        this.sha256 = sha256;
        this.resolver = resolver;
        this.assembly = assembly;
    }

    /// <summary>
    /// Opens one assembly read-only. References resolve from the assembly's own directory first and
    /// then from <paramref name="managedDirectory"/>, which is what lets an assembly the game ships
    /// outside Managed — BepInEx, a plugin — still find the engine and game assemblies it was
    /// compiled against.
    /// </summary>
    internal static AssemblyInspector Open(string path, string? managedDirectory = null)
    {
        var resolvedPath = Path.GetFullPath(path);
        string hash;
        using (var hashStream = OpenReadShared(resolvedPath))
        {
            hash = Convert.ToHexString(SHA256.HashData(hashStream)).ToLowerInvariant();
        }

        var resolver = new ReadOnlyAssemblyResolver(
            Path.GetDirectoryName(resolvedPath)!, managedDirectory);
        try
        {
            var assembly = resolver.ReadTarget(resolvedPath);
            return new AssemblyInspector(resolvedPath, hash, resolver, assembly);
        }
        catch
        {
            resolver.Dispose();
            throw;
        }
    }

    internal void WriteHeader(TextWriter output)
    {
        output.WriteLine($"assembly: {assemblyPath}");
        output.WriteLine($"sha256: {sha256}");
        output.WriteLine($"mvid: {assembly.MainModule.Mvid:D}");
        output.WriteLine();
    }

    internal void Execute(string verb, string query, TextWriter output)
    {
        switch (verb)
        {
            case "type":
                WriteType(query, output);
                break;
            case "method":
                WriteMethod(query, output);
                break;
            case "callers":
                WriteCallers(query, output);
                break;
            case "implementers":
                WriteImplementers(query, output);
                break;
            case "strings":
                WriteStrings(query, output);
                break;
            default:
                throw new InvalidOperationException($"Unsupported query verb: {verb}");
        }
    }

    public void Dispose() => resolver.Dispose();

    private void WriteType(string query, TextWriter output)
    {
        var type = FindType(query);
        output.WriteLine($"type {Names.Type(type)}");
        output.WriteLine("base-chain:");
        var baseType = type.BaseType;
        while (baseType is not null)
        {
            output.WriteLine($"  {Names.Type(baseType)}");
            if (!TryResolve(baseType, out var resolvedBase))
            {
                output.WriteLine("  [base definition unresolved]");
                break;
            }
            baseType = resolvedBase.BaseType;
        }

        output.WriteLine("interfaces:");
        var interfaces = AllInterfaces(type)
            .OrderBy(item => Names.Type(item.Interface), StringComparer.Ordinal)
            .ThenBy(item => item.Declared ? 0 : 1)
            .ToArray();
        if (interfaces.Length == 0)
        {
            output.WriteLine("  (none)");
        }
        foreach (var item in interfaces)
        {
            output.WriteLine(
                $"  {Names.Type(item.Interface)} [{(item.Declared ? "declared" : "inherited")}]");
        }

        output.WriteLine("members:");
        foreach (var field in type.Fields.OrderBy(field => field.Name, StringComparer.Ordinal))
        {
            output.WriteLine($"  {Describe(field)}");
        }
        foreach (var property in type.Properties.OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            output.WriteLine($"  {Describe(property)}");
        }
        foreach (var @event in type.Events.OrderBy(@event => @event.Name, StringComparer.Ordinal))
        {
            output.WriteLine($"  {Describe(@event)}");
        }
        foreach (var method in type.Methods.OrderBy(method => method.Name, StringComparer.Ordinal)
                     .ThenBy(Names.Method, StringComparer.Ordinal))
        {
            output.WriteLine($"  {Describe(method)}");
        }
    }

    private void WriteMethod(string query, TextWriter output)
    {
        var (type, memberName) = FindMemberOwner(query);
        var methods = type.Methods.Where(method => method.Name == memberName).ToArray();
        if (methods.Length == 0)
        {
            throw new InvalidOperationException($"Method was not found: {query}");
        }

        foreach (var method in methods.OrderBy(Names.Method, StringComparer.Ordinal))
        {
            output.WriteLine($"method {Names.Method(method)} : {Names.Type(method.ReturnType)}");
            if (!method.HasBody)
            {
                output.WriteLine("  (no IL body)");
                output.WriteLine();
                continue;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                output.WriteLine(
                    $"  IL_{instruction.Offset:X4}: {instruction.OpCode.Name} {FormatOperand(instruction.Operand)}".TrimEnd());
            }
            output.WriteLine();
        }
    }

    private void WriteCallers(string query, TextWriter output)
    {
        var (targetType, memberName) = FindMemberOwner(query);
        var targetTypeName = targetType.FullName;
        var targetMethods = targetType.Methods
            .Where(method => method.Name == memberName)
            .ToHashSet();
        var targetFields = targetType.Fields
            .Where(field => field.Name == memberName)
            .ToHashSet();
        var hits = new List<(MethodDefinition Method, Instruction Instruction, string Target)>();
        var resolvedMethodSiteCounts = new Dictionary<MethodDefinition, int>();

        foreach (var method in AllTypes().SelectMany(type => type.Methods).Where(method => method.HasBody))
        {
            foreach (var instruction in method.Body.Instructions)
            {
                switch (instruction.Operand)
                {
                    case MethodReference reference when IsCall(instruction.OpCode.Code):
                        if (TryResolve(reference, out var resolvedMethod))
                        {
                            resolvedMethodSiteCounts.TryGetValue(resolvedMethod, out var count);
                            resolvedMethodSiteCounts[resolvedMethod] = count + 1;
                            if (targetMethods.Contains(resolvedMethod))
                            {
                                hits.Add((method, instruction, Names.Method(reference)));
                            }
                        }
                        else if (reference.DeclaringType.FullName == targetTypeName &&
                                 reference.Name == memberName)
                        {
                            hits.Add((method, instruction, Names.Method(reference)));
                        }
                        break;
                    case FieldReference reference when IsFieldAccess(instruction.OpCode.Code):
                        if (TryResolve(reference, out var resolvedField))
                        {
                            if (targetFields.Contains(resolvedField))
                            {
                                hits.Add((method, instruction, Names.Field(reference)));
                            }
                        }
                        else if (reference.DeclaringType.FullName == targetTypeName &&
                                 reference.Name == memberName)
                        {
                            hits.Add((method, instruction, Names.Field(reference)));
                        }
                        break;
                }
            }
        }

        output.WriteLine($"callers {Names.Type(targetType)}.{memberName}");
        var notes = CallerNotes(targetMethods, resolvedMethodSiteCounts).ToArray();
        foreach (var note in notes)
        {
            output.WriteLine($"  note: {note}");
        }
        if (hits.Count == 0 && notes.Length == 0)
        {
            output.WriteLine("  (none)");
            return;
        }
        foreach (var hit in hits.OrderBy(hit => Names.Method(hit.Method), StringComparer.Ordinal)
                     .ThenBy(hit => hit.Instruction.Offset))
        {
            output.WriteLine(
                $"  {Names.Method(hit.Method)} — IL_{hit.Instruction.Offset:X4} " +
                $"{hit.Instruction.OpCode.Name} {hit.Target}");
        }
    }

    private IEnumerable<string> CallerNotes(
        IEnumerable<MethodDefinition> targetMethods,
        IReadOnlyDictionary<MethodDefinition, int> resolvedMethodSiteCounts)
    {
        foreach (var target in targetMethods.OrderBy(Names.Method, StringComparer.Ordinal))
        {
            var baseDeclaration = FindBaseDeclaration(target);
            if (baseDeclaration is not null)
            {
                resolvedMethodSiteCounts.TryGetValue(baseDeclaration, out var count);
                yield return
                    $"{Names.Method(target)} overrides {Names.Method(baseDeclaration)}; " +
                    $"{count} call {(count == 1 ? "site" : "sites")} in this assembly " +
                    "reference the base declaration.";
            }

            foreach (var @override in FindOverrides(target))
            {
                yield return $"override declaration: {Names.Method(@override)}";
            }
        }
    }

    private IEnumerable<MethodDefinition> FindOverrides(MethodDefinition declaration) =>
        AllTypes()
            .SelectMany(type => type.Methods)
            .Where(method => !ReferenceEquals(method, declaration) && Overrides(method, declaration))
            .OrderBy(Names.Method, StringComparer.Ordinal);

    private bool Overrides(MethodDefinition method, MethodDefinition declaration)
    {
        var visited = new HashSet<MethodDefinition>();
        var current = FindBaseDeclaration(method);
        while (current is not null && visited.Add(current))
        {
            if (ReferenceEquals(current, declaration))
            {
                return true;
            }
            current = FindBaseDeclaration(current);
        }
        return false;
    }

    private static MethodDefinition? FindBaseDeclaration(MethodDefinition method)
    {
        foreach (var explicitOverride in method.Overrides)
        {
            if (TryResolve(explicitOverride, out var resolved) && !resolved.DeclaringType.IsInterface)
            {
                return resolved;
            }
        }

        if (!method.IsVirtual || method.IsNewSlot)
        {
            return null;
        }

        var baseReference = method.DeclaringType.BaseType;
        while (baseReference is not null && TryResolve(baseReference, out var baseType))
        {
            var declaration = baseType.Methods.FirstOrDefault(candidate =>
                candidate.IsVirtual && MethodSlotsMatch(method, candidate));
            if (declaration is not null)
            {
                return declaration;
            }
            baseReference = baseType.BaseType;
        }
        return null;
    }

    private static bool MethodSlotsMatch(MethodDefinition method, MethodDefinition candidate) =>
        method.Name == candidate.Name &&
        method.GenericParameters.Count == candidate.GenericParameters.Count &&
        method.Parameters.Count == candidate.Parameters.Count &&
        method.Parameters.Zip(candidate.Parameters, (left, right) =>
                left.ParameterType.FullName == right.ParameterType.FullName)
            .All(matches => matches);

    private void WriteImplementers(string query, TextWriter output)
    {
        var target = FindType(query);
        if (!target.IsInterface)
        {
            throw new InvalidOperationException($"Type is not an interface: {Names.Type(target)}");
        }

        var implementations = AllTypes()
            .Where(type => !type.IsInterface && Implements(type, target.FullName))
            .Select(type => (Type: type, Declared: type.Interfaces.Any(
                implementation => implementation.InterfaceType.FullName == target.FullName)))
            .OrderBy(item => Names.Type(item.Type), StringComparer.Ordinal)
            .ToArray();

        output.WriteLine($"implementers {Names.Type(target)}");
        if (implementations.Length == 0)
        {
            output.WriteLine("  (none)");
            return;
        }
        foreach (var implementation in implementations)
        {
            output.WriteLine(
                $"  {Names.Type(implementation.Type)} " +
                $"[{(implementation.Declared ? "declared" : "inherited")}]");
        }
    }

    private void WriteStrings(string query, TextWriter output)
    {
        output.WriteLine($"strings {Quote(query)}");
        var hits = AllTypes()
            .SelectMany(type => type.Methods)
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions
                .Where(instruction => instruction.OpCode.Code == Code.Ldstr &&
                    instruction.Operand is string literal &&
                    literal.Contains(query, StringComparison.Ordinal))
                .Select(instruction => (Method: method, Instruction: instruction, Literal: (string)instruction.Operand)))
            .OrderBy(hit => Names.Method(hit.Method), StringComparer.Ordinal)
            .ThenBy(hit => hit.Instruction.Offset)
            .ToArray();

        if (hits.Length == 0)
        {
            output.WriteLine("  (none)");
            return;
        }
        foreach (var hit in hits)
        {
            output.WriteLine(
                $"  {Names.Method(hit.Method)} — IL_{hit.Instruction.Offset:X4} {Quote(hit.Literal)}");
        }
    }

    private TypeDefinition FindType(string query)
    {
        var normalized = query.Replace('+', '/');
        var matches = AllTypes().Where(type =>
            type.FullName == normalized ||
            Names.Type(type) == query ||
            type.Name == query).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Type was not found: {query}"),
            _ => throw new InvalidOperationException(
                $"Type name is ambiguous: {query}. Matches: " +
                string.Join(", ", matches.Select(Names.Type).Order(StringComparer.Ordinal))),
        };
    }

    private (TypeDefinition Type, string MemberName) FindMemberOwner(string query)
    {
        var separator = query.LastIndexOf("::", StringComparison.Ordinal);
        if (separator <= 0 || separator == query.Length - 2)
        {
            throw new InvalidOperationException($"Member query must use Type::Member: {query}");
        }
        return (FindType(query[..separator]), query[(separator + 2)..]);
    }

    private IEnumerable<TypeDefinition> AllTypes() =>
        assembly.Modules.SelectMany(module => module.Types).SelectMany(Flatten);

    private static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
    {
        yield return type;
        foreach (var nested in type.NestedTypes.SelectMany(Flatten))
        {
            yield return nested;
        }
    }

    private IEnumerable<(TypeReference Interface, bool Declared)> AllInterfaces(TypeDefinition type)
    {
        var results = new Dictionary<string, (TypeReference Interface, bool Declared)>(StringComparer.Ordinal);
        foreach (var implementation in type.Interfaces)
        {
            AddInterface(implementation.InterfaceType, declared: true, results);
        }

        var baseReference = type.BaseType;
        while (baseReference is not null && TryResolve(baseReference, out var baseType))
        {
            foreach (var implementation in baseType.Interfaces)
            {
                AddInterface(implementation.InterfaceType, declared: false, results);
            }
            baseReference = baseType.BaseType;
        }
        return results.Values;
    }

    private void AddInterface(
        TypeReference reference,
        bool declared,
        IDictionary<string, (TypeReference Interface, bool Declared)> results)
    {
        if (!results.TryGetValue(reference.FullName, out var existing) || declared && !existing.Declared)
        {
            results[reference.FullName] = (reference, declared);
        }
        if (!TryResolve(reference, out var definition))
        {
            return;
        }
        foreach (var inherited in definition.Interfaces)
        {
            AddInterface(inherited.InterfaceType, declared: false, results);
        }
    }

    private bool Implements(TypeDefinition type, string targetFullName)
    {
        if (type.Interfaces.Any(implementation =>
                implementation.InterfaceType.FullName == targetFullName ||
                InterfaceExtends(implementation.InterfaceType, targetFullName)))
        {
            return true;
        }
        return type.BaseType is not null &&
            TryResolve(type.BaseType, out var baseType) &&
            Implements(baseType, targetFullName);
    }

    private bool InterfaceExtends(TypeReference reference, string targetFullName)
    {
        if (!TryResolve(reference, out var definition))
        {
            return false;
        }
        return definition.Interfaces.Any(implementation =>
            implementation.InterfaceType.FullName == targetFullName ||
            InterfaceExtends(implementation.InterfaceType, targetFullName));
    }

    private static bool TryResolve(TypeReference reference, out TypeDefinition definition)
    {
        try
        {
            definition = reference.Resolve();
            return definition is not null;
        }
        catch (AssemblyResolutionException)
        {
            definition = null!;
            return false;
        }
    }

    private static bool TryResolve(MethodReference reference, out MethodDefinition definition)
    {
        try
        {
            definition = reference.Resolve();
            return definition is not null;
        }
        catch (Exception exception) when (
            exception is AssemblyResolutionException or ResolutionException)
        {
            definition = null!;
            return false;
        }
    }

    private static bool TryResolve(FieldReference reference, out FieldDefinition definition)
    {
        try
        {
            definition = reference.Resolve();
            return definition is not null;
        }
        catch (Exception exception) when (
            exception is AssemblyResolutionException or ResolutionException)
        {
            definition = null!;
            return false;
        }
    }

    private static bool IsCall(Code code) =>
        code is Code.Call or Code.Callvirt or Code.Newobj or Code.Jmp or Code.Ldftn or Code.Ldvirtftn;

    private static bool IsFieldAccess(Code code) =>
        code is Code.Ldfld or Code.Ldflda or Code.Stfld or Code.Ldsfld or Code.Ldsflda or Code.Stsfld;

    private static string Describe(FieldDefinition field) =>
        $"{Visibility(field)}{FieldModifiers(field)}field " +
        $"{Names.Field(field)} : {Names.Type(field.FieldType)}";

    private static string FieldModifiers(FieldDefinition field)
    {
        if (field.IsLiteral)
        {
            return " const ";
        }

        var modifiers = new List<string>();
        if (field.IsStatic) modifiers.Add("static");
        if (field.IsInitOnly) modifiers.Add("readonly");
        return modifiers.Count == 0 ? " " : " " + string.Join(" ", modifiers) + " ";
    }

    private static string Describe(PropertyDefinition property)
    {
        var accessors = new[] { property.GetMethod, property.SetMethod }.Where(method => method is not null).ToArray();
        var visibility = accessors.Length == 0 ? "private" : MostVisible(accessors!);
        var representative = accessors.FirstOrDefault();
        return $"{visibility}{Modifiers(representative?.IsStatic == true, representative?.IsAbstract == true, representative?.IsVirtual == true, representative?.IsFinal == true)}property " +
            $"{Names.Property(property)} : {Names.Type(property.PropertyType)}";
    }

    private static string Describe(EventDefinition @event)
    {
        var accessors = new[] { @event.AddMethod, @event.RemoveMethod }.Where(method => method is not null).ToArray();
        var visibility = accessors.Length == 0 ? "private" : MostVisible(accessors!);
        var representative = accessors.FirstOrDefault();
        return $"{visibility}{Modifiers(representative?.IsStatic == true, representative?.IsAbstract == true, representative?.IsVirtual == true, representative?.IsFinal == true)}event " +
            $"{Names.Event(@event)} : {Names.Type(@event.EventType)}";
    }

    private static string Describe(MethodDefinition method) =>
        $"{Visibility(method)}{Modifiers(method.IsStatic, method.IsAbstract, method.IsVirtual, method.IsFinal)}method " +
        $"{Names.Method(method)} : {Names.Type(method.ReturnType)}";

    private static string Visibility(FieldDefinition field) => (field.Attributes & FieldAttributes.FieldAccessMask) switch
    {
        FieldAttributes.Public => "public",
        FieldAttributes.Family => "protected",
        FieldAttributes.FamORAssem => "protected internal",
        FieldAttributes.Assembly => "internal",
        FieldAttributes.FamANDAssem => "private protected",
        _ => "private",
    };

    private static string Visibility(MethodDefinition method) => (method.Attributes & MethodAttributes.MemberAccessMask) switch
    {
        MethodAttributes.Public => "public",
        MethodAttributes.Family => "protected",
        MethodAttributes.FamORAssem => "protected internal",
        MethodAttributes.Assembly => "internal",
        MethodAttributes.FamANDAssem => "private protected",
        _ => "private",
    };

    private static string MostVisible(IEnumerable<MethodDefinition> methods) =>
        methods.Select(method => Visibility(method)).OrderBy(visibility => visibility switch
        {
            "public" => 0,
            "protected internal" => 1,
            "protected" => 2,
            "internal" => 3,
            "private protected" => 4,
            _ => 5,
        }).First();

    private static string Modifiers(bool isStatic, bool isAbstract, bool isVirtual, bool isFinal)
    {
        var modifiers = new List<string>();
        if (isStatic) modifiers.Add("static");
        if (isAbstract) modifiers.Add("abstract");
        if (isVirtual) modifiers.Add("virtual");
        if (isFinal) modifiers.Add("final");
        return modifiers.Count == 0 ? " " : " " + string.Join(" ", modifiers) + " ";
    }

    private static string FormatOperand(object? operand) => operand switch
    {
        null => string.Empty,
        MethodReference method => Names.Method(method),
        FieldReference field => Names.Field(field),
        TypeReference type => Names.Type(type),
        Instruction instruction => $"IL_{instruction.Offset:X4}",
        Instruction[] instructions => string.Join(", ", instructions.Select(item => $"IL_{item.Offset:X4}")),
        VariableDefinition variable => $"V_{variable.Index} ({Names.Type(variable.VariableType)})",
        ParameterDefinition parameter => parameter.Name,
        string text => Quote(text),
        IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
        _ => operand.ToString() ?? string.Empty,
    };

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static FileStream OpenReadShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private sealed class ReadOnlyAssemblyResolver : BaseAssemblyResolver
    {
        private readonly List<string> searchDirectories = new();
        private readonly Dictionary<string, AssemblyDefinition> assemblies =
            new(StringComparer.OrdinalIgnoreCase);

        internal ReadOnlyAssemblyResolver(string assemblyDirectory, string? managedDirectory)
        {
            searchDirectories.Add(assemblyDirectory);
            if (!string.IsNullOrEmpty(managedDirectory) &&
                !string.Equals(managedDirectory, assemblyDirectory, StringComparison.Ordinal))
            {
                searchDirectories.Add(managedDirectory);
            }
        }

        internal AssemblyDefinition ReadTarget(string path)
        {
            var assembly = Read(path);
            assemblies[assembly.Name.Name] = assembly;
            return assembly;
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            if (assemblies.TryGetValue(name.Name, out var assembly))
            {
                return assembly;
            }

            foreach (var directory in searchDirectories)
            {
                var path = Path.Combine(directory, name.Name + ".dll");
                if (!File.Exists(path)) continue;
                assembly = Read(path);
                assemblies[name.Name] = assembly;
                return assembly;
            }

            throw new AssemblyResolutionException(name);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var assembly in assemblies.Values.Distinct())
                {
                    assembly.Dispose();
                }
                assemblies.Clear();
            }
            base.Dispose(disposing);
        }

        private AssemblyDefinition Read(string path)
        {
            using var source = OpenReadShared(path);
            var image = new MemoryStream();
            source.CopyTo(image);
            image.Position = 0;
            try
            {
                return AssemblyDefinition.ReadAssembly(image, new ReaderParameters
                {
                    AssemblyResolver = this,
                    InMemory = true,
                    ReadingMode = ReadingMode.Deferred,
                });
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }
    }

    private static class Names
    {
        internal static string Type(TypeReference type) => type.FullName.Replace('/', '.');

        internal static string Method(MethodReference method) =>
            $"{Type(method.DeclaringType)}.{method.Name}(" +
            string.Join(", ", method.Parameters.Select(parameter => Type(parameter.ParameterType))) + ")";

        internal static string Field(FieldReference field) =>
            $"{Type(field.DeclaringType)}.{field.Name}";

        internal static string Property(PropertyReference property) =>
            $"{Type(property.DeclaringType)}.{property.Name}";

        internal static string Event(EventDefinition @event) =>
            $"{Type(@event.DeclaringType)}.{@event.Name}";
    }
}
