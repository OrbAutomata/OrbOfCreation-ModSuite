using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common.Runtime.GameMath;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// Reads one <c>ValueModifierRecord</c> the way the game reads it — its memo while that memo stands,
/// its recomputation from base value and modifier sets when the game would recompute — without
/// calling the accessor that would write either.
/// </summary>
/// <remarks>
/// <para>
/// This is a port of <c>GetValue()</c>, which is <c>calculationDirty ? Calculate() : calculatedValue</c>,
/// and getting it down to only half of that cost the suite two live failures in opposite directions.
/// Reading <c>calculatedValue</c> raw was the first: the field is <c>[NonSerialized]</c>, so on a cold
/// collection <c>Player.GetStructureCost()</c> read as zero, which priced all 180 structures at
/// nothing and had Auto Buy plan a purchase for every one of them. Folding unconditionally was the
/// second, and it is subtler: a record with <em>no modifiers</em> is a record nothing ever dirtied, so
/// the game never recomputes it and <c>GetValue()</c> returns its zero for the rest of the session —
/// while the fold returned the base value. <c>StructureSO.passiveCostMod</c> is such a record, 100
/// against 0, and the two land on opposite sides of the <c>Max</c> in <c>GetNextCostMod()</c>. Every
/// structure was published at the game's price times 1.25^levels-owned, and nothing was affordable.
/// </para>
/// <para>
/// So the rule is not "never read the game's memo" and never was. It is <b>read what the game reads.</b>
/// A number the game will not recompute is the number it will charge, however far a recomputation
/// would drift from it; a number it will recompute is the recomputation. Both readings are here, the
/// dirty flag chooses between them, and neither writes: <c>GetValue()</c> would have written
/// <c>calculatedValue</c>, cleared <c>calculationDirty</c> and bumped <c>observedId</c> on the suite's
/// schedule instead of the game's.
/// </para>
/// <para>
/// <c>Calculate()</c> is depth-0 and acyclic. It reads only its own record's base value and modifier
/// sets, never another record's, because a cross-record dependency materialises in this game as a
/// fresh immutable <c>ValueModifier</c> snapshot pushed into the target rather than as a live
/// pointer. There is therefore no ordering problem and no recursion to bound.
/// </para>
/// <para>
/// Two members are deliberately not reproduced, and they are the writes. <c>Calculate()</c> stores its
/// result and clears the flag; <c>Calculate()</c> also calls <c>UpdateObservable()</c> when the new
/// value moved by more than 0.1%. Omitting both is the whole point, and it means a recomputed value
/// can be right while the game's memo and observers have not caught up. That is expected — the game
/// will do its own writing when it next asks — not a discrepancy.
/// </para>
/// </remarks>
internal sealed class NativeModifierRecordAccess
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// One machine per record type, because the record type is the same one for every field in the
    /// game and building it costs five compiled lambdas and a closed generic method.
    /// </summary>
    private static readonly Dictionary<Type, NativeModifierRecordAccess?> Bound = new();

    private readonly Func<object, BigDouble> _calculatedValue;
    private readonly Func<object, bool> _calculationDirty;
    private readonly Func<object, double> _baseValue;
    private readonly Func<object, int> _passiveCount;
    private readonly Func<object, int> _activeCount;
    private readonly Func<object, GameValueModifier[], int, int> _copyPassive;
    private readonly Func<object, GameValueModifier[], int, int> _copyActive;

    /// <summary>
    /// Where the modifiers of one record are staged before they are folded. Owned here and reused,
    /// because a collection reads roughly five thousand records and a per-record array would be an
    /// allocation for each of the ones that are dirty. Safe for the same reason the category buffers
    /// are: capture is single-threaded on the Unity thread.
    /// </summary>
    private GameValueModifier[] _scratch = new GameValueModifier[8];

    private NativeModifierRecordAccess(
        Func<object, BigDouble> calculatedValue,
        Func<object, bool> calculationDirty,
        Func<object, double> baseValue,
        Func<object, int> passiveCount,
        Func<object, int> activeCount,
        Func<object, GameValueModifier[], int, int> copyPassive,
        Func<object, GameValueModifier[], int, int> copyActive,
        string degradation)
    {
        _calculatedValue = calculatedValue;
        _calculationDirty = calculationDirty;
        _baseValue = baseValue;
        _passiveCount = passiveCount;
        _activeCount = activeCount;
        _copyPassive = copyPassive;
        _copyActive = copyActive;
        Degradation = degradation;
    }

    /// <summary>
    /// Empty when every input bound exactly. Otherwise it names what had to be reconstructed and
    /// what that costs, so an operator reading a collection report learns it before a number does.
    /// </summary>
    internal string Degradation { get; }

    /// <summary>
    /// The machine for a record type, or <see langword="null"/> when this build does not expose the
    /// inputs the fold needs. Null is a binding failure and callers already have a path for it: a
    /// value that cannot be read is not evidence, so the category degrades and says so.
    /// </summary>
    internal static NativeModifierRecordAccess? For(Type? recordType)
    {
        if (recordType is null) return null;
        if (Bound.TryGetValue(recordType, out var existing)) return existing;

        var built = Build(recordType);
        Bound[recordType] = built;
        return built;
    }

    /// <summary>
    /// Ported from <c>ValueModifierRecord.GetValue()</c>: the memo while the record is clean, and
    /// <c>Calculate()</c>'s own <c>Adjust(baseValue)</c> over both modifier sets when it is dirty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dirty flag is read first and decides everything, which is both the correctness of this and
    /// most of its speed. A clean record's number is its memo whatever the modifiers would produce,
    /// because the game will not recompute it either — so the modifier copy and the arithmetic are
    /// skipped outright, and in a live save the great majority of records are clean.
    /// </para>
    /// <para>
    /// The empty short-circuit inside the dirty branch is the original's, in
    /// <c>ModifierRecord.Adjust</c>: a record with no modifiers at all is its base value verbatim.
    /// </para>
    /// </remarks>
    internal BigDouble Fold(object? record)
    {
        if (record is null) return default;

        if (!_calculationDirty(record)) return _calculatedValue(record);

        BigDouble baseValue = _baseValue(record);

        var total = _passiveCount(record) + _activeCount(record);
        if (total <= 0) return baseValue;

        if (_scratch.Length < total) _scratch = new GameValueModifier[total];

        var written = _copyPassive(record, _scratch, 0);
        written = _copyActive(record, _scratch, written);

        return GameModifierStack.AdjustWith(baseValue, new ReadOnlySpan<GameValueModifier>(_scratch, 0, written));
    }

    private static NativeModifierRecordAccess? Build(Type recordType)
    {
        // The two halves of GetValue(). Neither is optional: a build that stopped exposing the flag
        // would leave this unable to tell which reading the game takes, and guessing one of them is
        // how a snapshot ends up confidently carrying a number the game never uses.
        var calculatedValue = NativeAccessorBinder.Field<BigDouble>(recordType, "calculatedValue");
        var calculationDirty = NativeAccessorBinder.Field<bool>(recordType, "calculationDirty");
        var baseValue = NativeAccessorBinder.Field<double>(recordType, "baseValue");
        var passiveCount = NativeAccessorBinder.CollectionCount(recordType, "passiveModifiers");
        var activeCount = NativeAccessorBinder.CollectionCount(recordType, "activeModifiers");
        var passive = NativeAccessorBinder.Reference(recordType, "passiveModifiers");
        var active = NativeAccessorBinder.Reference(recordType, "activeModifiers");
        if (calculatedValue is null || calculationDirty is null || baseValue is null ||
            passiveCount is null || activeCount is null || passive is null || active is null)
        {
            return null;
        }

        var modifierType = ModifierType(recordType, "passiveModifiers");
        if (modifierType is null) return null;

        var readType = MemberReader(modifierType, "type", typeof(int), asEnum: true);
        var readOrder = MemberReader(modifierType, "order", typeof(int), asEnum: false);
        if (readType is null || readOrder is null) return null;

        // adjustReal is the authoritative in-memory amount. The public `adjust` beside it is written
        // as adjustReal.ToDouble(), so it saturates above ~1e308 — and this game's modifiers live
        // well past that. Reconstructing from it is a last resort, and it says so out loud.
        var degradation = string.Empty;
        var reconstruct = false;
        var readAmount = MemberReader(modifierType, "adjustReal", typeof(BigDouble), asEnum: false);
        if (readAmount is null)
        {
            var lossy = MemberReader(modifierType, "adjust", typeof(double), asEnum: false);
            if (lossy is null) return null;
            readAmount = Widen(modifierType, lossy);

            reconstruct = true;
            degradation =
                "ValueModifier.adjustReal did not bind, so every modifier amount is reconstructed " +
                "from the public 'adjust' double. That field is written as adjustReal.ToDouble() and " +
                "saturates above ~1e308, so folded values at this save's magnitudes may be wrong.";
        }

        var copyPassive = Copier(modifierType, passive, readType, readOrder, readAmount, reconstruct);
        var copyActive = Copier(modifierType, active, readType, readOrder, readAmount, reconstruct);

        return new NativeModifierRecordAccess(
            calculatedValue, calculationDirty, baseValue, passiveCount, activeCount,
            copyPassive, copyActive, degradation);
    }

    /// <summary>The element type of the modifier dictionary — the game's <c>ValueModifier</c>.</summary>
    private static Type? ModifierType(Type recordType, string fieldName)
    {
        var field = recordType.GetField(fieldName, Instance)?.FieldType;
        if (field is not { IsGenericType: true }) return null;

        var arguments = field.GetGenericArguments();
        if (arguments.Length != 2 || arguments[0] != typeof(Guid)) return null;
        return arguments[1].IsValueType ? arguments[1] : null;
    }

    private static Delegate? MemberReader(Type modifierType, string name, Type result, bool asEnum)
    {
        var field = modifierType.GetField(name, Instance);
        if (field is null) return null;

        if (asEnum)
        {
            if (!field.FieldType.IsEnum) return null;
            if (Enum.GetUnderlyingType(field.FieldType) != typeof(int)) return null;
        }
        else if (field.FieldType != result)
        {
            return null;
        }

        var source = Expression.Parameter(modifierType, "modifier");
        Expression body = Expression.Field(source, field);
        if (asEnum) body = Expression.Convert(body, typeof(int));

        return Expression.Lambda(
            typeof(Func<,>).MakeGenericType(modifierType, result), body, source).Compile();
    }

    /// <summary>Wraps a <c>Func&lt;TModifier, double&gt;</c> as a <c>Func&lt;TModifier, BigDouble&gt;</c>.</summary>
    private static Delegate Widen(Type modifierType, Delegate readDouble)
    {
        var source = Expression.Parameter(modifierType, "modifier");
        var call = Expression.Invoke(Expression.Constant(readDouble), source);
        var body = Expression.Convert(call, typeof(BigDouble));

        return Expression.Lambda(
            typeof(Func<,>).MakeGenericType(modifierType, typeof(BigDouble)), body, source).Compile();
    }

    private static Func<object, GameValueModifier[], int, int> Copier(
        Type modifierType,
        Func<object, object?> readDictionary,
        Delegate readType,
        Delegate readOrder,
        Delegate readAmount,
        bool reconstruct)
    {
        var factory = typeof(NativeModifierRecordAccess)
            .GetMethod(nameof(MakeCopier), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(modifierType);

        return (Func<object, GameValueModifier[], int, int>)factory.Invoke(
            null,
            new object[] { readDictionary, readType, readOrder, readAmount, reconstruct })!;
    }

    /// <summary>
    /// Copies one modifier dictionary into the staging buffer without boxing.
    /// </summary>
    /// <remarks>
    /// Closed over the game's modifier struct at bind time, so the <c>foreach</c> uses
    /// <c>Dictionary&lt;,&gt;</c>'s struct enumerator and each member read is a compiled field load
    /// on the unboxed value. Walking the dictionary through the non-generic <see cref="System.Collections.IDictionary"/>
    /// instead would box every modifier in the save on every collection.
    /// </remarks>
    private static Func<object, GameValueModifier[], int, int> MakeCopier<TModifier>(
        Func<object, object?> readDictionary,
        Func<TModifier, int> readType,
        Func<TModifier, int> readOrder,
        Func<TModifier, BigDouble> readAmount,
        bool reconstruct)
    {
        return (source, buffer, start) =>
        {
            if (readDictionary(source) is not Dictionary<Guid, TModifier> dictionary) return start;

            var index = start;
            foreach (var entry in dictionary)
            {
                if (index >= buffer.Length) break;

                var type = (GameValueModifierType)readType(entry.Value);
                var amount = readAmount(entry.Value);

                // ValueModifier.ConvertToReal: the serialized double drops the implicit one that
                // the multiplicative kinds carry.
                if (reconstruct &&
                    type is GameValueModifierType.MultiStacking or GameValueModifierType.Exponent)
                {
                    amount += BigDouble.One;
                }

                buffer[index++] = new GameValueModifier(type, amount, readOrder(entry.Value));
            }

            return index;
        };
    }
}
