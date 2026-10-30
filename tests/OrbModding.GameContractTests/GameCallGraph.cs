using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace OrbModding.GameContractTests;

/// <summary>
/// Every call one game method can make, read off the IL of the whole assembly, and the methods a
/// set of roots can reach through it.
/// </summary>
/// <remarks>
/// <para>
/// The edges are the operands of <c>call</c>, <c>callvirt</c>, <c>newobj</c>, <c>ldftn</c> and
/// <c>ldvirtftn</c>. A delegate is reached where it is made rather than where it is invoked, which
/// is why <c>ldftn</c> counts: a tooltip row's computed text is a lambda the row builder loads and
/// the panel invokes later.
/// </para>
/// <para>
/// Dispatch is resolved wide on purpose. A call through a virtual or interface slot reaches every
/// method of that name on every type below the slot's owner, and a compiler-generated type
/// (an iterator, a closure) is reached whole once any of its members is. Over-approximating is
/// the safe direction for a question of the form "can this reach X": a false path costs a reading
/// of the IL, a missing one costs the claim.
/// </para>
/// </remarks>
internal sealed class GameCallGraph
{
    private readonly Dictionary<int, Method> _methods = new();
    private readonly Dictionary<(string Type, string Name), List<int>> _byName = new();
    private readonly Dictionary<string, List<int>> _byType = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _baseOf = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _below = new(StringComparer.Ordinal);
    private readonly Dictionary<int, List<Callee>> _calls = new();

    private GameCallGraph()
    {
    }

    internal int Reached { get; private set; }

    internal static GameCallGraph Build(GameAssemblyMetadata assembly)
    {
        var graph = new GameCallGraph();
        var reader = assembly.Reader;
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            var typeName = assembly.FullTypeName(typeHandle);
            if (!type.BaseType.IsNil)
            {
                var baseName = Definition(assembly.TypeName(type.BaseType));
                graph._baseOf[typeName] = baseName;
                graph.Below(baseName).Add(typeName);
            }
            foreach (var implementation in type.GetInterfaceImplementations())
            {
                var face = reader.GetInterfaceImplementation(implementation).Interface;
                graph.Below(Definition(assembly.TypeName(face))).Add(typeName);
            }
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                var token = MetadataTokens.GetToken(methodHandle);
                var name = reader.GetString(method.Name);
                graph._methods[token] = new Method(
                    token, typeName, name, (method.Attributes & MethodAttributes.Virtual) != 0);
                graph.Index(graph._byName, (typeName, name), token);
                graph.Index(graph._byType, typeName, token);
                graph._calls[token] = Callees(assembly, assembly.GetIlBytes(methodHandle));
            }
        }
        return graph;
    }

    /// <summary>The roots: every method of these names on every type at or below this one.</summary>
    internal IReadOnlyList<int> MethodsBelow(string type, IReadOnlyCollection<string> names)
    {
        var roots = new List<int>();
        foreach (var owner in TypesAtOrBelow(type))
        {
            foreach (var name in names)
                if (_byName.TryGetValue((owner, name), out var found)) roots.AddRange(found);
        }
        return roots;
    }

    /// <summary>Every call site of a method by its declared owner and name, as "Caller.Method", one entry per site.</summary>
    internal IReadOnlyList<string> CallSitesOf(string type, string name) =>
        _calls.SelectMany(calls => calls.Value
                .Where(callee => callee.Type == type && callee.Name == name)
                .Select(_ => _methods[calls.Key].Type + "." + _methods[calls.Key].Name))
            .OrderBy(caller => caller, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Every call a reachable method makes that the predicate calls a side effect, with the chain
    /// of methods from a root that leads to it.
    /// </summary>
    internal IReadOnlyList<string> PathsTo(IEnumerable<int> roots, Func<string, string, bool> sink)
    {
        var parent = new Dictionary<int, int>();
        var queue = new Queue<int>();
        foreach (var root in roots)
        {
            if (parent.ContainsKey(root)) continue;
            parent[root] = -1;
            queue.Enqueue(root);
        }
        var paths = new List<string>();
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var callee in _calls[current])
            {
                if (sink(callee.Type, callee.Name))
                    paths.Add(Chain(parent, current) + " -> " + callee.Type + "." + callee.Name);
                foreach (var next in Dispatch(callee))
                {
                    if (parent.ContainsKey(next)) continue;
                    parent[next] = current;
                    queue.Enqueue(next);
                }
            }
        }
        Reached = parent.Count;
        return paths.Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    private IEnumerable<int> Dispatch(Callee callee)
    {
        var found = new HashSet<int>();
        if (callee.Definition is { } direct && _methods.TryGetValue(direct, out var method))
        {
            found.Add(direct);
            if (method.Virtual) AddBelow(found, method.Type, method.Name);
            AddGenerated(found, method.Type);
        }
        else
        {
            for (var owner = callee.Type; owner is not null; owner = _baseOf.GetValueOrDefault(owner))
            {
                if (_byName.TryGetValue((owner, callee.Name), out var declared))
                {
                    foreach (var token in declared) found.Add(token);
                    break;
                }
            }
            if (_byType.ContainsKey(callee.Type)) AddBelow(found, callee.Type, callee.Name);
        }
        return found;
    }

    private void AddBelow(HashSet<int> found, string type, string name)
    {
        foreach (var owner in TypesAtOrBelow(type))
            if (_byName.TryGetValue((owner, name), out var tokens))
                foreach (var token in tokens) found.Add(token);
    }

    private void AddGenerated(HashSet<int> found, string type)
    {
        if (!type.Contains(">d__", StringComparison.Ordinal)) return;
        if (_byType.TryGetValue(type, out var tokens))
            foreach (var token in tokens) found.Add(token);
    }

    private IEnumerable<string> TypesAtOrBelow(string type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { type };
        var pending = new Stack<string>();
        pending.Push(type);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;
            if (!_below.TryGetValue(current, out var derived)) continue;
            foreach (var next in derived)
                if (seen.Add(next)) pending.Push(next);
        }
    }

    private string Chain(Dictionary<int, int> parent, int token)
    {
        var steps = new List<string>();
        for (var current = token; current != -1; current = parent[current])
            steps.Add(_methods[current].Type + "." + _methods[current].Name);
        steps.Reverse();
        return string.Join(" -> ", steps);
    }

    private static List<Callee> Callees(GameAssemblyMetadata assembly, byte[]? il)
    {
        var callees = new List<Callee>();
        if (il is null) return callees;
        for (var offset = 0; offset < il.Length; offset++)
        {
            int operand;
            if (il[offset] is 0x28 or 0x6F or 0x73) operand = offset + 1;
            else if (il[offset] == 0xFE && offset + 1 < il.Length && il[offset + 1] is 0x06 or 0x07)
                operand = offset + 2;
            else continue;
            if (operand + 4 > il.Length) continue;
            var token = il[operand] | (il[operand + 1] << 8) | (il[operand + 2] << 16) | (il[operand + 3] << 24);
            if (TryCallee(assembly, token, out var callee)) callees.Add(callee);
        }
        return callees;
    }

    private static bool TryCallee(GameAssemblyMetadata assembly, int token, out Callee callee)
    {
        callee = default;
        var reader = assembly.Reader;
        var table = token >> 24;
        var row = token & 0xFFFFFF;
        if (row == 0) return false;
        switch (table)
        {
            case 0x06 when row <= reader.GetTableRowCount(TableIndex.MethodDef):
            {
                var handle = MetadataTokens.MethodDefinitionHandle(row);
                var method = reader.GetMethodDefinition(handle);
                callee = new Callee(
                    token,
                    assembly.FullTypeName(method.GetDeclaringType()),
                    reader.GetString(method.Name));
                return true;
            }
            case 0x0A when row <= reader.GetTableRowCount(TableIndex.MemberRef):
            {
                var member = reader.GetMemberReference(MetadataTokens.MemberReferenceHandle(row));
                if (member.GetKind() != MemberReferenceKind.Method) return false;
                callee = new Callee(null, Definition(assembly.TypeName(member.Parent)), reader.GetString(member.Name));
                return true;
            }
            case 0x2B when row <= reader.GetTableRowCount(TableIndex.MethodSpec):
            {
                var spec = reader.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(row));
                return TryCallee(assembly, MetadataTokens.GetToken(spec.Method), out callee);
            }
            default:
                return false;
        }
    }

    private static string Definition(string typeName)
    {
        var generic = typeName.IndexOf('<', StringComparison.Ordinal);
        if (generic > 0 && typeName.IndexOf('`', StringComparison.Ordinal) is var tick && tick > 0 && tick < generic)
            return typeName.Substring(0, generic);
        return typeName;
    }

    private List<string> Below(string type)
    {
        if (!_below.TryGetValue(type, out var list)) _below[type] = list = new List<string>();
        return list;
    }

    private void Index<TKey>(Dictionary<TKey, List<int>> index, TKey key, int token)
        where TKey : notnull
    {
        if (!index.TryGetValue(key, out var list)) index[key] = list = new List<int>();
        list.Add(token);
    }

    private sealed record Method(int Token, string Type, string Name, bool Virtual);

    private readonly record struct Callee(int? Definition, string Type, string Name);
}
