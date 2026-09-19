using System;
using System.Reflection;
using OrbModding.Common.Runtime.GameMath;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// The frame-wide terms the ported math needs that belong to no single entity.
/// </summary>
/// <remarks>
/// Read once per collection on the Unity thread and carried to the worker, for the same reason every
/// other reading is: the math runs off-thread, and <c>Time.fixedDeltaTime</c> and the player globals
/// may only be touched on the main thread.
/// </remarks>
internal readonly struct WorldFrameGlobals
{
    internal WorldFrameGlobals(
        BigDouble resourceOverflowPercent,
        BigDouble resourceOverflowLossPercent,
        BigDouble resetTimePassed,
        BigDouble structureCostPercent,
        BigDouble attributeQualityBonus,
        double fixedDeltaTime)
    {
        ResourceOverflowPercent = resourceOverflowPercent;
        ResourceOverflowLossPercent = resourceOverflowLossPercent;
        ResetTimePassed = resetTimePassed;
        StructureCostPercent = structureCostPercent;
        AttributeQualityBonus = attributeQualityBonus;
        FixedDeltaTime = fixedDeltaTime;
    }

    internal BigDouble ResourceOverflowPercent { get; }
    internal BigDouble ResourceOverflowLossPercent { get; }
    internal BigDouble ResetTimePassed { get; }

    /// <summary>
    /// The global multiplier on every structure's purchase cost, already through
    /// <see cref="OrbGameMath.AsPercent"/>.
    /// </summary>
    internal BigDouble StructureCostPercent { get; }

    /// <summary>
    /// The exponent the game raises a resource's quality to when discounting that resource's
    /// attribute cost: <c>attributeCostMod / Pow(quality.AsPercent(), this)</c>.
    /// </summary>
    /// <remarks>
    /// Raw, not through <see cref="OrbGameMath.AsPercent"/>. Every other global here is a percent
    /// because it multiplies something; this one is an exponent, and putting it on the percent scale
    /// would take a hundredth root of the quality instead of the power the game takes.
    /// </remarks>
    internal BigDouble AttributeQualityBonus { get; }

    internal double FixedDeltaTime { get; }
}

/// <summary>
/// Binds the static player accessors the ported math reads, and reads them per collection.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the category readers because these are statics on <c>Player</c> rather than members
/// of an enumerated entity, so none of the per-entity binding machinery applies. Binding failure
/// degrades rather than throwing, matching how a category that cannot bind degrades: a build that
/// renamed one accessor should still publish everything else.
/// </para>
/// <para>
/// Each accessor returns a <c>DoubleVariable</c> whose <c>value</c> record holds the number, read
/// here through the same read-only port of <c>GetValue()</c> every other record goes through. It used
/// to take the record's cache raw, and that is the defect this file was at the centre of:
/// <c>Player</c>'s globals sit outside the per-tick refresh the economy gives <c>ResourceSO</c>, so on
/// the first cold collection after a load <c>GetStructureCost()</c>'s cache was still the zero it
/// deserialises to. Nothing guarded it, <c>AsPercent(0)</c> is zero, and a zero cost multiplier
/// priced all 180 structures at nothing. What that read was missing was the dirty flag beside it: a
/// record deserialised at zero and marked dirty is one the game will recompute, and the reading now
/// recomputes it too. A record the game will <em>not</em> recompute still reads as its memo, because
/// that is the number the game will charge.
/// </para>
/// <para>
/// Every accessor here has the same <c>DoubleVariable</c> shape, which is what makes adding one a
/// binding and a field rather than a mechanism. <c>GetAttributeQualityBonus()</c> is the fifth, and
/// it lands on the memo rule above for free: it goes through the same <see cref="Value"/> helper, so
/// a bonus the game has not recalculated reads as the memo the game will use rather than a fresh
/// number the game will not.
/// </para>
/// <para>
/// The degraded value is not uniformly zero; see <see cref="Read"/> for why it is chosen per term.
/// </para>
/// </remarks>
internal sealed class WorldFrameGlobalsReader
{
    private const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instances = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Func<object?>? _resourceOverflow;
    private readonly Func<object?>? _resourceOverflowLoss;
    private readonly Func<object?>? _resetTimePassed;
    private readonly Func<object?>? _structureCost;
    private readonly Func<object?>? _attributeQualityBonus;
    private readonly Func<object, object?>? _variableValue;
    private readonly NativeModifierRecordAccess? _record;

    internal WorldFrameGlobalsReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));

        var player = resolveType("Player");
        if (player is null) return;

        var resourceOverflow = StaticNoArg(player, "GetResourceOverflow");
        var variableValue = resourceOverflow?.ReturnType.GetField("value", Instances);
        _record = NativeModifierRecordAccess.For(variableValue?.FieldType);

        // Discovery stays where it was; only the reading changes. A member that resolves but will
        // not compile leaves its accessor null, which is the same unavailability a renamed accessor
        // produces and degrades through the same per-term neutral values.
        _resourceOverflow = NativeAccessorBinder.CallStaticValue(resourceOverflow);
        _resourceOverflowLoss = NativeAccessorBinder.CallStaticValue(
            StaticNoArg(player, "GetResourceOverflowLoss"));
        _resetTimePassed = NativeAccessorBinder.CallStaticValue(
            StaticNoArg(player, "GetResetTimePassed"));
        _structureCost = NativeAccessorBinder.CallStaticValue(
            StaticNoArg(player, "GetStructureCost"));
        _attributeQualityBonus = NativeAccessorBinder.CallStaticValue(
            StaticNoArg(player, "GetAttributeQualityBonus"));
        _variableValue = NativeAccessorBinder.ReadValue(variableValue);
    }

    internal bool IsAvailable =>
        _resourceOverflow is not null &&
        _resourceOverflowLoss is not null &&
        _resetTimePassed is not null &&
        _structureCost is not null &&
        _attributeQualityBonus is not null &&
        _variableValue is not null &&
        _record is not null;

    /// <summary>
    /// Reads the globals. An unavailable reader yields the neutral value for each rather than
    /// refusing: the terms they feed drop out to their unmodified values, which is a worse answer
    /// than the game's but a defined one, and the alternative is failing the whole collection over
    /// five scalars.
    /// </summary>
    /// <remarks>
    /// Neutral is per-term, not uniform. Zero is right for the additive rate terms and wrong for
    /// <c>structureCost</c>, which multiplies — a zeroed one prices every structure at nothing — so
    /// that degrades to its identity of one. <c>attributeQualityBonus</c> is an exponent, and the
    /// exponent that leaves its base alone is zero, not one: <c>Pow(quality, 0)</c> is a divisor of
    /// one, whereas <c>Pow(quality, 1)</c> would divide the whole price by the quality.
    /// </remarks>
    internal WorldFrameGlobals Read(double fixedDeltaTime)
    {
        if (!IsAvailable)
            return new WorldFrameGlobals(default, default, default, BigDouble.One, default, fixedDeltaTime);

        return new WorldFrameGlobals(
            OrbGameMath.AsPercent(Value(_resourceOverflow!)),
            OrbGameMath.AsPercent(Value(_resourceOverflowLoss!)),
            Value(_resetTimePassed!),
            OrbGameMath.AsPercent(Value(_structureCost!)),
            Value(_attributeQualityBonus!),
            fixedDeltaTime);
    }

    private BigDouble Value(Func<object?> accessor)
    {
        var variable = accessor();
        if (variable is null) return default;

        return _record!.Fold(_variableValue!(variable));
    }

    /// <summary>
    /// Empty when the fold bound every input exactly, otherwise what it had to reconstruct. Carried
    /// up so a collection report says it before a number does.
    /// </summary>
    internal string Degradation => _record?.Degradation ?? string.Empty;

    private static MethodInfo? StaticNoArg(Type type, string name) =>
        type.GetMethod(name, Statics, null, Type.EmptyTypes, null);
}
