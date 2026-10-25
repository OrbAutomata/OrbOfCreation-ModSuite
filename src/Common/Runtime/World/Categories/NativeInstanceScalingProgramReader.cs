using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace OrbModding.Common.Runtime.World;

internal readonly struct WorldInstanceScalingBasis
{
    internal WorldInstanceScalingBasis(
        Guid scalingId,
        bool costUsesRarity,
        bool speedUsesRarity)
    {
        ScalingId = scalingId;
        CostUsesRarity = costUsesRarity;
        SpeedUsesRarity = speedUsesRarity;
    }
    internal Guid ScalingId { get; }
    internal bool CostUsesRarity { get; }
    internal bool SpeedUsesRarity { get; }
}

/// <summary>One shared capture of the instance-scaling programs used by every Alchemy recipe.</summary>
internal sealed class NativeInstanceScalingProgramReader
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Func<object, object?>? _scaling;
    private readonly Func<object, Guid>? _id;
    private readonly Func<object, bool>? _useRarity;
    private readonly Func<object, IList?>? _blacklist;
    private readonly Func<object, object?>? _conversion;
    private readonly Func<object, object?>? _values;
    private readonly NativeModifierProgramReader _programs;

    internal NativeInstanceScalingProgramReader(Func<string, Type?> resolve)
    {
        var reference = resolve("InstanceScalingRef");
        var scaling = resolve("InstanceScalingSO");
        _scaling = NativeAccessorBinder.Reference(reference, "scaling");
        _id = NativeAccessorBinder.StableUuid(scaling);
        _useRarity = NativeAccessorBinder.Field<bool>(scaling, "useRarity");
        _blacklist = NativeAccessorBinder.CollectionField(scaling, "rarityAttributeBlacklist");
        _conversion = NativeAccessorBinder.Reference(scaling, "instanceScaling");
        var conversion = scaling?.GetField("instanceScaling", Instance)?.FieldType;
        _values = NativeAccessorBinder.ReadValue(FindField(conversion, "values"));
        _programs = new NativeModifierProgramReader(resolve("ValueModifierRecord"), resolve("ValueModifierList"));
    }

    internal bool IsAvailable => _scaling is not null && _id is not null && _useRarity is not null &&
        _blacklist is not null && _conversion is not null && _values is not null && _programs.IsAvailable;

    internal WorldInstanceScalingBasis Capture(object reference, HashSet<Guid> captured,
        WorldRelationBuffer<WorldModifierProgram> programs, WorldRelationBuffer<WorldModifierProgramEntry> entries)
    {
        var scaling = _scaling!(reference) ?? throw new InvalidOperationException("instanceScaling.scaling was null");
        var id = _id!(scaling);
        if (id == Guid.Empty) throw new InvalidOperationException("instanceScaling carried no identity");
        var useRarity = _useRarity!(scaling);
        var blacklist = _blacklist!(scaling);
        var basis = new WorldInstanceScalingBasis(id,
            useRarity && !Contains(blacklist, 4), useRarity && !Contains(blacklist, 6));
        if (!captured.Add(id)) return basis;
        var conversion = _conversion!(scaling) ?? throw new InvalidOperationException("instanceScaling.instanceScaling was null");
        if (_values!(conversion) is not IDictionary values)
            throw new InvalidOperationException("instanceScaling.instanceScaling.values was not a dictionary");
        _programs.CaptureList(id, WorldModifierProgramRole.InstanceScalingCost, List(values, 4), programs, entries);
        _programs.CaptureList(id, WorldModifierProgramRole.InstanceScalingSpeed, List(values, 6), programs, entries);
        return basis;
    }

    private static bool Contains(IList? values, int kind)
    {
        for (var i = 0; i < (values?.Count ?? 0); i++)
            if (values![i] is not null && Convert.ToInt32(values[i]) == kind) return true;
        return false;
    }

    private static object? List(IDictionary source, int kind)
    {
        foreach (DictionaryEntry pair in source)
            if (pair.Key is not null && Convert.ToInt32(pair.Key) == kind) return pair.Value;
        return null;
    }

    private static FieldInfo? FindField(Type? owner, string name)
    {
        while (owner is not null)
        {
            var field = owner.GetField(name, Instance | BindingFlags.DeclaredOnly);
            if (field is not null) return field;
            owner = owner.BaseType;
        }
        return null;
    }
}
