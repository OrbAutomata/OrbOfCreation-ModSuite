using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One harvest action as published: the three records that scale every run of that verb.
/// </summary>
/// <remarks>
/// <para>
/// These six are the base verbs the agromancy screens offer — Plant, Woodcutting, Mining, Create,
/// Transmorgify, Expand — and the class stores exactly three records:
/// <c>HarvestActionSO.GetScalingInfo()</c> loads <c>power</c>, <c>speed</c> and <c>costMod</c> into
/// the scaling map, with <c>costMod</c> serving as the drain modifier as well. Everything else the
/// class holds is audio and an authored scaling reference.
/// </para>
/// <para>
/// Which types the action wears is not on this row. It is published by
/// <see cref="WorldEntityKeywordReader"/> off the same <c>actionTypes</c> list
/// <c>HarvestActionSO.GetDisplayType()</c> joins into the word line the game prints, and reading it
/// here would file one native member under two owners.
/// </para>
/// </remarks>
internal readonly struct WorldHarvestAction : IWorldEntity
{
    internal WorldHarvestAction(
        Guid harvestActionId,
        BigDouble power,
        BigDouble speed,
        BigDouble costMod)
    {
        HarvestActionId = harvestActionId;
        Power = power;
        Speed = speed;
        CostMod = costMod;
    }

    internal Guid HarvestActionId { get; }

    public Guid EntityId => HarvestActionId;

    /// <summary>How hard one run of this verb hits, as the scaling map's Power.</summary>
    internal BigDouble Power { get; }

    /// <summary>How fast one run of this verb completes, as the scaling map's Speed.</summary>
    internal BigDouble Speed { get; }

    /// <summary>
    /// What one run costs, as the scaling map's CostMod. The same record is also its DrainCostMod,
    /// so this one number moves both the element cost and the resource drain.
    /// </summary>
    internal BigDouble CostMod { get; }
}

internal sealed class WorldHarvestActionBinder : WorldPlainBinder<WorldHarvestAction>
{
    private Func<object, Guid>? _id;
    private Func<object, BigDouble>? _power;
    private Func<object, BigDouble>? _speed;
    private Func<object, BigDouble>? _costMod;

    internal override string Category => "harvest actions";

    internal override string TypeName => "HarvestActionSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _power = bind.ModifierRecord("power");
        _speed = bind.ModifierRecord("speed");
        _costMod = bind.ModifierRecord("costMod");
        return bind.Failure;
    }

    internal override WorldHarvestAction Read(object entity) =>
        new(_id!(entity), _power!(entity), _speed!(entity), _costMod!(entity));
}
