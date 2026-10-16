using System;

namespace OrbModding.Common.Runtime.GameMath;

/// <summary>
/// Ported from <c>ValueModifier.ValueModifierType</c>. Order matters: it is the order in which
/// merged modifiers of one <see cref="GameValueModifier.Order"/> group are applied.
/// </summary>
internal enum GameValueModifierType
{
    Raw = 0,
    MultiDiminishing = 1,
    MultiStacking = 2,
    Reduction = 3,
    Exponent = 4,
}

/// <summary>
/// One modifier in the game's modifier stack, ported so the suite can evaluate the stack itself.
/// </summary>
/// <remarks>
/// Provenance: <c>ValueModifier</c> in <c>Assembly-CSharp.dll</c>, SHA-256 <c>5652EBE3…</c>
/// (audited baseline <c>steam-macos-2026-07-13</c>). See <see cref="OrbGameMath"/> for why the
/// suite owns this math.
/// </remarks>
internal readonly struct GameValueModifier
{
    internal GameValueModifier(GameValueModifierType type, BigDouble amount, int order = 0)
    {
        Type = type;
        Amount = amount;
        Order = order;
    }

    internal GameValueModifierType Type { get; }

    /// <summary>The original's <c>adjustReal</c>.</summary>
    internal BigDouble Amount { get; }

    /// <summary>
    /// Stack position. Modifiers sharing an order are merged with each other <em>before</em> any of
    /// them is applied; different orders are applied lowest-first as separate passes.
    /// </summary>
    internal int Order { get; }

    /// <summary>
    /// The neutral value for a type, ported from <c>ValueModifier(ValueModifierType)</c>: zero for
    /// the additive kinds, one for the multiplicative kinds.
    /// </summary>
    internal static BigDouble IdentityFor(GameValueModifierType type) =>
        type is GameValueModifierType.MultiStacking or GameValueModifierType.Exponent
            ? BigDouble.One
            : BigDouble.Zero;

    /// <summary>Ported from <c>ValueModifier.IsEmpty()</c> — the amount equals its type's identity.</summary>
    internal bool IsEmpty() => Amount == IdentityFor(Type);

    /// <summary>
    /// Ported from <c>ValueModifier.ToStringValue(bool)</c>: what the tooltip prints for this
    /// modifier — a signed addend, a signed percentage, <c>x1.02</c>, an inverted-sign percentage,
    /// or <c>^2</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The kind is not a word beside the number; it <em>is</em> how the number is written, which is
    /// why the original renders both together and why none of the five spellings needs a glossary.
    /// The two multiplicative kinds pass a threshold of one into
    /// <see cref="GameScientificNumber.Beautify"/>, so a multiplier a hair above one keeps the
    /// decimals that say so.
    /// </para>
    /// <para>
    /// The original's suffix flag is dropped: it reaches only the notation branch, and the notation
    /// the suite pins ignores it. Its default arm — the raw <c>BigDouble</c> for an ordinal outside
    /// the five — is dropped too: a kind the suite cannot spell is a broken contract, not a number
    /// to print anyway.
    /// </para>
    /// </remarks>
    internal string ToStringValue() => Type switch
    {
        GameValueModifierType.Raw =>
            Sign(Amount) + GameScientificNumber.Beautify(BigDouble.Abs(Amount), BigDouble.Zero),
        GameValueModifierType.MultiDiminishing =>
            Sign(Amount) +
            GameScientificNumber.Beautify(BigDouble.Abs(Amount * 100), BigDouble.Zero) + "%",
        GameValueModifierType.MultiStacking =>
            "x" + GameScientificNumber.Beautify(BigDouble.Abs(Amount), BigDouble.One),
        GameValueModifierType.Reduction =>
            (Amount >= BigDouble.Zero ? "-" : "+") +
            GameScientificNumber.Beautify(BigDouble.Abs(100 * Amount), BigDouble.Zero) + "%",
        GameValueModifierType.Exponent =>
            "^" + GameScientificNumber.Beautify(BigDouble.Abs(Amount), BigDouble.One),
        _ => throw new InvalidOperationException(
            "ValueModifier.ValueModifierType ordinal " + (int)Type + " has no spelling."),
    };

    private static string Sign(BigDouble amount) => amount >= BigDouble.Zero ? "+" : "-";

    /// <summary>
    /// Ported verbatim from <c>ValueModifier.Adjust(BigDouble)</c>.
    /// </summary>
    /// <remarks>
    /// The <c>Exponent</c> case inverts its exponent for values strictly between 0 and 1, so that a
    /// modifier intended to increase a magnitude does not shrink a fractional one. Reproduced as
    /// written.
    /// </remarks>
    internal BigDouble Adjust(BigDouble value) => Type switch
    {
        GameValueModifierType.Raw => value + Amount,
        GameValueModifierType.MultiDiminishing => value * (1 + Amount),
        GameValueModifierType.MultiStacking => value * Amount,
        GameValueModifierType.Reduction => value / (1 + Amount),
        GameValueModifierType.Exponent => BigDouble.Pow(
            value,
            value > 0 && value < 1 ? OrbGameMath.Invert(Amount) : Amount),
        _ => value,
    };

    /// <summary>
    /// Ported from <c>ValueModifier.MultiplyScalar(BigDouble)</c>: scale this modifier's strength,
    /// as when a per-level modifier is applied at some level count.
    /// </summary>
    /// <remarks>
    /// Additive kinds scale linearly while multiplicative kinds scale by exponentiation, which is
    /// the same distinction as <see cref="MergeWith"/>: applying a multiplier <c>n</c> times is
    /// <c>m^n</c>, not <c>m*n</c>.
    /// </remarks>
    internal GameValueModifier MultiplyScalar(BigDouble scalar) => Type switch
    {
        GameValueModifierType.Raw => new GameValueModifier(Type, Amount * scalar, Order),
        GameValueModifierType.MultiDiminishing => new GameValueModifier(Type, Amount * scalar, Order),
        GameValueModifierType.MultiStacking => new GameValueModifier(Type, BigDouble.Pow(Amount, scalar), Order),
        GameValueModifierType.Reduction => new GameValueModifier(Type, Amount * scalar, Order),
        GameValueModifierType.Exponent => new GameValueModifier(Type, BigDouble.Pow(Amount, scalar), Order),
        _ => this,
    };

    /// <summary>
    /// Ported from <c>ValueModifier.Adjust(ValueModifier)</c>: this modifier applied to another one,
    /// producing a modifier of the target's type and order with a strengthened amount.
    /// </summary>
    /// <remarks>
    /// The split is the original's. A multiplicative target is raised to the power of what this
    /// modifier does to one — so doubling an exponent squares it — while an additive target simply
    /// has its amount adjusted directly. This is what makes an exponent list act on a modifier list
    /// rather than on a value.
    /// </remarks>
    internal GameValueModifier Adjust(in GameValueModifier target) => target.Type switch
    {
        GameValueModifierType.MultiStacking or GameValueModifierType.Exponent =>
            target.WithAmount(BigDouble.Pow(target.Amount, Adjust(BigDouble.One))),
        GameValueModifierType.Raw or GameValueModifierType.MultiDiminishing or
            GameValueModifierType.Reduction => target.WithAmount(Adjust(target.Amount)),
        _ => target,
    };

    /// <summary>Same type and order, replaced amount — the original's <c>new ValueModifier(mod, x)</c>.</summary>
    internal GameValueModifier WithAmount(BigDouble amount) => new(Type, amount, Order);

    /// <summary>
    /// Ported from <c>ValueModifier.AddModifier(ValueModifier)</c>: how two modifiers of the same
    /// type and order combine. Additive kinds sum; multiplicative kinds multiply.
    /// </summary>
    internal GameValueModifier MergeWith(in GameValueModifier other) => Type switch
    {
        GameValueModifierType.Raw => new GameValueModifier(Type, Amount + other.Amount, Order),
        GameValueModifierType.MultiDiminishing => new GameValueModifier(Type, Amount + other.Amount, Order),
        GameValueModifierType.MultiStacking => new GameValueModifier(Type, Amount * other.Amount, Order),
        GameValueModifierType.Reduction => new GameValueModifier(Type, Amount + other.Amount, Order),
        GameValueModifierType.Exponent => new GameValueModifier(Type, Amount * other.Amount, Order),
        _ => this,
    };
}

/// <summary>
/// Evaluates a modifier stack against a base value, reproducing
/// <c>ValueModifier.AdjustWith(BigDouble, IEnumerable&lt;ValueModifier&gt;)</c> without allocating.
/// </summary>
/// <remarks>
/// <para>
/// The merge-before-apply rule is the whole reason this is not a simple fold, and getting it wrong
/// produces plausible numbers that are quietly incorrect. Two <c>MultiDiminishing</c> modifiers of
/// the same order sum into one — <c>v * (1 + a + b)</c> — whereas applying them in sequence would
/// give <c>v * (1 + a) * (1 + b)</c>. The original achieves this via
/// <c>CombineSameOrderLists</c> → <c>AdjustWithRaw</c>; this port achieves the same result by
/// accumulating one merged modifier per type per order group.
/// </para>
/// <para>
/// Within a group, merged modifiers are applied in declaration order of
/// <see cref="GameValueModifierType"/> (Raw, MultiDiminishing, MultiStacking, Reduction, Exponent),
/// matching the order in which the original appends them to its combined list. Empty modifiers are
/// skipped exactly as the original's <c>IsEmpty()</c> checks skip them — which matters, because
/// applying a neutral <c>Exponent</c> is not a no-op for values between 0 and 1.
/// </para>
/// <para>
/// The original sorts into lists; this walks the span once per distinct order instead. Modifier
/// counts per value are small, so the quadratic term is irrelevant next to avoiding an allocation
/// on a path that runs per candidate per collect.
/// </para>
/// </remarks>
internal static class GameModifierStack
{
    private static readonly GameValueModifierType[] ApplicationOrder =
    {
        GameValueModifierType.Raw,
        GameValueModifierType.MultiDiminishing,
        GameValueModifierType.MultiStacking,
        GameValueModifierType.Reduction,
        GameValueModifierType.Exponent,
    };

    /// <summary>
    /// Ported from <c>ValueModifier.AdjustWith(BigDouble, IEnumerable&lt;ValueModifier&gt;)</c>.
    /// An empty stack returns the base value untouched, as the original short-circuits.
    /// </summary>
    internal static BigDouble AdjustWith(BigDouble baseValue, ReadOnlySpan<GameValueModifier> modifiers)
    {
        if (modifiers.Length == 0) return baseValue;

        var value = baseValue;

        // Orders already applied. A long sentinel below int.MinValue lets the first pass select the
        // lowest order present without needing a separate "first iteration" flag.
        var appliedThrough = long.MinValue;

        while (true)
        {
            if (!TryFindNextOrder(modifiers, appliedThrough, out var order)) break;
            value = ApplyOrderGroup(value, modifiers, order);
            appliedThrough = order;
        }

        return value;
    }

    private static bool TryFindNextOrder(
        ReadOnlySpan<GameValueModifier> modifiers,
        long appliedThrough,
        out int order)
    {
        var found = false;
        order = 0;

        for (var index = 0; index < modifiers.Length; index++)
        {
            var candidate = modifiers[index].Order;
            if (candidate <= appliedThrough) continue;
            if (found && candidate >= order) continue;

            order = candidate;
            found = true;
        }

        return found;
    }

    /// <summary>
    /// Ported from <c>ValueModifier.AdjustWith(ValueModifier, IEnumerable&lt;ValueModifier&gt;)</c>:
    /// the same merge-then-apply rule, with a modifier rather than a value on the receiving end.
    /// </summary>
    internal static GameValueModifier AdjustWith(
        in GameValueModifier baseModifier,
        ReadOnlySpan<GameValueModifier> modifiers)
    {
        if (modifiers.Length == 0) return baseModifier;

        var adjusted = baseModifier;
        var appliedThrough = long.MinValue;
        while (TryFindNextOrder(modifiers, appliedThrough, out var order))
        {
            for (var slot = 0; slot < ApplicationOrder.Length; slot++)
            {
                if (TryMergeOrderGroup(modifiers, order, ApplicationOrder[slot], out var merged))
                    adjusted = merged.Adjust(in adjusted);
            }

            appliedThrough = order;
        }

        return adjusted;
    }

    /// <summary>
    /// Ported from
    /// <c>ValueModifier.AdjustWith(BigDouble, IEnumerable&lt;ValueModifier&gt;, IEnumerable&lt;ValueModifier&gt;)</c>
    /// — the exponent-aware form, where a second list strengthens the first before any of it is
    /// applied to the value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is load-bearing and not interchangeable with applying the two lists in sequence: the
    /// modifiers are merged into their order groups <em>first</em>, then every exponent group acts on
    /// those merged modifiers, and only then does the result touch the value. Exponentiating each
    /// modifier before merging would give a different answer whenever two modifiers share an order.
    /// </para>
    /// <para>
    /// <paramref name="scratch"/> is caller-owned and must be at least as long as
    /// <paramref name="modifiers"/>; combining can only shrink a list, never grow it, because every
    /// source modifier lands in exactly one (order, type) cell.
    /// </para>
    /// </remarks>
    internal static BigDouble AdjustWith(
        BigDouble baseValue,
        ReadOnlySpan<GameValueModifier> modifiers,
        ReadOnlySpan<GameValueModifier> exponents,
        Span<GameValueModifier> scratch)
    {
        if (modifiers.Length == 0) return baseValue;
        if (exponents.Length == 0) return AdjustWith(baseValue, modifiers);
        if (scratch.Length < modifiers.Length)
            throw new ArgumentException("Scratch must hold at least one entry per modifier.", nameof(scratch));

        var combined = Combine(modifiers, scratch);

        var appliedThrough = long.MinValue;
        while (TryFindNextOrder(exponents, appliedThrough, out var order))
        {
            for (var slot = 0; slot < ApplicationOrder.Length; slot++)
            {
                if (!TryMergeOrderGroup(exponents, order, ApplicationOrder[slot], out var exponent)) continue;
                for (var index = 0; index < combined; index++)
                    scratch[index] = exponent.Adjust(in scratch[index]);
            }

            appliedThrough = order;
        }

        var value = baseValue;
        for (var index = 0; index < combined; index++) value = scratch[index].Adjust(value);
        return value;
    }

    /// <summary>
    /// Ported from <c>ValueModifier.CombineSameOrderLists</c> flattened: one modifier per (order,
    /// type) that has any, ascending by order and then by application order, with groups that merged
    /// back to their identity dropped.
    /// </summary>
    /// <returns>How many entries of <paramref name="destination"/> were written.</returns>
    internal static int Combine(
        ReadOnlySpan<GameValueModifier> source,
        Span<GameValueModifier> destination)
    {
        var written = 0;
        var appliedThrough = long.MinValue;
        while (TryFindNextOrder(source, appliedThrough, out var order))
        {
            for (var slot = 0; slot < ApplicationOrder.Length; slot++)
            {
                if (TryMergeOrderGroup(source, order, ApplicationOrder[slot], out var merged))
                    destination[written++] = merged;
            }

            appliedThrough = order;
        }

        return written;
    }

    /// <summary>
    /// Merges every modifier of one type and order into one. False when the group is absent, or when
    /// it merged back to its own identity — the original only appends a combined modifier that is not
    /// empty, so such a group is never applied.
    /// </summary>
    private static bool TryMergeOrderGroup(
        ReadOnlySpan<GameValueModifier> modifiers,
        int order,
        GameValueModifierType type,
        out GameValueModifier merged)
    {
        merged = new GameValueModifier(type, GameValueModifier.IdentityFor(type), order);
        var present = false;

        for (var index = 0; index < modifiers.Length; index++)
        {
            var modifier = modifiers[index];
            if (modifier.Order != order || modifier.Type != type) continue;

            merged = merged.MergeWith(in modifier);
            present = true;
        }

        return present && !merged.IsEmpty();
    }

    private static BigDouble ApplyOrderGroup(
        BigDouble value,
        ReadOnlySpan<GameValueModifier> modifiers,
        int order)
    {
        for (var slot = 0; slot < ApplicationOrder.Length; slot++)
        {
            if (TryMergeOrderGroup(modifiers, order, ApplicationOrder[slot], out var merged))
                value = merged.Adjust(value);
        }

        return value;
    }
}
