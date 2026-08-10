using System;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// Compiles typed accessors for native members once, so reading a value on the warm path is a direct
/// call returning an unboxed result rather than a reflective one.
/// </summary>
/// <remarks>
/// <para>
/// This is the mechanism the Auto Buy reader already proved out: binding
/// <c>Expression.Lambda&lt;Func&lt;object, T&gt;&gt;</c> once and invoking it per entity removed
/// roughly 15 ms from a 23.7 ms collect, almost all of it <see cref="MethodInfo.Invoke"/> overhead and
/// the boxing it forces on every value-typed read. Nothing here is novel; it is that technique
/// factored out so world collection does not reimplement it a fifth time.
/// </para>
/// <para>
/// Every binder returns <see langword="null"/> rather than throwing when a member is absent or has an
/// unexpected type. A collector that cannot bind an accessor must degrade that category with evidence,
/// which it can only do if binding failure is a value it can inspect.
/// </para>
/// <para>
/// Return types are matched exactly rather than converted. An implicit widening would let a member
/// whose type changed between game versions keep binding and start returning subtly different numbers —
/// exactly the silent drift the assembly hash gate exists to prevent, reintroduced one member at a
/// time.
/// </para>
/// </remarks>
internal static class NativeAccessorBinder
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private const BindingFlags Static =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Binds a direct field read. The cheapest form: one cast, one load.</summary>
    internal static Func<object, TValue>? Field<TValue>(Type? owner, string name)
    {
        if (owner is null) return null;

        var field = owner.GetField(name, Instance);
        if (field is null || field.FieldType != typeof(TValue)) return null;

        var source = Expression.Parameter(typeof(object), "source");
        var read = Expression.Field(Expression.Convert(source, owner), field);
        return Compile<TValue>(read, source);
    }

    /// <summary>Binds one exact static field read for a frame-wide native fact.</summary>
    internal static Func<TValue>? StaticField<TValue>(Type? owner, string name)
    {
        if (owner is null) return null;

        var field = owner.GetField(name, Static);
        if (field is null || field.FieldType != typeof(TValue)) return null;

        try
        {
            return Expression.Lambda<Func<TValue>>(Expression.Field(null, field)).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Binds a no-argument instance method.</summary>
    internal static Func<object, TValue>? Call<TValue>(Type? owner, string name)
    {
        if (owner is null) return null;

        var method = owner.GetMethod(name, Instance, null, Type.EmptyTypes, null);
        if (method is null || method.ReturnType != typeof(TValue)) return null;

        var source = Expression.Parameter(typeof(object), "source");
        var call = Expression.Call(Expression.Convert(source, owner), method);
        return Compile<TValue>(call, source);
    }

    /// <summary>
    /// Binds a no-argument method whose exact native reference return type is known only at runtime.
    /// The returned object is consumed inside the collection pass and never enters a publication.
    /// </summary>
    internal static Func<object, object?>? CallObject(
        Type? owner,
        string name,
        Type? exactReturnType)
    {
        if (owner is null || exactReturnType is null || exactReturnType.IsValueType) return null;
        var method = owner.GetMethod(name, Instance, null, Type.EmptyTypes, null);
        if (method is null || method.ReturnType != exactReturnType) return null;

        var source = Expression.Parameter(typeof(object), "source");
        var call = Expression.Convert(
            Expression.Call(Expression.Convert(source, owner), method),
            typeof(object));
        try
        {
            return Expression.Lambda<Func<object, object?>>(call, source).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Binds an exact generic-list-returning method as an <see cref="IList"/>.</summary>
    internal static Func<object, IList?>? CallList(
        Type? owner,
        string name,
        Type? exactElementType)
    {
        if (owner is null || exactElementType is null) return null;
        var expected = typeof(System.Collections.Generic.List<>).MakeGenericType(exactElementType);
        var method = owner.GetMethod(name, Instance, null, Type.EmptyTypes, null);
        if (method is null || method.ReturnType != expected) return null;

        var source = Expression.Parameter(typeof(object), "source");
        var call = Expression.Convert(
            Expression.Call(Expression.Convert(source, owner), method),
            typeof(IList));
        try
        {
            return Expression.Lambda<Func<object, IList?>>(call, source).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Binds a one-argument instance method with exact argument and return types.</summary>
    internal static Func<object, TArgument, TValue>? Call<TArgument, TValue>(
        Type? owner,
        string name)
    {
        if (owner is null) return null;

        var method = owner.GetMethod(name, Instance, null, new[] { typeof(TArgument) }, null);
        if (method is null || method.ReturnType != typeof(TValue)) return null;

        var source = Expression.Parameter(typeof(object), "source");
        var argument = Expression.Parameter(typeof(TArgument), "argument");
        var call = Expression.Call(Expression.Convert(source, owner), method, argument);
        try
        {
            return Expression.Lambda<Func<object, TArgument, TValue>>(
                call,
                source,
                argument).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Binds a two-argument instance evaluator with exact managed argument types.</summary>
    internal static Func<object, TFirst, TSecond, TValue>? Call<TFirst, TSecond, TValue>(
        Type? owner,
        string name)
    {
        if (owner is null) return null;
        var method = owner.GetMethod(
            name,
            Instance,
            null,
            new[] { typeof(TFirst), typeof(TSecond) },
            null);
        if (method is null || method.ReturnType != typeof(TValue)) return null;
        var source = Expression.Parameter(typeof(object), "source");
        var first = Expression.Parameter(typeof(TFirst), "first");
        var second = Expression.Parameter(typeof(TSecond), "second");
        var call = Expression.Call(Expression.Convert(source, owner), method, first, second);
        try
        {
            return Expression.Lambda<Func<object, TFirst, TSecond, TValue>>(
                call, source, first, second).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Binds a one-argument evaluator returning an exact runtime reference type.</summary>
    internal static Func<object, TArgument, object?>? CallObject<TArgument>(
        Type? owner,
        string name,
        Type? exactReturnType)
    {
        if (owner is null || exactReturnType is null || exactReturnType.IsValueType) return null;
        var method = owner.GetMethod(
            name,
            Instance,
            null,
            new[] { typeof(TArgument) },
            null);
        if (method is null || method.ReturnType != exactReturnType) return null;
        var source = Expression.Parameter(typeof(object), "source");
        var argument = Expression.Parameter(typeof(TArgument), "argument");
        var call = Expression.Convert(
            Expression.Call(Expression.Convert(source, owner), method, argument),
            typeof(object));
        try
        {
            return Expression.Lambda<Func<object, TArgument, object?>>(
                call, source, argument).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Binds a two-argument evaluator returning an exact runtime reference type.</summary>
    internal static Func<object, TFirst, TSecond, object?>? CallObject<TFirst, TSecond>(
        Type? owner,
        string name,
        Type? exactReturnType)
    {
        if (owner is null || exactReturnType is null || exactReturnType.IsValueType) return null;
        var method = owner.GetMethod(
            name,
            Instance,
            null,
            new[] { typeof(TFirst), typeof(TSecond) },
            null);
        if (method is null || method.ReturnType != exactReturnType) return null;
        var source = Expression.Parameter(typeof(object), "source");
        var first = Expression.Parameter(typeof(TFirst), "first");
        var second = Expression.Parameter(typeof(TSecond), "second");
        var call = Expression.Convert(
            Expression.Call(Expression.Convert(source, owner), method, first, second),
            typeof(object));
        try
        {
            return Expression.Lambda<Func<object, TFirst, TSecond, object?>>(
                call, source, first, second).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Binds a method whose exact game-object argument type is known only at runtime.</summary>
    internal static Func<object, object, TValue>? CallWithObjectArgument<TValue>(
        Type? owner,
        string name,
        Type? argumentType)
    {
        if (owner is null || argumentType is null) return null;
        var method = owner.GetMethod(name, Instance, null, new[] { argumentType }, null);
        if (method is null || method.ReturnType != typeof(TValue)) return null;
        var source = Expression.Parameter(typeof(object), "source");
        var argument = Expression.Parameter(typeof(object), "argument");
        var call = Expression.Call(
            Expression.Convert(source, owner),
            method,
            Expression.Convert(argument, argumentType));
        try
        {
            return Expression.Lambda<Func<object, object, TValue>>(
                call, source, argument).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Binds a one-argument method whose argument is a native value type constructed from one
    /// <see cref="long"/>. The native type never crosses the binding boundary: callers supply the
    /// scalar and the compiled delegate constructs the exact argument inline.
    /// </summary>
    /// <remarks>
    /// This is deliberately narrower than a reflective invocation helper. It exists for
    /// <c>Prerequisites.Container.Check(Requirements.ConditionInfo)</c>, whose parameterized overload
    /// is read-only while the same-named parameterless overload latches availability. An ambiguous
    /// method, wrong return, wrong argument name, or missing exact constructor returns
    /// <see langword="null"/> before collection can begin.
    /// </remarks>
    internal static Func<object, long, TValue>? CallWithConstructedLongArgument<TValue>(
        Type? owner,
        string name,
        string argumentTypeName)
    {
        if (owner is null) return null;

        MethodInfo? method = null;
        foreach (var candidate in owner.GetMethods(Instance))
        {
            if (!string.Equals(candidate.Name, name, StringComparison.Ordinal) ||
                candidate.ReturnType != typeof(TValue))
            {
                continue;
            }
            var parameters = candidate.GetParameters();
            if (parameters.Length != 1 ||
                !string.Equals(
                    parameters[0].ParameterType.FullName,
                    argumentTypeName,
                    StringComparison.Ordinal))
            {
                continue;
            }
            if (method is not null) return null;
            method = candidate;
        }
        if (method is null) return null;

        var argumentType = method.GetParameters()[0].ParameterType;
        var constructor = argumentType.GetConstructor(new[] { typeof(long) });
        if (constructor is null) return null;

        var source = Expression.Parameter(typeof(object), "source");
        var level = Expression.Parameter(typeof(long), "level");
        var call = Expression.Call(
            Expression.Convert(source, owner),
            method,
            Expression.New(constructor, level));
        try
        {
            return Expression.Lambda<Func<object, long, TValue>>(
                call,
                source,
                level).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Binds "read a field, then read a field on it" as one accessor — the shape a stored struct's
    /// members take, such as a <c>ValueModifier</c>'s amount and order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to be how every <c>ValueModifierRecord</c> was read, reaching past the record's own
    /// accessor to its <c>calculatedValue</c> cache. Avoiding the accessor is still right —
    /// <c>GetValue()</c> calls <c>Calculate()</c> on a dirty record, which allocates a LINQ pass over
    /// both modifier dictionaries and writes four fields of game state — but the cache alone was the
    /// wrong substitute, because it is <c>[NonSerialized]</c> and holds zero until something calls the
    /// accessor. Records go through a read-only port of the accessor now; see
    /// <see cref="ModifierRecord"/>.
    /// </para>
    /// <para>
    /// What is left here is the honest case: a field inside a field that is plain stored data on both
    /// levels, where reading it asks the game nothing and can be neither stale nor unset.
    /// </para>
    /// </remarks>
    internal static Func<object, TValue>? NestedField<TValue>(Type? owner, string fieldName, string nestedName)
    {
        if (owner is null) return null;

        var outer = owner.GetField(fieldName, Instance);
        if (outer is null) return null;

        var inner = outer.FieldType.GetField(nestedName, Instance);
        if (inner is null || inner.FieldType != typeof(TValue)) return null;

        var source = Expression.Parameter(typeof(object), "source");
        var read = Expression.Field(Expression.Field(Expression.Convert(source, owner), outer), inner);
        return Compile<TValue>(read, source);
    }

    /// <summary>
    /// Binds a <c>ValueModifierRecord</c> field as the value the game itself reads out of it: its memo
    /// while that memo stands, and a recomputation from base value and modifier sets when the game
    /// would recompute.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaces <c>NestedField&lt;BigDouble&gt;(field, "calculatedValue")</c> everywhere. That
    /// read took half of <c>GetValue()</c> and called it the answer: for a record whose modifiers have
    /// churned, the memo is the previous answer and the game is about to replace it. A zero read of
    /// the global structure-cost multiplier priced every structure at nothing. But the other half is
    /// no more the answer on its own — a record nothing ever dirties is never recomputed, so its memo
    /// is what the game charges from forever, and recomputing it instead over-priced every structure
    /// by 1.25 to the power of its owned levels.
    /// </para>
    /// <para>
    /// Either way it is strictly read-only, which is the property the cached read was chosen for in
    /// the first place. <c>GetValue()</c> writes <c>calculatedValue</c>, clears
    /// <c>calculationDirty</c> and re-stamps the record's observable; this reads members and, when the
    /// record is dirty, does the arithmetic in this assembly. See
    /// <see cref="NativeModifierRecordAccess"/>.
    /// </para>
    /// </remarks>
    internal static Func<object, BigDouble>? ModifierRecord(Type? owner, string fieldName)
    {
        if (owner is null) return null;

        var field = owner.GetField(fieldName, Instance);
        if (field is null || field.FieldType.IsValueType) return null;

        var access = NativeModifierRecordAccess.For(field.FieldType);
        var read = Reference(owner, fieldName);
        if (access is null || read is null) return null;

        return source => access.Fold(read(source));
    }

    /// <summary>
    /// Binds an enum field inside a field, as its underlying integer — <see cref="NestedField{T}"/>
    /// and <see cref="EnumField"/> composed, with both of their type checks.
    /// </summary>
    internal static Func<object, int>? NestedEnumField(Type? owner, string fieldName, string nestedName)
    {
        if (owner is null) return null;

        var outer = owner.GetField(fieldName, Instance);
        if (outer is null) return null;

        var inner = outer.FieldType.GetField(nestedName, Instance);
        if (inner is null || !inner.FieldType.IsEnum) return null;
        if (Enum.GetUnderlyingType(inner.FieldType) != typeof(int)) return null;

        var source = Expression.Parameter(typeof(object), "source");
        var read = Expression.Convert(
            Expression.Field(Expression.Field(Expression.Convert(source, owner), outer), inner),
            typeof(int));
        return Compile<int>(read, source);
    }

    /// <summary>
    /// Binds an enum field as its underlying integer.
    /// </summary>
    /// <remarks>
    /// The suite deliberately does not mirror the game's enum types. A published row that named
    /// <c>ChallengeState.Completed</c> would have to redeclare every member, and a build that inserted
    /// one in the middle would silently renumber the copy while every comparison kept compiling. The
    /// integer is what the game persists and what a comparison actually rests on, so that is what
    /// travels; naming a state is the caller's problem, at the one place it matters.
    /// <para>
    /// The underlying type is checked exactly, for the same reason every other accessor checks its
    /// return type: a widened enum would keep binding and start meaning something else.
    /// </para>
    /// </remarks>
    internal static Func<object, int>? EnumField(Type? owner, string name)
    {
        if (owner is null) return null;

        var field = owner.GetField(name, Instance);
        if (field is null || !field.FieldType.IsEnum) return null;
        if (Enum.GetUnderlyingType(field.FieldType) != typeof(int)) return null;

        var source = Expression.Parameter(typeof(object), "source");
        var read = Expression.Convert(
            Expression.Field(Expression.Convert(source, owner), field), typeof(int));
        return Compile<int>(read, source);
    }

    /// <summary>
    /// Binds the element count of a collection-typed field.
    /// </summary>
    /// <remarks>
    /// The size of a variable-size thing is a fixed-size fact. An immutable publication cannot carry
    /// <c>List&lt;RitualEffectInstance&gt;</c>, and deferring the elements is usually right — but the
    /// game's own answer to "is this ritual running" is <c>ritualInstances.Count &gt; 0</c>, and that
    /// is an integer. Refusing the count along with the list would defer the fact as well as the
    /// storage, which was never the point. See D17.
    /// <para>
    /// A null collection reads as zero rather than throwing. The game leaves these fields null before
    /// first use, and "no instances" is the honest reading of that, not a failure.
    /// </para>
    /// </remarks>
    internal static Func<object, int>? CollectionCount(Type? owner, string name)
    {
        if (owner is null) return null;

        var field = owner.GetField(name, Instance);
        var count = field?.FieldType.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
        if (field is null || count is null || count.PropertyType != typeof(int)) return null;

        var source = Expression.Parameter(typeof(object), "source");
        var collection = Expression.Field(Expression.Convert(source, owner), field);
        var read = Expression.Condition(
            Expression.Equal(collection, Expression.Constant(null, field.FieldType)),
            Expression.Constant(0),
            Expression.Property(collection, count));
        return Compile<int>(read, source);
    }

    /// <summary>
    /// Binds a collection field as the list itself rather than its count.
    /// </summary>
    /// <remarks>
    /// The exception to <see cref="CollectionCount"/>'s rule, and it earns it: a purchase cost really
    /// is a variable-length list of resource amounts, and no scalar stands in for it. Everything the
    /// list yields is still copied out by value before it is published, so nothing escapes into a
    /// publication — the elements are read, converted, and dropped inside the collecting pass.
    /// <para>
    /// Typed as <see cref="IList"/> because the element type is a game type this assembly cannot
    /// name, and a null collection reads as null rather than throwing.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The element type of a generic collection field, or <see langword="null"/> when the field is
    /// absent or not generic. What <see cref="CollectionField"/> yields has to be read somehow, and
    /// the accessors that read it have to be bound against a type this assembly cannot name.
    /// </summary>
    internal static Type? CollectionElementType(Type? owner, string name)
    {
        var field = owner?.GetField(name, Instance)?.FieldType;
        return field is { IsGenericType: true } ? field.GetGenericArguments()[0] : null;
    }

    internal static Func<object, IList?>? CollectionField(Type? owner, string name)
    {
        if (owner is null) return null;

        var field = owner.GetField(name, Instance);
        if (field is null || !typeof(IList).IsAssignableFrom(field.FieldType)) return null;

        var source = Expression.Parameter(typeof(object), "source");
        var read = Expression.Convert(
            Expression.Field(Expression.Convert(source, owner), field), typeof(IList));

        try
        {
            return Expression.Lambda<Func<object, IList?>>(read, source).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Binds "read a field, then count the collection on it" — the shape
    /// <c>ModifierRecord.HasActiveElements()</c> takes.
    /// </summary>
    /// <remarks>
    /// <c>HasActiveElements()</c> is <c>activeModifiers.Count &gt; 0</c> over a public dictionary, so
    /// reading the count asks the same question without the call. It is also strictly more than the
    /// method returns: how many modifiers are stacked on a record is a fact the boolean throws away,
    /// and the collector is here to grab facts rather than answers.
    /// <para>
    /// Both levels read a null as zero, for the same reason <see cref="CollectionCount"/> does.
    /// </para>
    /// </remarks>
    internal static Func<object, int>? NestedCollectionCount(Type? owner, string fieldName, string nestedName)
    {
        if (owner is null) return null;

        var outer = owner.GetField(fieldName, Instance);
        var inner = outer?.FieldType.GetField(nestedName, Instance);
        var count = inner?.FieldType.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
        if (outer is null || inner is null || count is null || count.PropertyType != typeof(int))
        {
            return null;
        }

        var source = Expression.Parameter(typeof(object), "source");
        var record = Expression.Field(Expression.Convert(source, owner), outer);
        var collection = Expression.Field(record, inner);
        var zero = Expression.Constant(0);
        var read = Expression.Condition(
            Expression.Equal(record, Expression.Constant(null, outer.FieldType)),
            zero,
            Expression.Condition(
                Expression.Equal(collection, Expression.Constant(null, inner.FieldType)),
                zero,
                Expression.Property(collection, count)));
        return Compile<int>(read, source);
    }

    /// <summary>
    /// Binds a reference-typed field as the object it holds, so members can be bound against that
    /// object rather than against its owner.
    /// </summary>
    /// <remarks>
    /// This returns the object itself rather than a value read from it, which every other binder here
    /// deliberately avoids. It is the one case where that is right: the object is another entity, and
    /// what a caller wants is to bind <em>its</em> members. Nothing published ever holds the result —
    /// it is consumed inside <c>WorldMemberBinding.Through</c> and never reaches a row.
    /// </remarks>
    internal static Func<object, object?>? Reference(Type? owner, string name)
    {
        if (owner is null) return null;

        var field = owner.GetField(name, Instance);
        if (field is null || field.FieldType.IsValueType) return null;

        var source = Expression.Parameter(typeof(object), "source");
        var read = Expression.Convert(
            Expression.Field(Expression.Convert(source, owner), field), typeof(object));

        try
        {
            return Expression.Lambda<Func<object, object?>>(read, source).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Binds an exact reference-typed field as a collection-pass-only object.</summary>
    internal static Func<object, object?>? Reference(
        Type? owner,
        string name,
        Type? exactFieldType)
    {
        if (owner is null || exactFieldType is null || exactFieldType.IsValueType) return null;
        var field = owner.GetField(name, Instance);
        if (field is null || field.FieldType != exactFieldType) return null;

        var source = Expression.Parameter(typeof(object), "source");
        var read = Expression.Convert(
            Expression.Field(Expression.Convert(source, owner), field),
            typeof(object));

        try
        {
            return Expression.Lambda<Func<object, object?>>(read, source).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Binds a value-typed native field and boxes only at this infrequent structural seam.</summary>
    internal static Func<object, object?>? BoxedField(Type? owner, string name)
    {
        if (owner is null) return null;
        var field = owner.GetField(name, Instance);
        if (field is null || !field.FieldType.IsValueType) return null;
        var source = Expression.Parameter(typeof(object), "source");
        var read = Expression.Convert(
            Expression.Field(Expression.Convert(source, owner), field), typeof(object));
        try
        {
            return Expression.Lambda<Func<object, object?>>(read, source).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Binds a single-valued reference to another entity as that entity's identity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A published row may not hold a live game object, so an edge to another entity travels as the
    /// <see cref="Guid"/> a consumer can look up — which is D17's rule for references and the reason
    /// <c>AlchemyTypeSO.selectedLevel</c> was missing: the chosen level is an <c>IntVariable</c> that
    /// the global registry already collects, so only the edge to it was absent.
    /// </para>
    /// <para>
    /// The game spells a reference two ways. An <c>IdScriptableObject</c> answers <c>GetGuid()</c>; a
    /// <c>GuidContainer</c> wraps a private <c>_guid</c>. Both are accepted here because both mean
    /// "this points at that", and a caller should not have to know which one a field happens to use.
    /// </para>
    /// <para>
    /// An unset reference reads as <see cref="Guid.Empty"/> rather than throwing. Empty is already
    /// the suite's "no entity" — <c>WorldTable</c> refuses to admit a row carrying it — so a null
    /// reference arrives as a value that cannot be mistaken for a real edge.
    /// </para>
    /// </remarks>
    internal static Func<object, Guid>? ReferenceGuid(Type? owner, string name)
    {
        if (owner is null) return null;

        var field = owner.GetField(name, Instance);
        if (field is null || field.FieldType.IsValueType) return null;

        var getGuid = field.FieldType.GetMethod("GetGuid", Instance, null, Type.EmptyTypes, null);
        var guidField = field.FieldType.GetField("_guid", Instance);

        var source = Expression.Parameter(typeof(object), "source");
        var reference = Expression.Field(Expression.Convert(source, owner), field);

        Expression identity;
        if (getGuid is not null && getGuid.ReturnType == typeof(Guid))
        {
            identity = Expression.Call(reference, getGuid);
        }
        else if (guidField is not null && guidField.FieldType == typeof(Guid))
        {
            identity = Expression.Field(reference, guidField);
        }
        else
        {
            return null;
        }

        var read = Expression.Condition(
            Expression.Equal(reference, Expression.Constant(null, field.FieldType)),
            Expression.Constant(Guid.Empty),
            identity);
        return Compile<Guid>(read, source);
    }

    /// <summary>
    /// Binds a no-argument method that answers with another entity, as that entity's identity.
    /// </summary>
    /// <remarks>
    /// <see cref="ReferenceGuid"/>'s shape for the edges the game exposes as accessors rather than as
    /// fields. The method is invoked once and the result held, because calling it twice — to test for
    /// null and then to read the identity — would ask the game the same question twice per entity.
    /// </remarks>
    internal static Func<object, Guid>? CallReferenceGuid(Type? owner, string name)
    {
        if (owner is null) return null;

        var method = owner.GetMethod(name, Instance, null, Type.EmptyTypes, null);
        if (method is null || method.ReturnType.IsValueType) return null;

        var getGuid = method.ReturnType.GetMethod("GetGuid", Instance, null, Type.EmptyTypes, null);
        if (getGuid is null || getGuid.ReturnType != typeof(Guid)) return null;

        var source = Expression.Parameter(typeof(object), "source");
        var referenced = Expression.Variable(method.ReturnType, "referenced");
        var read = Expression.Block(
            new[] { referenced },
            Expression.Assign(referenced, Expression.Call(Expression.Convert(source, owner), method)),
            Expression.Condition(
                Expression.Equal(referenced, Expression.Constant(null, method.ReturnType)),
                Expression.Constant(Guid.Empty),
                Expression.Call(referenced, getGuid)));
        return Compile<Guid>(read, source);
    }

    /// <summary>
    /// Binds an entity-returning method whose declared interface does not itself expose identity,
    /// while every supported concrete result derives from one audited identity-bearing base type.
    /// </summary>
    /// <remarks>
    /// Brewing-station selector entries are the motivating native shape:
    /// <c>TypeElement.GetTooltipable()</c> declares <c>ITooltipable</c>, while its resource, glyph,
    /// and consumable results are <c>TooltipableObject</c> instances. The type test keeps a future
    /// non-entity tooltip result fail-closed as an empty identity and performs no reflection on the
    /// collection path.
    /// </remarks>
    internal static Func<object, Guid>? CallReferenceGuid(
        Type? owner,
        string name,
        Type? exactReturnType,
        Type? identityType)
    {
        if (owner is null || exactReturnType is null || identityType is null ||
            exactReturnType.IsValueType || identityType.IsValueType) return null;

        var method = owner.GetMethod(name, Instance, null, Type.EmptyTypes, null);
        var getGuid = identityType.GetMethod("GetGuid", Instance, null, Type.EmptyTypes, null);
        if (method is null || method.ReturnType != exactReturnType ||
            getGuid is null || getGuid.ReturnType != typeof(Guid)) return null;

        var source = Expression.Parameter(typeof(object), "source");
        var referenced = Expression.Variable(exactReturnType, "referenced");
        var read = Expression.Block(
            new[] { referenced },
            Expression.Assign(referenced, Expression.Call(Expression.Convert(source, owner), method)),
            Expression.Condition(
                Expression.AndAlso(
                    Expression.NotEqual(referenced, Expression.Constant(null, exactReturnType)),
                    Expression.TypeIs(referenced, identityType)),
                Expression.Call(Expression.Convert(referenced, identityType), getGuid),
                Expression.Constant(Guid.Empty)));
        return Compile<Guid>(read, source);
    }

    /// <summary>
    /// Reads a public static list member — the per-type <c>All</c> registry every category exposes,
    /// and the same discovery mechanism Auto Buy already uses for candidate enumeration.
    /// </summary>
    internal static IList? StaticList(Type? owner, string name)
    {
        if (owner is null) return null;

        var value = owner.GetField(name, Static)?.GetValue(null) ??
            owner.GetProperty(name, Static)?.GetValue(null, null);
        return value as IList;
    }

    /// <summary>
    /// Resolves a static list once and returns only the warm-path value read. This is the lifecycle-
    /// bound form for readers that must not rediscover the member on every collection.
    /// </summary>
    internal static Func<IList?>? StaticListAccessor(Type? owner, string name)
    {
        if (owner is null) return null;
        var field = owner.GetField(name, Static);
        if (field is null || !typeof(IList).IsAssignableFrom(field.FieldType)) return null;
        var read = Expression.Convert(Expression.Field(null, field), typeof(IList));
        try
        {
            return Expression.Lambda<Func<IList?>>(read).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a static dictionary member — the game's identity registry, which is how an entity that
    /// belongs to no per-type <c>All</c> list is reached.
    /// </summary>
    /// <remarks>
    /// The list variables holding the action queues are such entities: their <c>All</c> is declared on
    /// the generic base rather than on the concrete type, so it is not a registry of queues at all.
    /// Looking one up by its stable uuid is the path the rest of the suite already takes to them.
    /// </remarks>
    internal static IDictionary? StaticDictionary(Type? owner, string name)
    {
        if (owner is null) return null;

        var value = owner.GetField(name, Static)?.GetValue(null) ??
            owner.GetProperty(name, Static)?.GetValue(null, null);
        return value as IDictionary;
    }

    /// <summary>Compiles an already-audited instance field as an unboxed typed read.</summary>
    /// <remarks>
    /// <para>
    /// The binders above discover a member and compile it in one step, which is right when the
    /// name and the expected type are the whole contract. Some readers audit more than that before
    /// they will accept a member — a list whose element type has to match, a field declared private
    /// on a generic base, a method resolved up the hierarchy — and re-spelling those audits as a
    /// name plus a type would quietly relax them.
    /// </para>
    /// <para>
    /// This family takes the <see cref="MemberInfo"/> such a reader already resolved and compiles
    /// that, so the discovery stays exactly as strict as it was and only the reading changes. The
    /// type checks are still made here rather than assumed: a caller that resolved a member for one
    /// purpose and compiles it for another gets <see langword="null"/> instead of a delegate that
    /// reinterprets it.
    /// </para>
    /// </remarks>
    internal static Func<object, TValue>? Read<TValue>(FieldInfo? field)
    {
        if (field is null || field.IsStatic || field.FieldType != typeof(TValue)) return null;
        var source = Expression.Parameter(typeof(object), "source");
        return Compile<TValue>(ReadField(field, source), source);
    }

    /// <summary>Compiles an already-audited instance field as the value it holds, boxed if need be.</summary>
    internal static Func<object, object?>? ReadValue(FieldInfo? field)
    {
        if (field is null || field.IsStatic) return null;
        var source = Expression.Parameter(typeof(object), "source");
        return Compile<object?>(
            Expression.Convert(ReadField(field, source), typeof(object)),
            source);
    }

    /// <summary>Compiles an already-audited instance collection field as the sequence it holds.</summary>
    internal static Func<object, IEnumerable?>? ReadSequence(FieldInfo? field)
    {
        if (field is null || field.IsStatic ||
            !typeof(IEnumerable).IsAssignableFrom(field.FieldType)) return null;
        var source = Expression.Parameter(typeof(object), "source");
        return Compile<IEnumerable?>(
            Expression.Convert(ReadField(field, source), typeof(IEnumerable)),
            source);
    }

    /// <summary>Compiles an already-audited instance collection field as the list it holds.</summary>
    internal static Func<object, IList?>? ReadList(FieldInfo? field)
    {
        if (field is null || field.IsStatic ||
            !typeof(IList).IsAssignableFrom(field.FieldType)) return null;
        var source = Expression.Parameter(typeof(object), "source");
        return Compile<IList?>(
            Expression.Convert(ReadField(field, source), typeof(IList)),
            source);
    }

    /// <summary>Compiles an already-audited static collection field as the sequence it holds.</summary>
    internal static Func<IEnumerable?>? ReadStaticSequence(FieldInfo? field)
    {
        if (field is null || !field.IsStatic ||
            !typeof(IEnumerable).IsAssignableFrom(field.FieldType)) return null;
        try
        {
            return Expression.Lambda<Func<IEnumerable?>>(
                Expression.Convert(Expression.Field(null, field), typeof(IEnumerable))).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Compiles an already-audited no-argument instance method as an unboxed typed call.</summary>
    internal static Func<object, TValue>? Call<TValue>(MethodInfo? method)
    {
        if (!IsInstanceCall(method, argumentCount: 0) ||
            method!.ReturnType != typeof(TValue)) return null;
        var source = Expression.Parameter(typeof(object), "source");
        return Compile<TValue>(CallOn(method, source), source);
    }

    /// <summary>Compiles an already-audited no-argument instance method as the value it answers.</summary>
    internal static Func<object, object?>? CallValue(MethodInfo? method)
    {
        if (!IsInstanceCall(method, argumentCount: 0)) return null;
        var source = Expression.Parameter(typeof(object), "source");
        return Compile<object?>(
            Expression.Convert(CallOn(method!, source), typeof(object)),
            source);
    }

    /// <summary>
    /// Compiles an already-audited one-argument instance method as the sequence it answers. The
    /// argument crosses as <see cref="object"/> because its type is a game type this assembly
    /// cannot name, and is converted to the exact parameter type inside the compiled body.
    /// </summary>
    internal static Func<object, object, IEnumerable?>? CallSequence(MethodInfo? method)
    {
        if (!IsInstanceCall(method, argumentCount: 1) ||
            !typeof(IEnumerable).IsAssignableFrom(method!.ReturnType)) return null;
        var source = Expression.Parameter(typeof(object), "source");
        var argument = Expression.Parameter(typeof(object), "argument");
        var call = Expression.Call(
            Expression.Convert(source, method.DeclaringType!),
            method,
            Expression.Convert(argument, method.GetParameters()[0].ParameterType));
        try
        {
            return Expression.Lambda<Func<object, object, IEnumerable?>>(
                Expression.Convert(call, typeof(IEnumerable)), source, argument).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Compiles an already-audited one-argument instance method as an unboxed typed answer. The
    /// argument crosses as <see cref="object"/> because its type is a game type this assembly cannot
    /// name, and is converted to the exact parameter type inside the compiled body — so neither the
    /// argument array nor the boxed return that <see cref="MethodInfo.Invoke"/> forces is allocated.
    /// </summary>
    internal static Func<object, object, TValue>? CallWithObjectArgument<TValue>(MethodInfo? method)
    {
        if (!IsInstanceCall(method, argumentCount: 1) ||
            method!.ReturnType != typeof(TValue)) return null;
        var source = Expression.Parameter(typeof(object), "source");
        var argument = Expression.Parameter(typeof(object), "argument");
        var call = Expression.Call(
            Expression.Convert(source, method.DeclaringType!),
            method,
            Expression.Convert(argument, method.GetParameters()[0].ParameterType));
        try
        {
            return Expression.Lambda<Func<object, object, TValue>>(
                call, source, argument).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Binds a static reference-typed field as the object it holds, for the manager singletons a
    /// reader must reach once per pass without rediscovering the field.
    /// </summary>
    /// <remarks>
    /// <see cref="Reference(Type?, string)"/>'s shape for statics, and for the same reason it returns
    /// the object rather than a value read from it: what a caller wants is the entity, so it can
    /// check the runtime type it got and bind members against it.
    /// </remarks>
    internal static Func<object?>? StaticReference(Type? owner, string name)
    {
        if (owner is null) return null;
        var field = owner.GetField(name, Static);
        if (field is null || field.FieldType.IsValueType) return null;
        try
        {
            return Expression.Lambda<Func<object?>>(
                Expression.Convert(Expression.Field(null, field), typeof(object))).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Compiles an already-audited one-argument static method as the value it answers.</summary>
    internal static Func<TArgument, object?>? CallStatic<TArgument>(MethodInfo? method)
    {
        var parameters = method?.GetParameters();
        if (method is null || !method.IsStatic || parameters!.Length != 1 ||
            parameters[0].ParameterType != typeof(TArgument)) return null;
        var argument = Expression.Parameter(typeof(TArgument), "argument");
        try
        {
            return Expression.Lambda<Func<TArgument, object?>>(
                Expression.Convert(Expression.Call(method, argument), typeof(object)),
                argument).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsInstanceCall(MethodInfo? method, int argumentCount) =>
        method is { IsStatic: false, DeclaringType: not null } &&
        method.GetParameters().Length == argumentCount;

    private static Expression ReadField(FieldInfo field, ParameterExpression source) =>
        Expression.Field(Expression.Convert(source, field.DeclaringType!), field);

    private static Expression CallOn(MethodInfo method, ParameterExpression source) =>
        Expression.Call(Expression.Convert(source, method.DeclaringType!), method);

    private static Func<object, TValue>? Compile<TValue>(Expression body, ParameterExpression source)
    {
        try
        {
            return Expression.Lambda<Func<object, TValue>>(body, source).Compile();
        }
        catch (Exception)
        {
            // Compilation can fail on a runtime without dynamic code generation. Treating that as an
            // unbound accessor keeps the failure on the one path callers already handle.
            return null;
        }
    }
}
