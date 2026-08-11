using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// Lifecycle-bound, read-only access to every entry of one plain native <c>ModifierRecord</c> field.
/// </summary>
/// <remarks>
/// This exists for diagnostic provenance, not for arithmetic. The game remains the evaluator:
/// <c>ResearchSO.GetRequirementLevel()</c> supplies the effective result. These rows explain which
/// direct passive and active operands were present and which tooltipable source supplied each one.
/// All reflection is resolved while the world category binds; the collection path executes compiled
/// delegates and never discovers a member at read time.
/// </remarks>
internal sealed class NativeModifierAdjustmentAccess
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Func<object, object?> _record;
    private readonly Func<object, int> _passiveCount;
    private readonly Func<object, int> _activeCount;
    private readonly Func<object, WorldResearchRequirementAdjustment[], int, int> _copyPassive;
    private readonly Func<object, WorldResearchRequirementAdjustment[], int, int> _copyActive;
    private WorldResearchRequirementAdjustment[] _scratch =
        new WorldResearchRequirementAdjustment[4];

    private NativeModifierAdjustmentAccess(
        Func<object, object?> record,
        Func<object, int> passiveCount,
        Func<object, int> activeCount,
        Func<object, WorldResearchRequirementAdjustment[], int, int> copyPassive,
        Func<object, WorldResearchRequirementAdjustment[], int, int> copyActive,
        string recordNativeType)
    {
        _record = record;
        _passiveCount = passiveCount;
        _activeCount = activeCount;
        _copyPassive = copyPassive;
        _copyActive = copyActive;
        RecordNativeType = recordNativeType;
    }

    /// <summary>
    /// The bound field's declared record class. A caller publishing many records at once needs it to
    /// tell a record that holds a value of its own from one that only distributes.
    /// </summary>
    internal string RecordNativeType { get; }

    internal static NativeModifierAdjustmentAccess? Bind(
        Type ownerType,
        string fieldName,
        out string failure)
    {
        failure = string.Empty;
        var recordField = ownerType.GetField(fieldName, Instance);
        var record = NativeAccessorBinder.Reference(ownerType, fieldName);
        if (recordField is null || recordField.FieldType.IsValueType || record is null)
        {
            failure = fieldName;
            return null;
        }

        var recordType = recordField.FieldType;
        var passiveCount = NativeAccessorBinder.CollectionCount(recordType, "passiveModifiers");
        var activeCount = NativeAccessorBinder.CollectionCount(recordType, "activeModifiers");
        var passive = NativeAccessorBinder.Reference(recordType, "passiveModifiers");
        var active = NativeAccessorBinder.Reference(recordType, "activeModifiers");
        var modifierType = DictionaryModifierType(recordType, "passiveModifiers");
        if (passiveCount is null || activeCount is null || passive is null || active is null ||
            modifierType is null ||
            modifierType != DictionaryModifierType(recordType, "activeModifiers"))
        {
            failure = fieldName + ".passiveModifiers/activeModifiers";
            return null;
        }

        var readType = EnumReader(modifierType, "type");
        var readOrder = FieldReader<int>(modifierType, "order");
        var readAmount = FieldReader<BigDouble>(modifierType, "adjustReal");
        var readSource = ReferenceReader(modifierType, "reference");
        var readSourceId = SourceIdentityReader(modifierType, "reference");
        if (readType is null || readOrder is null || readAmount is null || readSource is null ||
            readSourceId is null)
        {
            failure = fieldName + ".modifier(type/order/adjustReal/reference)";
            return null;
        }

        var copyPassive = Copier(
            modifierType,
            passive,
            readType,
            readOrder,
            readAmount,
            readSource,
            readSourceId,
            passive: true);
        var copyActive = Copier(
            modifierType,
            active,
            readType,
            readOrder,
            readAmount,
            readSource,
            readSourceId,
            passive: false);
        if (copyPassive is null || copyActive is null)
        {
            failure = fieldName + ".modifier copier";
            return null;
        }

        return new NativeModifierAdjustmentAccess(
            record,
            passiveCount,
            activeCount,
            copyPassive,
            copyActive,
            recordType.Name);
    }

    internal PublicationTable<WorldResearchRequirementAdjustment> Read(object owner) =>
        PublicationTable<WorldResearchRequirementAdjustment>.Create(ReadInto(owner));

    /// <summary>
    /// The owner's modifier entries, in a buffer this accessor owns and overwrites on the next call.
    /// For a caller appending into its own table, this is the same reading as <see cref="Read"/>
    /// without a publication allocation per record — which matters when the walk covers eighteen
    /// hundred records a pass rather than one per entity.
    /// </summary>
    internal ReadOnlySpan<WorldResearchRequirementAdjustment> ReadInto(object owner)
    {
        var record = _record(owner);
        if (record is null) return default;

        var total = _passiveCount(record) + _activeCount(record);
        if (total <= 0) return default;
        if (_scratch.Length < total)
            _scratch = new WorldResearchRequirementAdjustment[Math.Max(total, _scratch.Length * 2)];

        var written = _copyPassive(record, _scratch, 0);
        written = _copyActive(record, _scratch, written);
        return new ReadOnlySpan<WorldResearchRequirementAdjustment>(_scratch, 0, written);
    }

    /// <summary>
    /// How many modifiers sit on the owner's record right now, without reading the entries. False
    /// when the owner holds no record at all, which is a fact about the asset rather than a count.
    /// </summary>
    internal bool TryCount(object owner, out int passive, out int active)
    {
        passive = 0;
        active = 0;
        var record = _record(owner);
        if (record is null) return false;

        passive = _passiveCount(record);
        active = _activeCount(record);
        return true;
    }

    private static Type? DictionaryModifierType(Type recordType, string fieldName)
    {
        var field = recordType.GetField(fieldName, Instance)?.FieldType;
        if (field is not { IsGenericType: true }) return null;
        var arguments = field.GetGenericArguments();
        return arguments.Length == 2 && arguments[0] == typeof(Guid) && arguments[1].IsValueType
            ? arguments[1]
            : null;
    }

    private static Delegate? EnumReader(Type owner, string fieldName)
    {
        var field = owner.GetField(fieldName, Instance);
        if (field is null || !field.FieldType.IsEnum ||
            Enum.GetUnderlyingType(field.FieldType) != typeof(int))
        {
            return null;
        }

        var source = Expression.Parameter(owner, "modifier");
        var body = Expression.Convert(Expression.Field(source, field), typeof(int));
        return Compile(owner, typeof(int), body, source);
    }

    private static Delegate? FieldReader<TValue>(Type owner, string fieldName)
    {
        var field = owner.GetField(fieldName, Instance);
        if (field is null || field.FieldType != typeof(TValue)) return null;
        var source = Expression.Parameter(owner, "modifier");
        return Compile(owner, typeof(TValue), Expression.Field(source, field), source);
    }

    private static Delegate? ReferenceReader(Type owner, string fieldName)
    {
        var field = owner.GetField(fieldName, Instance);
        if (field is null || field.FieldType.IsValueType) return null;
        var source = Expression.Parameter(owner, "modifier");
        var body = Expression.Convert(Expression.Field(source, field), typeof(object));
        return Compile(owner, typeof(object), body, source);
    }

    private static Func<object, Guid>? SourceIdentityReader(Type modifierType, string fieldName)
    {
        var field = modifierType.GetField(fieldName, Instance);
        if (field is null || field.FieldType.IsValueType) return null;

        var sourceType = field.FieldType;
        var getGuid = sourceType.GetMethod("GetGuid", Instance, null, Type.EmptyTypes, null);
        Type? guidContract = null;
        if (getGuid is null || getGuid.ReturnType != typeof(Guid))
        {
            guidContract = modifierType.Assembly.GetType("IHasGuid", throwOnError: false);
            getGuid = guidContract?.GetMethod("GetGuid", Instance, null, Type.EmptyTypes, null);
            if (getGuid is null || getGuid.ReturnType != typeof(Guid)) return null;
        }

        var boxed = Expression.Parameter(typeof(object), "source");
        var empty = Expression.Constant(Guid.Empty);
        Expression body;
        if (guidContract is null)
        {
            body = Expression.Condition(
                Expression.Equal(boxed, Expression.Constant(null)),
                empty,
                Expression.Call(Expression.Convert(boxed, sourceType), getGuid));
        }
        else
        {
            body = Expression.Condition(
                Expression.TypeIs(boxed, guidContract),
                Expression.Call(Expression.Convert(boxed, guidContract), getGuid),
                empty);
        }

        try
        {
            return Expression.Lambda<Func<object, Guid>>(body, boxed).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Delegate? Compile(
        Type sourceType,
        Type resultType,
        Expression body,
        ParameterExpression source)
    {
        try
        {
            return Expression.Lambda(
                typeof(Func<,>).MakeGenericType(sourceType, resultType),
                body,
                source).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Func<object, WorldResearchRequirementAdjustment[], int, int>? Copier(
        Type modifierType,
        Func<object, object?> readDictionary,
        Delegate readType,
        Delegate readOrder,
        Delegate readAmount,
        Delegate readSource,
        Func<object, Guid> readSourceId,
        bool passive)
    {
        var factory = typeof(NativeModifierAdjustmentAccess).GetMethod(
            nameof(MakeCopier),
            BindingFlags.Static | BindingFlags.NonPublic)?.MakeGenericMethod(modifierType);
        if (factory is null) return null;

        try
        {
            return factory.Invoke(
                    null,
                    new object[]
                    {
                        readDictionary,
                        readType,
                        readOrder,
                        readAmount,
                        readSource,
                        readSourceId,
                        passive,
                    })
                as Func<object, WorldResearchRequirementAdjustment[], int, int>;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Func<object, WorldResearchRequirementAdjustment[], int, int> MakeCopier<TModifier>(
        Func<object, object?> readDictionary,
        Func<TModifier, int> readType,
        Func<TModifier, int> readOrder,
        Func<TModifier, BigDouble> readAmount,
        Func<TModifier, object?> readSource,
        Func<object, Guid> readSourceId,
        bool passive)
        where TModifier : struct
    {
        return (record, destination, start) =>
        {
            if (readDictionary(record) is not Dictionary<Guid, TModifier> dictionary) return start;

            var index = start;
            foreach (var entry in dictionary)
            {
                if (index >= destination.Length) break;
                var source = readSource(entry.Value);
                destination[index++] = new WorldResearchRequirementAdjustment(
                    entry.Key,
                    source is null ? Guid.Empty : readSourceId(source),
                    source?.GetType().Name ?? string.Empty,
                    readType(entry.Value),
                    readAmount(entry.Value),
                    readOrder(entry.Value),
                    passive);
            }
            return index;
        };
    }
}
