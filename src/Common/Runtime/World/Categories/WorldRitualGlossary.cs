using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One status a ritual can put on a combatant: the word the screen prints over a portrait, whether
/// it helps or hurts, how long it lasts and how a second application stacks with the first.
/// </summary>
/// <remarks>
/// <para>
/// The game's own word for the class is <c>Status Effect</c>, which is what its
/// <c>CombatStatus</c> localization entry says, so that is the category's name rather than the
/// <c>CombatStatusSO</c> the assembly spells it with.
/// </para>
/// <para>
/// <see cref="MaxDuration"/> is authored negative on the statuses whose stack count is their
/// duration — Barrier's own sentence says "Lasts stacks in seconds" — and the negative travels as
/// the game states it rather than as a word this suite invented for it. What the status does to a
/// combatant's numbers is its <c>statChanges</c> list, which is a relation and not a row, and it is
/// deliberately not here: the sentence the game authors for each of the eight already says it, and
/// a table of stat deltas with no live combat state to apply them to would be arithmetic about a
/// battle this suite cannot see.
/// </para>
/// </remarks>
internal readonly struct WorldStatusEffect : IWorldEntity
{
    internal WorldStatusEffect(
        Guid statusEffectId,
        bool isBuff,
        double maxDuration,
        bool stacksSeparately,
        bool resetDurationOnApplication,
        double effectTimer,
        string description)
    {
        StatusEffectId = statusEffectId;
        IsBuff = isBuff;
        MaxDuration = maxDuration;
        StacksSeparately = stacksSeparately;
        ResetDurationOnApplication = resetDurationOnApplication;
        EffectTimer = effectTimer;
        Description = description ?? string.Empty;
    }

    internal Guid StatusEffectId { get; }

    public Guid EntityId => StatusEffectId;

    /// <summary>Whether the game files this one as a buff rather than as a debuff.</summary>
    internal bool IsBuff { get; }

    /// <summary>Seconds it lasts, or negative where the stack count is the duration.</summary>
    internal double MaxDuration { get; }

    /// <summary>Whether a second application runs beside the first instead of merging into it.</summary>
    internal bool StacksSeparately { get; }

    /// <summary>Whether a fresh application puts the clock back to full.</summary>
    internal bool ResetDurationOnApplication { get; }

    /// <summary>Seconds between two ticks of the repeating half, where the status has one.</summary>
    internal double EffectTimer { get; }

    /// <summary>The sentence the game prints under the word.</summary>
    internal string Description { get; }
}

/// <summary>The ritual's status vocabulary: <c>CombatStatusSO.All</c>.</summary>
internal sealed class WorldStatusEffectBinder : WorldPlainBinder<WorldStatusEffect>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _isBuff;
    private Func<object, double>? _maxDuration;
    private Func<object, bool>? _stacksSeparately;
    private Func<object, bool>? _resetDurationOnApplication;
    private Func<object, double>? _effectTimer;
    private Func<object, string>? _description;

    internal override string Category => "status effects";

    internal override string TypeName => "CombatStatusSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _isBuff = bind.Field<bool>("isBuff");
        _maxDuration = bind.Field<double>("maxDuration");
        _stacksSeparately = bind.Field<bool>("stacksSeparately");
        _resetDurationOnApplication = bind.Field<bool>("resetDurationOnApplication");
        _effectTimer = bind.Field<double>("effectTimer");
        _description = bind.Field<string>("description");
        return bind.Failure;
    }

    internal override WorldStatusEffect Read(object entity) =>
        new(
            _id!(entity),
            _isBuff!(entity),
            _maxDuration!(entity),
            _stacksSeparately!(entity),
            _resetDurationOnApplication!(entity),
            _effectTimer!(entity),
            _description!(entity));
}

/// <summary>
/// One of the numbers a combatant is described by — Max Health, Power, Speed, Defense — under the
/// word the ritual screen prints for it.
/// </summary>
/// <remarks>
/// Named <c>character-attributes</c> after the game's own <c>CharacterAttribute</c> localization
/// entry, and deliberately not "combat stats": <c>statistics</c> is already the word this suite uses
/// for the game's <c>AttributeSO</c> glossary, and these eight are a different eight — a combatant's
/// stat line rather than a tooltip's section heading.
/// <para>
/// <see cref="DamageTypeId"/> is the game's own edge — <c>CharacterAttributeSO.damageType</c> — and
/// on the pinned build every one of the eight authors it null, so the column reads empty on all of
/// them. It is published anyway, and said rather than dropped: the field is the game's, what is
/// missing is an authored value rather than a read, and a suite that quietly omitted an edge
/// because this build leaves it blank would omit it silently on the build that fills it.
/// The authoring key the game's <c>StatBlock</c> looks these up by is <c>associatedStat</c>, which
/// nothing here selects and which the manifest therefore mirrors: it is an internal key the player
/// never reads.
/// </para>
/// </remarks>
internal readonly struct WorldCharacterAttribute : IWorldEntity
{
    internal WorldCharacterAttribute(
        Guid characterAttributeId,
        Guid damageTypeId,
        string description)
    {
        CharacterAttributeId = characterAttributeId;
        DamageTypeId = damageTypeId;
        Description = description ?? string.Empty;
    }

    internal Guid CharacterAttributeId { get; }

    public Guid EntityId => CharacterAttributeId;

    /// <summary>The damage type this stat is about, or empty where the game names none.</summary>
    internal Guid DamageTypeId { get; }

    internal string Description { get; }
}

/// <summary>A combatant's stat line: <c>CharacterAttributeSO.All</c>.</summary>
internal sealed class WorldCharacterAttributeBinder : WorldPlainBinder<WorldCharacterAttribute>
{
    private Func<object, Guid>? _id;
    private Func<object, Guid>? _damageTypeId;
    private Func<object, string>? _description;

    internal override string Category => "character attributes";

    internal override string TypeName => "CharacterAttributeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _damageTypeId = bind.ReferenceGuid("damageType");
        _description = bind.Field<string>("description");
        return bind.Failure;
    }

    internal override WorldCharacterAttribute Read(object entity) =>
        new(_id!(entity), _damageTypeId!(entity), _description!(entity));
}

/// <summary>
/// One kind of damage a ritual deals, with the two rules that decide what a hit of it is worth.
/// </summary>
/// <remarks>
/// <see cref="DamageReductionRate"/> scales how effective a target's Defense is against this kind —
/// Area authors 1.2, so resistance counts for more against it — and
/// <see cref="IgnoreEntrenched"/> is the same asset's other half: it ignores the penalty for
/// reaching the back row. Both are printed in the type's own sentence, and both are published
/// because the sentence is prose and these are the numbers behind it.
/// </remarks>
internal readonly struct WorldDamageType : IWorldEntity
{
    internal WorldDamageType(
        Guid damageTypeId,
        double damageReductionRate,
        bool ignoreEntrenched,
        string description)
    {
        DamageTypeId = damageTypeId;
        DamageReductionRate = damageReductionRate;
        IgnoreEntrenched = ignoreEntrenched;
        Description = description ?? string.Empty;
    }

    internal Guid DamageTypeId { get; }

    public Guid EntityId => DamageTypeId;

    /// <summary>How much of a target's reduction applies to a hit of this kind.</summary>
    internal double DamageReductionRate { get; }

    /// <summary>Whether a hit of this kind ignores the Entrenched back-row penalty.</summary>
    internal bool IgnoreEntrenched { get; }

    internal string Description { get; }
}

/// <summary>The ritual's damage vocabulary: <c>DamageTypeSO.All</c>.</summary>
internal sealed class WorldDamageTypeBinder : WorldPlainBinder<WorldDamageType>
{
    private Func<object, Guid>? _id;
    private Func<object, double>? _damageReductionRate;
    private Func<object, bool>? _ignoreEntrenched;
    private Func<object, string>? _description;

    internal override string Category => "damage types";

    internal override string TypeName => "DamageTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _damageReductionRate = bind.Field<double>("damageReductionRate");
        _ignoreEntrenched = bind.Field<bool>("ignoreEntrenched");
        _description = bind.Field<string>("description");
        return bind.Failure;
    }

    internal override WorldDamageType Read(object entity) =>
        new(
            _id!(entity),
            _damageReductionRate!(entity),
            _ignoreEntrenched!(entity),
            _description!(entity));
}

/// <summary>
/// One prefix a ritual can roll onto an enemy — Armored, Deadly, Healing, Quick — and how often it
/// rolls.
/// </summary>
/// <remarks>
/// The game's own word for the class is <c>Character Modifier</c>. What the prefix does to the
/// enemy is its <c>attributeModifiers</c> list, a relation this row does not carry for the same
/// reason a status's stat changes are not carried: the game authors a sentence for each of the four
/// and the ritual state those deltas would apply to is captured nowhere.
/// <see cref="WeightChance"/> is authored, published as authored, and is a weight rather than a
/// probability — the roll normalizes it against the other prefixes offered.
/// </remarks>
internal readonly struct WorldCharacterModifier : IWorldEntity
{
    internal WorldCharacterModifier(
        Guid characterModifierId,
        double weightChance,
        string description)
    {
        CharacterModifierId = characterModifierId;
        WeightChance = weightChance;
        Description = description ?? string.Empty;
    }

    internal Guid CharacterModifierId { get; }

    public Guid EntityId => CharacterModifierId;

    /// <summary>The prefix's authored roll weight, against the other prefixes on offer.</summary>
    internal double WeightChance { get; }

    internal string Description { get; }
}

/// <summary>The enemy prefixes: <c>CharacterModifierSO.All</c>.</summary>
internal sealed class WorldCharacterModifierBinder : WorldPlainBinder<WorldCharacterModifier>
{
    private Func<object, Guid>? _id;
    private Func<object, float>? _weightChance;
    private Func<object, string>? _description;

    internal override string Category => "character modifiers";

    internal override string TypeName => "CharacterModifierSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _weightChance = bind.Field<float>("weightChance");
        _description = bind.Field<string>("description");
        return bind.Failure;
    }

    internal override WorldCharacterModifier Read(object entity) =>
        new(_id!(entity), _weightChance!(entity), _description!(entity));
}

/// <summary>
/// One thing a combatant does in a ritual — the words on the battle log — with the three numbers
/// that decide when it lands.
/// </summary>
/// <remarks>
/// A turn is a wind-up and then the act: <see cref="PrepTime"/> is the wind-up the screen colours
/// while it runs, <see cref="ActionTime"/> is the act, and <see cref="SpeedMod"/> is what the
/// combatant's own Speed is multiplied by for this action. The effect blocks the action fires are
/// not on the row: they are the same <c>InstantEffectBlock</c> shape the world already publishes
/// under <c>effect-blocks</c> for the owners that lane covered, and widening that reader is a
/// different change from naming the action.
/// </remarks>
internal readonly struct WorldCharacterAction : IWorldEntity
{
    internal WorldCharacterAction(
        Guid characterActionId,
        double prepTime,
        double actionTime,
        double speedMod,
        string description)
    {
        CharacterActionId = characterActionId;
        PrepTime = prepTime;
        ActionTime = actionTime;
        SpeedMod = speedMod;
        Description = description ?? string.Empty;
    }

    internal Guid CharacterActionId { get; }

    public Guid EntityId => CharacterActionId;

    /// <summary>Seconds of wind-up before the action fires.</summary>
    internal double PrepTime { get; }

    /// <summary>Seconds the action itself takes.</summary>
    internal double ActionTime { get; }

    /// <summary>What this action multiplies the actor's own speed by.</summary>
    internal double SpeedMod { get; }

    internal string Description { get; }
}

/// <summary>What a combatant does: <c>CharacterActionSO.All</c>.</summary>
internal sealed class WorldCharacterActionBinder : WorldPlainBinder<WorldCharacterAction>
{
    private Func<object, Guid>? _id;
    private Func<object, double>? _prepTime;
    private Func<object, double>? _actionTime;
    private Func<object, double>? _speedMod;
    private Func<object, string>? _description;

    internal override string Category => "character actions";

    internal override string TypeName => "CharacterActionSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _prepTime = bind.Field<double>("prepTime");
        _actionTime = bind.Field<double>("actionTime");
        _speedMod = bind.Field<double>("speedMod");
        _description = bind.Field<string>("description");
        return bind.Failure;
    }

    internal override WorldCharacterAction Read(object entity) =>
        new(
            _id!(entity),
            _prepTime!(entity),
            _actionTime!(entity),
            _speedMod!(entity),
            _description!(entity));
}

/// <summary>
/// One creature family — the word a ritual's combatants are classified by.
/// </summary>
/// <remarks>
/// The family word already reaches a reader as a keyword on every <c>characters</c> row, because
/// <c>CharacterSO.characterTypes</c> is one of the thirteen owners the keyword walk binds. What was
/// missing was the far side of that edge: the id the keyword resolves through answered no category,
/// so a reader who saw <c>keywords: Aberration</c> and asked what an Aberration is got its identity
/// and nothing else. The type asset stores no scalar of its own, so the row is the handle — the
/// same shape <c>plot-node-action-types</c> and <c>passive-ability-types</c> already publish.
/// </remarks>
internal readonly struct WorldCharacterType : IWorldEntity
{
    internal WorldCharacterType(Guid characterTypeId, string description)
    {
        CharacterTypeId = characterTypeId;
        Description = description ?? string.Empty;
    }

    internal Guid CharacterTypeId { get; }

    public Guid EntityId => CharacterTypeId;

    internal string Description { get; }
}

/// <summary>The creature families: <c>CharacterTypeSO.All</c>.</summary>
internal sealed class WorldCharacterTypeBinder : WorldPlainBinder<WorldCharacterType>
{
    private Func<object, Guid>? _id;
    private Func<object, string>? _description;

    internal override string Category => "character types";

    internal override string TypeName => "CharacterTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _description = bind.Field<string>("description");
        return bind.Failure;
    }

    internal override WorldCharacterType Read(object entity) =>
        new(_id!(entity), _description!(entity));
}
