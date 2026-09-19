using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One global number as published: the game's own cached value, plus whether it is meant to be read
/// as a percentage.
/// </summary>
/// <remarks>
/// <para>
/// These are the game's global variables — the registries behind <c>Player</c> and
/// <c>GlobalVariables</c>, which between them expose well over a hundred accessors. Every one of
/// those accessors is a lookup into one of these lists, so collecting the lists collects the lot,
/// and does it without naming a single accessor the suite would then have to keep declared.
/// </para>
/// <para>
/// <see cref="IsPercent"/> is not presentation. A percent variable holds 100 for parity and the
/// game divides it by a hundred at every use, so a consumer that treats one as a plain number is
/// out by two orders of magnitude — the same trap resource quality sets.
/// </para>
/// </remarks>
internal readonly struct WorldNumberVariable : IWorldEntity
{
    internal WorldNumberVariable(
        Guid variableId,
        BigDouble value,
        bool isPercent,
        bool isTime = false,
        bool isTimeAccurate = false)
    {
        VariableId = variableId;
        Value = value;
        IsPercent = isPercent;
        IsTime = isTime;
        IsTimeAccurate = isTimeAccurate;
    }

    internal Guid VariableId { get; }

    public Guid EntityId => VariableId;

    /// <summary>The record's cached value, read as a field so collecting never recalculates it.</summary>
    internal BigDouble Value { get; }

    /// <summary>Whether the value is in the game's percent representation, where 100 is parity.</summary>
    internal bool IsPercent { get; }

    /// <summary>
    /// Whether the value is a count of seconds the game draws as a duration, and whether it draws it
    /// to the second or to two decimals.
    /// </summary>
    /// <remarks>
    /// <c>NumberVariable.GetValueDisplay()</c> tests these two before <c>isPercentVariable</c>:
    /// <c>isTimeVariable</c> alone is <c>Utils.BeautifyTimeAccurate</c>, both together are
    /// <c>Utils.BeautifyTimeUltraPrecise</c>. Without them every time-shaped variable but the one
    /// the suite had hard-coded reached the wire as a bare magnitude, which is the number of seconds
    /// and not what any screen shows.
    /// </remarks>
    internal bool IsTime { get; }

    internal bool IsTimeAccurate { get; }
}

/// <summary>One global flag as published.</summary>
internal readonly struct WorldBoolVariable : IWorldEntity
{
    internal WorldBoolVariable(Guid variableId, bool value,
        bool initialValue,
        bool isSaved,
        int observerId)
    {
        VariableId = variableId;
        Value = value;
        InitialValue = initialValue;
        IsSaved = isSaved;
        ObserverId = observerId;
    }

    internal Guid VariableId { get; }

    public Guid EntityId => VariableId;

    internal bool Value { get; }

    /// <summary>The value the flag starts from, whether the game saves it, and its observable stamp.</summary>
    internal bool InitialValue { get; }

    internal bool IsSaved { get; }

    internal int ObserverId { get; }
}

/// <summary>
/// The shared shape of both number-variable registries. <c>DoubleVariable</c> and
/// <c>IntVariable</c> are separate registries of the same base type, and differ only in how the game
/// converts the value out — which is a caller's concern, not a collection one.
/// </summary>
internal abstract class WorldNumberVariableBinder : WorldPlainBinder<WorldNumberVariable>
{
    private Func<object, Guid>? _id;
    private Func<object, BigDouble>? _value;
    private Func<object, bool>? _isPercent;
    private Func<object, bool>? _isTime;
    private Func<object, bool>? _isTimeAccurate;

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _value = bind.ModifierRecord("value");
        _isPercent = bind.Field<bool>("isPercentVariable");
        _isTime = bind.Field<bool>("isTimeVariable");
        _isTimeAccurate = bind.Field<bool>("isTimeAccurateVariable");
        return bind.Failure;
    }

    internal override WorldNumberVariable Read(object entity) =>
        new(
            _id!(entity),
            _value!(entity),
            _isPercent!(entity),
            _isTime!(entity),
            _isTimeAccurate!(entity));
}

/// <summary>Global scalars: rates, multipliers, thresholds.</summary>
internal sealed class WorldDoubleVariableBinder : WorldNumberVariableBinder
{
    internal override string Category => "double variables";

    internal override string TypeName => "DoubleVariable";
}

/// <summary>Global counts: multi-buy, bulk development, slot limits.</summary>
internal sealed class WorldIntVariableBinder : WorldNumberVariableBinder
{
    internal override string Category => "int variables";

    internal override string TypeName => "IntVariable";
}

/// <summary>Global flags.</summary>
internal sealed class WorldBoolVariableBinder : WorldPlainBinder<WorldBoolVariable>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _value;
    private Func<object, bool>? _initialValue;
    private Func<object, bool>? _isSaved;
    private Func<object, int>? _observerId;

    internal override string Category => "bool variables";

    internal override string TypeName => "BoolVariable";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");

        // A plain field rather than a modifier record: BoolVariable does not derive from
        // NumberVariable and has nothing to calculate.
        _value = bind.Field<bool>("value");
        _initialValue = bind.Field<bool>("initialValue");
        _isSaved = bind.Field<bool>("isSaved");
        _observerId = bind.Field<int>("observerId");
        return bind.Failure;
    }

    internal override WorldBoolVariable Read(object entity) => new(_id!(entity), _value!(entity),
            _initialValue!(entity),
            _isSaved!(entity),
            _observerId!(entity));
}
