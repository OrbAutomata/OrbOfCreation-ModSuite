using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One position in the player's equipped spell loadout, as the game answers for it.
/// </summary>
/// <remarks>
/// <para>
/// A slot has no identity of its own. The loadout is an ordered row of positions the player fills and
/// empties, two of which may hold the same spell, and the game addresses a cast by position — so the
/// position is the key and <see cref="SpellRecipeId"/> is a fact about the occupant rather than the
/// row's name. That is why this table is not identity-keyed and is reached by
/// <see cref="WorldSpellSlotLookup"/> rather than <c>WorldLookup</c>.
/// </para>
/// <para>
/// Every readiness member here is the game's own answer, not a reconstruction of one.
/// <see cref="CasterAvailable"/> is <c>SpellManager.CanCastASpell()</c>,
/// <see cref="CastReady"/> is <c>Spell.CanCast()</c>, and the three fields under it are the game's
/// classification of why a refusal happened. Publishing the composite and its terms together is what
/// lets a planner rank without guessing and still leaves the boundary the authority: a plan is made
/// against this reading and re-checked live before the game is touched (M3).
/// </para>
/// <para>
/// The row is deliberately positive-sense. The game says <c>IsEmpty</c>; this publishes
/// <see cref="Occupied"/>, so a slot whose reading could not be taken at all is absent from the table
/// rather than present and claiming to hold something. Absence and emptiness both mean "nothing to
/// cast here", which is the direction a missed reading should fail in.
/// </para>
/// </remarks>
internal readonly struct WorldSpellSlot
{
    internal WorldSpellSlot(
        int slotIndex,
        Guid spellRecipeId,
        bool occupied,
        bool casting,
        bool readyingCast,
        bool attuning,
        bool channeled,
        bool toggled,
        bool chargeable,
        bool castReady,
        bool chargeAvailable,
        bool resourcesCovered,
        int currentCharges,
        int maximumCharges,
        BigDouble cooldownRemaining,
        bool casterAvailable = true)
        : this(
            slotIndex,
            Guid.Empty,
            spellRecipeId,
            occupied,
            casting,
            readyingCast,
            attuning,
            channeled,
            toggled,
            chargeable,
            castReady,
            chargeAvailable,
            chargeAvailable && !casting,
            resourcesCovered,
            currentCharges,
            maximumCharges,
            cooldownRemaining,
            0,
            0,
            0,
            0,
            false,
            false,
            PublicationTable<WorldSpellSlotGlyph>.Empty,
            false,
            casterAvailable)
    {
    }

    internal WorldSpellSlot(
        int slotIndex,
        Guid spellInstanceId,
        Guid spellRecipeId,
        bool occupied,
        bool casting,
        bool readyingCast,
        bool attuning,
        bool channeled,
        bool toggled,
        bool chargeable,
        bool castReady,
        bool chargeAvailable,
        bool resourcesCovered,
        int currentCharges,
        int maximumCharges,
        BigDouble cooldownRemaining,
        bool casterAvailable = true)
        : this(
            slotIndex,
            spellInstanceId,
            spellRecipeId,
            occupied,
            casting,
            readyingCast,
            attuning,
            channeled,
            toggled,
            chargeable,
            castReady,
            chargeAvailable,
            chargeAvailable && !casting,
            resourcesCovered,
            currentCharges,
            maximumCharges,
            cooldownRemaining,
            0,
            0,
            0,
            0,
            false,
            false,
            PublicationTable<WorldSpellSlotGlyph>.Empty,
            false,
            casterAvailable)
    {
    }

    internal WorldSpellSlot(
        int slotIndex,
        Guid spellInstanceId,
        Guid spellRecipeId,
        bool occupied,
        bool casting,
        bool readyingCast,
        bool attuning,
        bool channeled,
        bool toggled,
        bool chargeable,
        bool castReady,
        bool chargeAvailable,
        bool resourcesCovered,
        int currentCharges,
        int maximumCharges,
        BigDouble cooldownRemaining,
        int outputLevel,
        int effectiveLevel,
        int requiredMasteryLevel,
        int recipeMasteryLevel,
        bool durationSpell,
        bool usageRequirementsMet,
        PublicationTable<WorldSpellSlotGlyph> augmentGlyphs,
        bool casterAvailable = true)
        : this(
            slotIndex,
            spellInstanceId,
            spellRecipeId,
            occupied,
            casting,
            readyingCast,
            attuning,
            channeled,
            toggled,
            chargeable,
            castReady,
            chargeAvailable,
            chargeAvailable && !casting,
            resourcesCovered,
            currentCharges,
            maximumCharges,
            cooldownRemaining,
            outputLevel,
            effectiveLevel,
            requiredMasteryLevel,
            recipeMasteryLevel,
            durationSpell,
            usageRequirementsMet,
            augmentGlyphs,
            false,
            casterAvailable)
    {
    }

    internal WorldSpellSlot(
        int slotIndex,
        Guid spellInstanceId,
        Guid spellRecipeId,
        bool occupied,
        bool casting,
        bool readyingCast,
        bool attuning,
        bool channeled,
        bool toggled,
        bool chargeable,
        bool castReady,
        bool chargeAvailable,
        bool canRemove,
        bool resourcesCovered,
        int currentCharges,
        int maximumCharges,
        BigDouble cooldownRemaining,
        int outputLevel,
        int effectiveLevel,
        int requiredMasteryLevel,
        int recipeMasteryLevel,
        bool durationSpell,
        bool usageRequirementsMet,
        PublicationTable<WorldSpellSlotGlyph> augmentGlyphs,
        bool cancellationEnabled = false,
        bool casterAvailable = true,
        int castCount = 0)
    {
        CastCount = castCount;
        SlotIndex = slotIndex;
        SpellInstanceId = spellInstanceId;
        SpellRecipeId = spellRecipeId;
        Occupied = occupied;
        Casting = casting;
        ReadyingCast = readyingCast;
        Attuning = attuning;
        Channeled = channeled;
        Toggled = toggled;
        Chargeable = chargeable;
        CastReady = castReady;
        ChargeAvailable = chargeAvailable;
        CanRemove = canRemove;
        ResourcesCovered = resourcesCovered;
        CurrentCharges = currentCharges;
        MaximumCharges = maximumCharges;
        CooldownRemaining = cooldownRemaining;
        OutputLevel = outputLevel;
        EffectiveLevel = effectiveLevel;
        RequiredMasteryLevel = requiredMasteryLevel;
        RecipeMasteryLevel = recipeMasteryLevel;
        DurationSpell = durationSpell;
        UsageRequirementsMet = usageRequirementsMet;
        CancellationEnabled = cancellationEnabled;
        AugmentGlyphs = augmentGlyphs ??
            throw new ArgumentNullException(nameof(augmentGlyphs));
        CasterAvailable = casterAvailable;
    }

    /// <summary>
    /// The slot's position in the game's own list, counting the holes.
    /// </summary>
    /// <remarks>
    /// This is the number <c>SpellManager.FireSpellIndex</c> takes, so it must count empty positions
    /// the same way the game does. A row's position in this table is not it: the table omits slots it
    /// could not read, and a plan that fired the table's index would fire the wrong spell.
    /// </remarks>
    internal int SlotIndex { get; }

    /// <summary>
    /// How many times the game has counted this spell instance as manually cast.
    /// </summary>
    /// <remarks>
    /// The game's own counter: <c>Spell.ExecuteSpell()</c> adds one to <c>numCasts</c> on every cast
    /// whose data is <c>Manual</c>, which is the cast type <c>Spell.Cast()</c> creates and therefore
    /// the one a player press or an MCP fire produces. It is the single fact a firing loop can
    /// compare across two readings to learn whether anything fired — every other published slot fact
    /// either returns to its resting value within a frame or never moves for an instant spell.
    /// </remarks>
    internal int CastCount { get; }

    /// <summary>The runtime spell instance identity, distinct from its authored recipe identity.</summary>
    internal Guid SpellInstanceId { get; }

    /// <summary>Which spell the slot holds, or <see cref="Guid.Empty"/> when it holds none.</summary>
    internal Guid SpellRecipeId { get; }

    /// <summary>Whether the slot holds a spell at all.</summary>
    internal bool Occupied { get; }

    /// <summary>Whether the game says its one spell caster is available for another cast.</summary>
    internal bool CasterAvailable { get; }

    /// <summary>Whether a cast is under way in this slot right now.</summary>
    internal bool Casting { get; }

    /// <summary>
    /// Whether the slot is charging toward a cast, which is the state a full-charge hold keeps it in.
    /// </summary>
    internal bool ReadyingCast { get; }

    /// <summary>Whether the slot is attuning after a previous cast.</summary>
    internal bool Attuning { get; }

    /// <summary>Whether the spell channels, which occupies the caster until it ends.</summary>
    internal bool Channeled { get; }

    /// <summary>Whether the spell toggles rather than firing once — an aura.</summary>
    internal bool Toggled { get; }

    /// <summary>Whether the spell can be held at charge.</summary>
    internal bool Chargeable { get; }

    /// <summary>
    /// The game's own composite answer to "can this be cast now" — <c>Spell.CanCast()</c>.
    /// </summary>
    internal bool CastReady { get; }

    /// <summary>Whether a charge is off cooldown and available to spend.</summary>
    internal bool ChargeAvailable { get; }

    /// <summary>The game's own live <c>Spell.CanRemove()</c> answer.</summary>
    internal bool CanRemove { get; }

    /// <summary>Whether the game says the caster can currently pay for this spell.</summary>
    internal bool ResourcesCovered { get; }

    /// <summary>Charges banked right now.</summary>
    internal int CurrentCharges { get; }

    /// <summary>Charges the slot can bank.</summary>
    internal int MaximumCharges { get; }

    /// <summary>Seconds left before the next charge returns, on the game's own clock.</summary>
    internal BigDouble CooldownRemaining { get; }

    internal int OutputLevel { get; }
    internal int EffectiveLevel { get; }
    internal int RequiredMasteryLevel { get; }
    internal int RecipeMasteryLevel { get; }
    internal bool DurationSpell { get; }
    internal bool UsageRequirementsMet { get; }

    /// <summary>Whether the native Cancellable Spells setting permits an active toggle to stop.</summary>
    internal bool CancellationEnabled { get; }
    internal PublicationTable<WorldSpellSlotGlyph> AugmentGlyphs { get; }
}

/// <summary>One unique augment and its native stack count on an equipped spell.</summary>
internal readonly struct WorldSpellSlotGlyph
{
    internal WorldSpellSlotGlyph(Guid glyphId, int quantity)
    {
        GlyphId = glyphId;
        Quantity = quantity;
    }

    internal Guid GlyphId { get; }
    internal int Quantity { get; }
}

/// <summary>
/// Position lookup over the equipped-loadout table, which is sorted by slot position.
/// </summary>
/// <remarks>
/// A slot has no identity, so <see cref="WorldLookup"/> cannot reach one, and the table is not dense:
/// a position whose reading failed is absent. A consumer therefore asks for a position rather than
/// indexing, and gets a plain "no" when the game had nothing readable there.
/// </remarks>
internal static class WorldSpellSlotLookup
{
    /// <summary>The row holding <paramref name="slotIndex"/>, if the pass published one.</summary>
    internal static bool TryFind(
        PublicationTable<WorldSpellSlot> table,
        int slotIndex,
        out WorldSpellSlot slot)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = rows[middle].SlotIndex.CompareTo(slotIndex);
            if (comparison == 0)
            {
                slot = rows[middle];
                return true;
            }

            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        slot = default;
        return false;
    }
}

/// <summary>Every loadout slot as read, held where a cycle can own them.</summary>
internal sealed class WorldSpellSlotBuffer
{
    private const int InitialCapacity = 16;

    private WorldSpellSlot[] _samples = new WorldSpellSlot[InitialCapacity];
    private int _count;

    internal int Count => _count;

    internal ref readonly WorldSpellSlot this[int index] => ref _samples[index];

    internal void Reset() => _count = 0;

    internal void Append(in WorldSpellSlot sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

/// <summary>Publishes the loadout readings sorted by position, so the lookup can bisect them.</summary>
internal static class WorldSpellSlotDeriver
{
    internal static PublicationTable<WorldSpellSlot> Build(WorldSpellSlotBuffer buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Count == 0) return PublicationTable<WorldSpellSlot>.Empty;

        var derived = new WorldSpellSlot[buffer.Count];
        for (var index = 0; index < buffer.Count; index++) derived[index] = buffer[index];

        Array.Sort(derived, 0, derived.Length, SlotComparer.ByIndex);
        return PublicationTable<WorldSpellSlot>.Create(derived, derived.Length);
    }

    private sealed class SlotComparer : IComparer<WorldSpellSlot>
    {
        internal static readonly IComparer<WorldSpellSlot> ByIndex = new SlotComparer();

        public int Compare(WorldSpellSlot left, WorldSpellSlot right) =>
            left.SlotIndex.CompareTo(right.SlotIndex);
    }
}

/// <summary>
/// Reads the player's equipped spell loadout, and the cost of casting what is in it.
/// </summary>
/// <remarks>
/// <para>
/// Not a registry walk, and not a singleton read. <c>Spell</c> has no registry of its own and the
/// loadout hangs off <c>SpellManager.instance</c>, but the list holding it is an ordinary list
/// variable with a uuid, so it is reached through the identity registry every other lookup in the
/// suite already goes through — the same route <c>WorldActionQueueReader</c> takes, and for the same
/// reason. The manager's static readiness answer is sampled once beside that list because it gates
/// every slot together; the action boundary asks the same question again immediately before firing.
/// </para>
/// <para>
/// One reader fills two tables. The costs come from <c>GetCost()</c> and <c>GetDrainCost()</c> on the
/// same <c>Spell</c> the slot row was read from, and splitting them into a second traversal would walk
/// the loadout twice to answer two halves of one question. This is the shape the upgrade and structure
/// cost readers already share.
/// </para>
/// <para>
/// The costs are per slot rather than per recipe on purpose. A spell's price is its recipe's authored
/// cost after the slot's own modifier chain, and the recipe alone does not answer it — the game's
/// answer is only available from the equipped instance, which is what <c>GetCost()</c> is.
/// </para>
/// </remarks>
internal sealed class WorldSpellSlotReader : IWorldCategoryReader
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Type? _registryType;
    private readonly RuntimeIdentityRegistryBinding _registryBinding;
    private readonly Type? _listType;
    private readonly Type? _spellType;
    private readonly Type? _managerType;
    private readonly string _unavailable;

    private readonly Func<object, IList?>? _slots;

    private readonly Func<object, bool>? _isEmpty;
    private readonly Func<object, bool>? _isCasting;
    private readonly Func<object, bool>? _isReadyingCast;
    private readonly Func<object, bool>? _isAttuning;
    private readonly Func<object, bool>? _isChanneled;
    private readonly Func<object, bool>? _isToggled;
    private readonly Func<object, bool>? _canCharge;
    private readonly Func<object, bool>? _canCast;
    private readonly Func<object, bool>? _isChargeAvailable;
    private readonly Func<object, bool>? _canRemove;
    private readonly Func<object, bool>? _hasEnoughResources;
    private readonly Func<object, int>? _currentCharges;
    private readonly Func<object, int>? _castCount;
    private readonly Func<object, int>? _maximumCharges;
    private readonly Func<object, BigDouble>? _cooldown;
    private readonly Func<object, Guid>? _recipeId;
    private readonly Func<object, Guid>? _spellInstanceId;
    private readonly Func<object, int>? _outputLevel;
    private readonly Func<object, int>? _effectiveLevel;
    private readonly Func<object, int>? _requiredMasteryLevel;
    private readonly Func<object, int>? _recipeMasteryLevel;
    private readonly Func<object, bool>? _durationSpell;
    private readonly Func<object, bool>? _usageRequirementsMet;
    private readonly Func<object, object?>? _getAugmentGlyphs;
    private readonly Func<object, object, int>? _getGlyphQuantity;
    private readonly Func<object, Guid>? _glyphId;
    private readonly Func<object?>? _canCancelSpells;

    private readonly Func<object, object?>? _getCost;
    private readonly Func<object, object?>? _getDrainCost;
    private readonly Func<object?>? _canCastASpell;
    private readonly Func<object, IList?>? _costEntries;
    private readonly Func<object, Guid>? _entryResource;
    private readonly Func<object, BigDouble>? _entryValue;

    internal WorldSpellSlotReader(Type? registryType, Type? listType, Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));

        _registryType = registryType;
        _registryBinding = new RuntimeIdentityRegistryBinding(
            () => registryType, requireStableIdentityContract: false);
        _listType = listType;
        _managerType = resolveType("SpellManager");
        if (registryType is null)
        {
            _unavailable = "the IdScriptableObject type was not found on this build";
            return;
        }

        if (listType is null)
        {
            _unavailable = "the SpellListVariable type was not found on this build";
            return;
        }

        var list = new WorldMemberBinding(listType, "SpellListVariable");
        _slots = list.CollectionField("value");
        _spellType = list.CollectionElementType("value");

        var spell = list.Elements(_spellType, "Spell");
        _isEmpty = spell.Call<bool>("IsEmpty");
        _isCasting = spell.Call<bool>("IsCasting");
        _isReadyingCast = spell.Call<bool>("IsReadyingCast");
        _isAttuning = spell.Call<bool>("IsAttuning");
        _isChanneled = spell.Call<bool>("IsChanneled");
        _isToggled = spell.Call<bool>("IsToggledSpell");
        _canCharge = spell.Call<bool>("CanCharge");
        _canCast = spell.Call<bool>("CanCast");
        _isChargeAvailable = spell.Call<bool>("IsChargeAvailable");
        _canRemove = spell.Call<bool>("CanRemove");
        _hasEnoughResources = spell.Call<bool>("HasEnoughResources");
        _currentCharges = spell.Call<int>("GetCurrSpellCharges");
        // The game's own per-cast counter, read as the field it is. No accessor exists, and the
        // increment lives in Spell.ExecuteSpell() rather than in anything callable.
        _castCount = spell.Field<int>("numCasts");
        _maximumCharges = spell.Call<int>("GetMaxSpellCharges");
        _cooldown = spell.Call<BigDouble>("GetCooldownTimeRemaining");
        _recipeId = spell.CallReferenceGuid("get_reference");
        _spellInstanceId = spell.ReferenceGuid("guidContainer");
        _outputLevel = spell.Call<int>("GetOutputLevel");
        _effectiveLevel = spell.Call<int>("GetLevel");
        _requiredMasteryLevel = spell.Call<int>("GetRequiredLevel");
        _recipeMasteryLevel = spell.Call<int>("GetRecipeMasteryLevel");
        _durationSpell = spell.Call<bool>("IsDurationSpell");
        _usageRequirementsMet = spell.Call<bool>("HasMetUsageRequirements");
        var glyphType = resolveType("GlyphSO");
        var glyphListType = glyphType is null ? null : typeof(List<>).MakeGenericType(glyphType);
        var getAugmentGlyphs = _spellType?.GetMethod(
            "GetAugmentGlyphs", Instance, null, Type.EmptyTypes, null);
        var getGlyphQuantity = glyphType is null
            ? null
            : _spellType?.GetMethod(
                "GetQuantityOfGlyph", Instance, null, new[] { glyphType }, null);
        _glyphId = NativeAccessorBinder.Call<Guid>(glyphType, "GetGuid");
        var settingsType = resolveType("SettingsManager");
        var canCancelSpells = settingsType?.GetMethod(
            "CanCancelSpells",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
        if (getAugmentGlyphs?.ReturnType != glyphListType ||
            getGlyphQuantity?.ReturnType != typeof(int))
        {
            getAugmentGlyphs = null;
            getGlyphQuantity = null;
        }

        // The two cost accessors return a cost list rather than a value, so their entries are bound
        // off the declared return type the same way the upgrade reader binds its authored costs.
        var getCost = _spellType?.GetMethod("GetCost", Instance, null, Type.EmptyTypes, null);
        var getDrainCost = _spellType?.GetMethod("GetDrainCost", Instance, null, Type.EmptyTypes, null);
        var canCastASpell = _managerType?.GetMethod(
            "CanCastASpell", Static, null, Type.EmptyTypes, null);

        // Every member the audits above accepted is compiled into an accessor. A member that resolves
        // but will not compile leaves its accessor null, which is the same unavailability an absent
        // member produces and reaches the player through the same sentence.
        _getAugmentGlyphs = NativeAccessorBinder.CallValue(getAugmentGlyphs);
        _getGlyphQuantity = NativeAccessorBinder.CallWithObjectArgument<int>(getGlyphQuantity);
        _canCancelSpells = canCancelSpells?.ReturnType == typeof(bool)
            ? NativeAccessorBinder.CallStaticValue(canCancelSpells)
            : null;
        _canCastASpell = canCastASpell?.ReturnType == typeof(bool)
            ? NativeAccessorBinder.CallStaticValue(canCastASpell)
            : null;
        _getCost = NativeAccessorBinder.CallValue(getCost);
        _getDrainCost = NativeAccessorBinder.CallValue(getDrainCost);

        var costListType = getCost?.ReturnType;
        var entryType = NativeAccessorBinder.CollectionElementType(costListType, "costs");
        _costEntries = NativeAccessorBinder.CollectionField(costListType, "costs");
        _entryResource = NativeAccessorBinder.ReferenceGuid(entryType, "resource");
        _entryValue = NativeAccessorBinder.Field<BigDouble>(entryType, "valueBig");

        if (list.Failure.Length != 0)
        {
            _unavailable = list.Failure;
            return;
        }

        _unavailable = IsCostBound() && IsCompositionBound() &&
            _canCancelSpells is not null && _canCastASpell is not null
            ? string.Empty
            : "Spell did not expose its complete cast, level, augment, cancellation, or manager readiness state on this build";
    }

    public string Category => "spell slots";

    public bool IsAvailable =>
        _registryType is not null && _listType is not null && _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));

        var slots = frame.SpellSlots;
        var costs = frame.SpellCosts;
        slots.Reset();
        costs.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var source = _registryBinding.Read();
        if (!source.IsReady || source.Registry is null)
            return WorldCategoryReport.Missing(Category, source.Reason);
        var registry = source.Registry;

        // A loadout the registry does not hold yet is a fact about the save rather than about the
        // read: the game registers its list variables during initialisation, and a pass before that
        // has nothing to report rather than a shortfall to report.
        var loadout = registry[KnownEntities.ActiveSpells.Uuid];
        if (loadout is null)
            return new WorldCategoryReport(Category, WorldCategoryOutcome.Collected, 0, 0, string.Empty);

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;

        IList? values;
        bool cancellationEnabled;
        bool casterAvailable;
        try
        {
            values = _slots!(loadout);
            cancellationEnabled = _canCancelSpells!() is true;
            casterAvailable = (bool)_canCastASpell!()!;
        }
        catch (Exception ex)
        {
            return WorldCategoryReport.Missing(
                Category, $"reading the equipped loadout threw: {ex.GetBaseException().Message}");
        }

        var count = values?.Count ?? 0;
        for (var index = 0; index < count; index++)
        {
            // The position is the game's own, holes included, because that is what a cast is
            // addressed by. A hole publishes nothing: an absent row and an empty row both say
            // "nothing to cast here", and only one of them requires reading a null.
            var entry = values![index];
            if (entry is null) continue;
            if (entry.GetType() != _spellType)
            {
                Skip(ref skipped, ref firstFailure, $"slot {index} held an entry that is not a spell");
                continue;
            }

            try
            {
                Read(entry, index, cancellationEnabled, casterAvailable, slots, costs);
                sampled++;
            }
            catch (Exception ex)
            {
                Skip(
                    ref skipped,
                    ref firstFailure,
                    $"reading slot {index} threw: {ex.GetBaseException().Message}");
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }

    private void Read(
        object spell,
        int index,
        bool cancellationEnabled,
        bool casterAvailable,
        WorldSpellSlotBuffer slots,
        WorldSpellCostBuffer costs)
    {
        var occupied = !_isEmpty!(spell);
        if (!occupied)
        {
            slots.Append(new WorldSpellSlot(
                index, Guid.Empty, Guid.Empty, false, false, false, false, false, false, false, false, false,
                false, 0, 0, default, casterAvailable));
            return;
        }

        var glyphs = ReadAugmentGlyphs(spell);
        slots.Append(new WorldSpellSlot(
            index,
            _spellInstanceId!(spell),
            _recipeId!(spell),
            true,
            _isCasting!(spell),
            _isReadyingCast!(spell),
            _isAttuning!(spell),
            _isChanneled!(spell),
            _isToggled!(spell),
            _canCharge!(spell),
            _canCast!(spell),
            _isChargeAvailable!(spell),
            _canRemove!(spell),
            _hasEnoughResources!(spell),
            _currentCharges!(spell),
            _maximumCharges!(spell),
            _cooldown!(spell),
            _outputLevel!(spell),
            _effectiveLevel!(spell),
            _requiredMasteryLevel!(spell),
            _recipeMasteryLevel!(spell),
            _durationSpell!(spell),
            _usageRequirementsMet!(spell),
            glyphs,
            cancellationEnabled,
            casterAvailable,
            _castCount!(spell)));

        Append(spell, index, WorldSpellCostKind.Immediate, _getCost!, costs);
        Append(spell, index, WorldSpellCostKind.Drain, _getDrainCost!, costs);
    }

    private void Append(
        object spell,
        int index,
        WorldSpellCostKind kind,
        Func<object, object?> accessor,
        WorldSpellCostBuffer costs)
    {
        var costList = accessor(spell);
        if (costList is null) return;

        var entries = _costEntries!(costList);
        if (entries is null) return;

        for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            var entry = entries[entryIndex];
            if (entry is null) continue;

            var resourceId = _entryResource!(entry);
            if (resourceId == Guid.Empty) continue;

            costs.Append(new WorldSpellCost(index, kind, resourceId, _entryValue!(entry)));
        }
    }

    private bool IsCostBound() =>
        _getCost is not null && _getDrainCost is not null &&
        _costEntries is not null && _entryResource is not null && _entryValue is not null;

    private bool IsCompositionBound() =>
        _castCount is not null &&
        _outputLevel is not null && _effectiveLevel is not null &&
        _requiredMasteryLevel is not null && _recipeMasteryLevel is not null &&
        _durationSpell is not null && _usageRequirementsMet is not null &&
        _canRemove is not null &&
        _getAugmentGlyphs is not null && _getGlyphQuantity is not null && _glyphId is not null;

    private PublicationTable<WorldSpellSlotGlyph> ReadAugmentGlyphs(object spell)
    {
        var values = _getAugmentGlyphs!(spell) as IList;
        if (values is null || values.Count == 0)
            return PublicationTable<WorldSpellSlotGlyph>.Empty;
        var rows = new List<WorldSpellSlotGlyph>(values.Count);
        var seen = new HashSet<Guid>();
        for (var index = 0; index < values.Count; index++)
        {
            var glyph = values[index];
            if (glyph is null) continue;
            var id = _glyphId!(glyph);
            if (id == Guid.Empty || !seen.Add(id)) continue;
            var quantity = _getGlyphQuantity!(spell, glyph);
            if (quantity > 0) rows.Add(new WorldSpellSlotGlyph(id, quantity));
        }
        rows.Sort(static (left, right) => left.GlyphId.CompareTo(right.GlyphId));
        return PublicationTable<WorldSpellSlotGlyph>.Create(rows.ToArray());
    }

    private static void Skip(ref int skipped, ref string firstFailure, string reason)
    {
        skipped++;
        if (firstFailure.Length == 0) firstFailure = reason;
    }
}
