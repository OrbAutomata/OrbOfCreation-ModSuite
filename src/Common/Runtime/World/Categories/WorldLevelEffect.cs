using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One authored modifier a level buys — the <c>+1 Max Druidry Lv</c> an upgrade's tooltip prints per
/// level, as the three numbers the game's modifier arithmetic is made of plus the thing the number is
/// about.
/// </summary>
/// <remarks>
/// <para>
/// Six holders author these and the game reads them through three record classes, so the wire carries
/// one tuple rather than three: <see cref="Property"/> says which of the target's properties moves,
/// <see cref="TargetId"/> names the thing it moves on, and <see cref="ModifierType"/>,
/// <see cref="Amount"/> and <see cref="Order"/> are the same three fields
/// <see cref="WorldModifierVariable"/> and <see cref="WorldGlyphFactor"/> publish, under the same
/// names, because they are the same arithmetic.
/// </para>
/// <para>
/// The three record classes name their target three ways and the column absorbs all three.
/// <c>UpgradeableObject.UpgradeEffectModifier</c> carries an object reference plus a
/// <c>propertyType</c> string, so the string is the property and the object is the target.
/// <c>ResourceSO.PersistentEffect</c> carries a resource reference plus a <c>ModifiableType</c>, so
/// the enum's own member name is the property and the resource is the target. A number-variable
/// tuple carries only the variable, so the variable is the target and there is no property at all —
/// the game prints <c>+1 Max Druidry Lv</c> with no property word, because the variable is the whole
/// of what moves.
/// </para>
/// <para>
/// <see cref="Amount"/> is the modifier's <c>adjustReal</c> for the same reason
/// <see cref="WorldGlyphFactor.Amount"/> is: <c>ConvertToReal</c> adds one for the multiplicative
/// kinds, so <c>adjustReal</c> is the number the screen prints and the number the modifier registry
/// beside this table already publishes. Nothing is pre-multiplied and the kind is never folded away.
/// </para>
/// </remarks>
internal readonly struct WorldLevelEffect
{
    internal WorldLevelEffect(
        Guid ownerId,
        int ordinal,
        string property,
        Guid targetId,
        int modifierType,
        BigDouble amount,
        int order)
    {
        OwnerId = ownerId;
        Ordinal = ordinal;
        Property = property ?? string.Empty;
        TargetId = targetId;
        ModifierType = modifierType;
        Amount = amount;
        Order = order;
    }

    /// <summary>The levelable entity whose authored effects these are.</summary>
    internal Guid OwnerId { get; }

    /// <summary>The tuple's position in its owner's authored order.</summary>
    internal int Ordinal { get; }

    /// <summary>
    /// Which of the target's properties the modifier moves, under the game's own word for it, or
    /// empty where the game authors none because the target is the whole of what moves.
    /// </summary>
    internal string Property { get; }

    /// <summary>
    /// The entity the modifier is applied to, or <see cref="Guid.Empty"/> where the reference is
    /// unset.
    /// </summary>
    /// <remarks>
    /// Empty is a published fact rather than a dropped row. A tuple whose reference the build does
    /// not author still says which property it moves and by how much, and a reader meets the same
    /// shape whether or not the edge exists.
    /// </remarks>
    internal Guid TargetId { get; }

    /// <summary>
    /// The game's <c>ValueModifierType</c> as its underlying integer, exactly as
    /// <see cref="WorldModifierVariable.ModifierType"/> carries it.
    /// </summary>
    internal int ModifierType { get; }

    /// <summary>The modifier's magnitude — the original's <c>adjustReal</c>.</summary>
    internal BigDouble Amount { get; }

    internal int Order { get; }
}

/// <summary>Publishes the tuple readings, sorted by owner and then by authored position.</summary>
internal static class WorldLevelEffectDeriver
{
    internal static PublicationTable<WorldLevelEffect> Build(
        WorldRelationBuffer<WorldLevelEffect> buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Count == 0) return PublicationTable<WorldLevelEffect>.Empty;

        var rows = new WorldLevelEffect[buffer.Count];
        for (var index = 0; index < rows.Length; index++) rows[index] = buffer[index];
        Array.Sort(rows, static (left, right) =>
        {
            var owner = left.OwnerId.CompareTo(right.OwnerId);
            return owner != 0 ? owner : left.Ordinal.CompareTo(right.Ordinal);
        });
        return PublicationTable<WorldLevelEffect>.Create(rows, rows.Length);
    }
}

/// <summary>Range lookup over the level-effect table, which is keyed by owner and then position.</summary>
internal static class WorldLevelEffectLookup
{
    /// <summary>One owner's tuples, as a contiguous range of the sorted table.</summary>
    internal static bool TryFindRange(
        PublicationTable<WorldLevelEffect> table,
        Guid ownerId,
        out int start,
        out int count)
    {
        var low = 0;
        var high = table.Count - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = table[middle].OwnerId.CompareTo(ownerId);
            if (comparison < 0) low = middle + 1;
            else
            {
                if (comparison == 0) found = middle;
                high = middle - 1;
            }
        }

        if (found < 0)
        {
            start = 0;
            count = 0;
            return false;
        }

        start = found;
        var end = found + 1;
        while (end < table.Count && table[end].OwnerId == ownerId) end++;
        count = end - found;
        return true;
    }
}

/// <summary>
/// Every authored modifier tuple the game applies, from the seven registries that author one. It
/// claims no identities: each owner is already claimed by its own category.
/// </summary>
/// <remarks>
/// <para>
/// One collector rather than two, because it is one subject and one of the registries is walked for
/// both halves of it. A glyph's fifteen inline <c>ValueModifier</c> slots are what the glyph does at
/// any level; its <c>levelingEffects</c> are what one more level of it buys. Both are read in the
/// same pass over <c>GlyphSO.All</c>, and the six per-level holders are read in the same shape
/// beside them.
/// </para>
/// <para>
/// A slot is published only when the game itself would print it, and the game's own predicate is
/// <c>ValueModifier.IsEmpty()</c> — the amount equals its type's identity, which is one for the
/// multiplicative kinds and zero for the rest. <see cref="GameValueModifier.IsEmpty"/> is that method
/// ported, so the rows are exactly the lines a tooltip carries rather than a convenient approximation
/// of them.
/// </para>
/// <para>
/// <c>TimeRuneSO.onLevelEffects</c> is walked and authors nothing on this build: its blocks are
/// <c>InstantEffectBlock</c>s whose scripts grant advancement experience rather than apply a
/// modifier. Walking it anyway is the same discipline that captures <c>spellBaseCooldown</c> on a
/// build where no glyph fills it — a build that authors one publishes it rather than silently losing
/// it.
/// </para>
/// </remarks>
internal sealed class WorldEffectFactorReader : IWorldCategoryReader
{
    private readonly Type? _glyphType;
    private readonly string _unavailable;

    private readonly Func<object, Guid>? _glyphId;
    private readonly Func<object, int>[] _slotTypes;
    private readonly Func<object, BigDouble>[] _slotAmounts;
    private readonly Func<object, int>[] _slotOrders;
    private readonly Func<object?>?[] _playerVariables;
    private readonly Func<object, Guid>? _playerVariableId;

    private readonly WorldLevelEffectHolder[] _holders;
    private readonly WorldLevelEffectScripts _scripts;
    private readonly Func<object, Guid>? _upgradeId;
    private readonly Func<object, IList?>? _numberVariableEffects;
    private readonly Func<object, IList?>? _upgradeableObjectEffects;
    private readonly Func<object, IList?>? _resourceEffects;
    private readonly Func<object, Guid>? _tupleTargetId;
    private readonly Func<object, int>? _tupleType;
    private readonly Func<object, BigDouble>? _tupleAmount;
    private readonly Func<object, int>? _tupleOrder;
    private readonly Type? _upgradeType;

    internal WorldEffectFactorReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));

        var slots = WorldGlyphFactorSlots.All;
        _slotTypes = new Func<object, int>[slots.Length];
        _slotAmounts = new Func<object, BigDouble>[slots.Length];
        _slotOrders = new Func<object, int>[slots.Length];
        _playerVariables = new Func<object?>?[slots.Length];
        _holders = Array.Empty<WorldLevelEffectHolder>();
        _scripts = WorldLevelEffectScripts.Unbound;
        var missing = new List<string>();

        _glyphType = resolveType("GlyphSO");
        _upgradeType = resolveType("UpgradeSO");
        if (_glyphType is null || _upgradeType is null)
        {
            _unavailable = "the " + (_glyphType is null ? "GlyphSO" : "UpgradeSO") +
                " type was not found on this build";
            return;
        }

        var glyph = new WorldMemberBinding(_glyphType, "GlyphSO");
        _glyphId = glyph.Call<Guid>("GetGuid");
        for (var index = 0; index < slots.Length; index++)
        {
            var field = slots[index].Field;
            _slotTypes[index] = glyph.NestedEnumField(field, "type")!;
            _slotAmounts[index] = glyph.NestedField<BigDouble>(field, "adjustReal")!;
            _slotOrders[index] = glyph.NestedField<int>(field, "order")!;
        }

        // The four slots the game prints against a variable it reads off the player rather than
        // against a statistic. Each accessor is two loads and a return in the pinned assembly —
        // `ldsfld Player._instance; ldfld Player.spellCriticalCastRating; ret` — so the edge is a
        // stored reference the game hands over, never a name this suite chose for it.
        var player = resolveType("Player");
        for (var index = 0; index < slots.Length; index++)
        {
            var accessor = slots[index].PlayerAccessor;
            if (accessor.Length == 0) continue;
            _playerVariables[index] = NativeAccessorBinder.CallStaticValue(
                player?.GetMethod(accessor, PublicStatic, null, Type.EmptyTypes, null));
            if (_playerVariables[index] is null) missing.Add("Player." + accessor + "()");
        }

        var doubleVariable = resolveType("DoubleVariable");
        var variables = doubleVariable is null
            ? null
            : new WorldMemberBinding(doubleVariable, "DoubleVariable");
        _playerVariableId = variables?.Call<Guid>("GetGuid");
        if (_playerVariableId is null) missing.Add("DoubleVariable.GetGuid()");

        _scripts = new WorldLevelEffectScripts(resolveType, missing);

        var upgrade = new WorldMemberBinding(_upgradeType, "UpgradeSO");
        _upgradeId = upgrade.Call<Guid>("GetGuid");
        var permanent = upgrade.Through("permanentEffects");
        _numberVariableEffects = permanent.CollectionField("numberVariableEffects");
        _upgradeableObjectEffects = permanent.CollectionField("upgradeableObjectEffects");
        _resourceEffects = permanent.CollectionField("resourceEffects");

        var tuple = permanent.Elements(
            permanent.CollectionElementType("numberVariableEffects"), "TupleMod");
        _tupleTargetId = tuple.ReferenceGuid("item");
        _tupleType = tuple.NestedEnumField("modifier", "type");
        _tupleAmount = tuple.NestedField<BigDouble>("modifier", "adjustReal");
        _tupleOrder = tuple.NestedField<int>("modifier", "order");

        _holders = new[]
        {
            Holder(resolveType, missing, "GlyphSO", "levelingEffects"),
            Holder(resolveType, missing, "ResourceTypeSO", "levelEffects"),
            Holder(resolveType, missing, "EquipmentTypeSO", "levelEffects"),
            Holder(resolveType, missing, "SpellTypeSO", "perLevelEffects"),
            Holder(resolveType, missing, "TimeRuneSO", "onLevelEffects"),
        };

        if (glyph.Failure.Length > 0) missing.Add(glyph.Failure);
        if (upgrade.Failure.Length > 0) missing.Add(upgrade.Failure);
        _unavailable = missing.Count == 0 ? string.Empty : string.Join("; ", missing);
    }

    private const System.Reflection.BindingFlags PublicStatic =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;

    private static WorldLevelEffectHolder Holder(
        Func<string, Type?> resolveType,
        List<string> missing,
        string typeName,
        string field)
    {
        var type = resolveType(typeName);
        if (type is null)
        {
            missing.Add("the " + typeName + " type was not found on this build");
            return new WorldLevelEffectHolder(null, typeName, null, null, null);
        }

        var bind = new WorldMemberBinding(type, typeName);
        var ownerId = bind.Call<Guid>("GetGuid");
        var blocks = bind.CollectionField(field);
        var block = bind.Elements(bind.CollectionElementType(field), typeName + "." + field);
        var scripts = block.CollectionField("effectScripts");
        if (bind.Failure.Length > 0) missing.Add(bind.Failure);
        return new WorldLevelEffectHolder(type, typeName, ownerId, blocks, scripts);
    }

    public string Category => "effect factors";

    public bool IsAvailable => _glyphType is not null && _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        var factors = frame.GlyphEffects;
        var levels = frame.LevelEffects;
        factors.Reset();
        levels.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var glyphs = NativeAccessorBinder.StaticList(_glyphType, "All");
        if (glyphs is null)
            return WorldCategoryReport.Missing(Category, "the GlyphSO registry was unreadable");

        var slots = WorldGlyphFactorSlots.All;
        var variableIds = new Guid[slots.Length];
        for (var index = 0; index < slots.Length; index++)
        {
            var read = _playerVariables[index];
            var variable = read?.Invoke();
            variableIds[index] = variable is null ? Guid.Empty : _playerVariableId!(variable);
        }

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;

        for (var index = 0; index < glyphs.Count; index++)
        {
            var glyph = glyphs[index];
            if (glyph is null) continue;
            try
            {
                sampled += ReadGlyphSlots(glyph, variableIds, factors);
            }
            catch (Exception ex)
            {
                skipped++;
                if (firstFailure.Length == 0)
                    firstFailure = "reading a glyph's factors threw: " + ex.GetBaseException().Message;
            }
        }

        var upgrades = NativeAccessorBinder.StaticList(_upgradeType, "All");
        if (upgrades is null)
            return WorldCategoryReport.Missing(Category, "the UpgradeSO registry was unreadable");

        for (var index = 0; index < upgrades.Count; index++)
        {
            var upgrade = upgrades[index];
            if (upgrade is null) continue;
            try
            {
                sampled += ReadPermanentEffects(upgrade, levels);
            }
            catch (Exception ex)
            {
                skipped++;
                if (firstFailure.Length == 0)
                {
                    firstFailure = "reading an upgrade's permanent effects threw: " +
                        ex.GetBaseException().Message;
                }
            }
        }

        for (var holder = 0; holder < _holders.Length; holder++)
        {
            var owners = NativeAccessorBinder.StaticList(_holders[holder].Type, "All");
            if (owners is null)
            {
                return WorldCategoryReport.Missing(
                    Category, "the " + _holders[holder].TypeName + " registry was unreadable");
            }

            for (var index = 0; index < owners.Count; index++)
            {
                var owner = owners[index];
                if (owner is null) continue;
                try
                {
                    sampled += ReadBlocks(in _holders[holder], owner, levels);
                }
                catch (Exception ex)
                {
                    skipped++;
                    if (firstFailure.Length == 0)
                    {
                        firstFailure = "reading a " + _holders[holder].TypeName +
                            "'s level effects threw: " + ex.GetBaseException().Message;
                    }
                }
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }

    private int ReadGlyphSlots(
        object glyph,
        Guid[] variableIds,
        WorldRelationBuffer<WorldGlyphFactor> buffer)
    {
        var glyphId = _glyphId!(glyph);
        if (glyphId == Guid.Empty) return 0;

        var slots = WorldGlyphFactorSlots.All;
        var appended = 0;
        for (var index = 0; index < slots.Length; index++)
        {
            var modifierType = _slotTypes[index](glyph);
            var amount = _slotAmounts[index](glyph);
            var order = _slotOrders[index](glyph);
            var modifier = new GameValueModifier((GameValueModifierType)modifierType, amount, order);
            if (modifier.IsEmpty()) continue;

            buffer.Append(new WorldGlyphFactor(
                glyphId,
                slots[index].Field,
                Guid.Empty,
                variableIds[index],
                modifierType,
                amount,
                order));
            appended++;
        }

        return appended;
    }

    private int ReadPermanentEffects(object upgrade, WorldRelationBuffer<WorldLevelEffect> buffer)
    {
        var upgradeId = _upgradeId!(upgrade);
        if (upgradeId == Guid.Empty) return 0;

        var ordinal = 0;
        var appended = 0;
        appended += AppendTupleMods(upgradeId, _numberVariableEffects!(upgrade), buffer, ref ordinal);
        appended += AppendScripts(
            upgradeId, _upgradeableObjectEffects!(upgrade), buffer, ref ordinal);
        appended += AppendScripts(upgradeId, _resourceEffects!(upgrade), buffer, ref ordinal);
        return appended;
    }

    private int AppendTupleMods(
        Guid ownerId,
        IList? tuples,
        WorldRelationBuffer<WorldLevelEffect> buffer,
        ref int ordinal)
    {
        var count = tuples?.Count ?? 0;
        var appended = 0;
        for (var index = 0; index < count; index++)
        {
            var entry = tuples![index];
            if (entry is null) continue;
            appended += Append(
                ownerId,
                string.Empty,
                _tupleTargetId!(entry),
                _tupleType!(entry),
                _tupleAmount!(entry),
                _tupleOrder!(entry),
                buffer,
                ref ordinal);
        }

        return appended;
    }

    private int ReadBlocks(
        in WorldLevelEffectHolder holder,
        object owner,
        WorldRelationBuffer<WorldLevelEffect> buffer)
    {
        var ownerId = holder.OwnerId!(owner);
        if (ownerId == Guid.Empty) return 0;

        var blocks = holder.Blocks!(owner);
        var count = blocks?.Count ?? 0;
        var ordinal = 0;
        var appended = 0;
        for (var index = 0; index < count; index++)
        {
            var block = blocks![index];
            if (block is null) continue;
            appended += AppendScripts(ownerId, holder.Scripts!(block), buffer, ref ordinal);
        }

        return appended;
    }

    private int AppendScripts(
        Guid ownerId,
        IList? scripts,
        WorldRelationBuffer<WorldLevelEffect> buffer,
        ref int ordinal)
    {
        var count = scripts?.Count ?? 0;
        var appended = 0;
        for (var index = 0; index < count; index++)
        {
            var script = scripts![index];
            if (script is null) continue;
            if (!_scripts.TryRead(
                    script, out var property, out var targetId, out var type, out var amount,
                    out var order))
            {
                continue;
            }
            appended += Append(
                ownerId, property, targetId, type, amount, order, buffer, ref ordinal);
        }

        return appended;
    }

    private static int Append(
        Guid ownerId,
        string property,
        Guid targetId,
        int modifierType,
        BigDouble amount,
        int order,
        WorldRelationBuffer<WorldLevelEffect> buffer,
        ref int ordinal)
    {
        var modifier = new GameValueModifier((GameValueModifierType)modifierType, amount, order);
        if (modifier.IsEmpty()) return 0;
        buffer.Append(new WorldLevelEffect(
            ownerId, ordinal++, property, targetId, modifierType, amount, order));
        return 1;
    }
}

/// <summary>One registry whose entities author per-level effect blocks.</summary>
internal readonly struct WorldLevelEffectHolder
{
    internal WorldLevelEffectHolder(
        Type? type,
        string typeName,
        Func<object, Guid>? ownerId,
        Func<object, IList?>? blocks,
        Func<object, IList?>? scripts)
    {
        Type = type;
        TypeName = typeName;
        OwnerId = ownerId;
        Blocks = blocks;
        Scripts = scripts;
    }

    internal Type? Type { get; }
    internal string TypeName { get; }
    internal Func<object, Guid>? OwnerId { get; }
    internal Func<object, IList?>? Blocks { get; }
    internal Func<object, IList?>? Scripts { get; }
}

/// <summary>
/// The three record classes an authored effect applies a modifier through, and how each names what it
/// moves.
/// </summary>
/// <remarks>
/// <para>
/// All three are reached by type because the lists that hold them are typed as interfaces, so the
/// element type says nothing about what the entries are. They are the same three classes whichever
/// holder authors them: an upgrade's <c>permanentEffects</c> stores two of them directly, and every
/// per-level effect block stores all three as scripts.
/// </para>
/// <para>
/// A <c>ResourceSO.ModifiableType</c> reaches the wire as the enum member's own name rather than as
/// its ordinal, and the names are read off the game's enum at bind time rather than copied here — a
/// copied table would say the wrong word the day the game inserts a member, and an ordinal says
/// nothing to a reader at all.
/// </para>
/// </remarks>
internal sealed class WorldLevelEffectScripts
{
    internal static readonly WorldLevelEffectScripts Unbound = new();

    private readonly Type? _objectModifier;
    private readonly Type? _numberVariableEffect;
    private readonly Type? _resourceEffect;

    private readonly Func<object, string>? _propertyType;
    private readonly Func<object, Guid>? _upgradeableObjectId;
    private readonly Func<object, Guid>? _numberVariableId;
    private readonly Func<object, Guid>? _resourceId;
    private readonly Func<object, int>? _modifiableType;
    private readonly string[] _modifiableTypeNames;

    private readonly Func<object, int>?[] _types;
    private readonly Func<object, BigDouble>?[] _amounts;
    private readonly Func<object, int>?[] _orders;

    private WorldLevelEffectScripts()
    {
        _modifiableTypeNames = Array.Empty<string>();
        _types = new Func<object, int>?[3];
        _amounts = new Func<object, BigDouble>?[3];
        _orders = new Func<object, int>?[3];
    }

    internal WorldLevelEffectScripts(Func<string, Type?> resolveType, List<string> missing)
    {
        _objectModifier = resolveType("UpgradeableObject+UpgradeEffectModifier");
        _numberVariableEffect = resolveType("NumberVariable+PersistentEffect");
        _resourceEffect = resolveType("ResourceSO+PersistentEffect");
        _types = new Func<object, int>?[3];
        _amounts = new Func<object, BigDouble>?[3];
        _orders = new Func<object, int>?[3];
        _modifiableTypeNames = Array.Empty<string>();

        if (_objectModifier is null || _numberVariableEffect is null || _resourceEffect is null)
        {
            missing.Add("the " + (
                _objectModifier is null ? "UpgradeableObject+UpgradeEffectModifier"
                : _numberVariableEffect is null ? "NumberVariable+PersistentEffect"
                : "ResourceSO+PersistentEffect") + " type was not found on this build");
            return;
        }

        var objects = new WorldMemberBinding(_objectModifier, "UpgradeEffectModifier");
        _propertyType = objects.Field<string>("propertyType");
        _upgradeableObjectId = objects.ReferenceGuid("upgradeableObject");
        Bind(objects, 0);

        var variables = new WorldMemberBinding(_numberVariableEffect, "NumberVariable.PersistentEffect");
        _numberVariableId = variables.ReferenceGuid("numberVariable");
        Bind(variables, 1);

        var resources = new WorldMemberBinding(_resourceEffect, "ResourceSO.PersistentEffect");
        _resourceId = resources.ReferenceGuid("resource");
        _modifiableType = resources.EnumField("upgradeType");
        Bind(resources, 2);

        _modifiableTypeNames = EnumNames(_resourceEffect.GetField(
            "upgradeType",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            ?.FieldType);

        if (objects.Failure.Length > 0) missing.Add(objects.Failure);
        if (variables.Failure.Length > 0) missing.Add(variables.Failure);
        if (resources.Failure.Length > 0) missing.Add(resources.Failure);
        if (_modifiableTypeNames.Length == 0)
            missing.Add("ResourceSO.PersistentEffect.upgradeType is not a readable enum");
    }

    private void Bind(WorldMemberBinding binding, int slot)
    {
        _types[slot] = binding.NestedEnumField("modifier", "type");
        _amounts[slot] = binding.NestedField<BigDouble>("modifier", "adjustReal");
        _orders[slot] = binding.NestedField<int>("modifier", "order");
    }

    /// <summary>
    /// The tuple this script applies, or false where the script applies no modifier at all — the
    /// advancement grants and treasure draws that share the same block lists.
    /// </summary>
    internal bool TryRead(
        object script,
        out string property,
        out Guid targetId,
        out int modifierType,
        out BigDouble amount,
        out int order)
    {
        property = string.Empty;
        targetId = Guid.Empty;
        modifierType = 0;
        amount = BigDouble.Zero;
        order = 0;

        int slot;
        if (_objectModifier?.IsInstanceOfType(script) == true)
        {
            slot = 0;
            property = _propertyType!(script) ?? string.Empty;
            targetId = _upgradeableObjectId!(script);
        }
        else if (_numberVariableEffect?.IsInstanceOfType(script) == true)
        {
            slot = 1;
            targetId = _numberVariableId!(script);
        }
        else if (_resourceEffect?.IsInstanceOfType(script) == true)
        {
            slot = 2;
            targetId = _resourceId!(script);
            var ordinal = _modifiableType!(script);
            property = ordinal >= 0 && ordinal < _modifiableTypeNames.Length
                ? _modifiableTypeNames[ordinal]
                : string.Empty;
        }
        else
        {
            return false;
        }

        modifierType = _types[slot]!(script);
        amount = _amounts[slot]!(script);
        order = _orders[slot]!(script);
        return true;
    }

    /// <summary>
    /// The enum's member names by ordinal, or nothing where the ordinals are not a dense range this
    /// can index. Read once at bind time, so a row costs an array index and allocates nothing.
    /// </summary>
    private static string[] EnumNames(Type? enumType)
    {
        if (enumType is null || !enumType.IsEnum) return Array.Empty<string>();
        var values = Enum.GetValues(enumType);
        var names = Enum.GetNames(enumType);
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var highest = 0;
        for (var index = 0; index < values.Length; index++)
        {
            var ordinal = Convert.ToInt32(values.GetValue(index), invariant);
            if (ordinal < 0 || ordinal > 1024) return Array.Empty<string>();
            if (ordinal > highest) highest = ordinal;
        }

        var byOrdinal = new string[highest + 1];
        for (var index = 0; index < byOrdinal.Length; index++) byOrdinal[index] = string.Empty;
        for (var index = 0; index < values.Length; index++)
            byOrdinal[Convert.ToInt32(values.GetValue(index), invariant)] = names[index];

        return byOrdinal;
    }
}
