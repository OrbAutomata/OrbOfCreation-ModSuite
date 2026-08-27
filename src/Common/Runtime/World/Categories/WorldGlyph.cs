using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One Augment Glyph as published — the twenty-two the game socket into spells and upgrades on
/// Magic &gt; Augments.
/// </summary>
/// <remarks>
/// <c>GlyphSO</c> also backs twenty-five assets that are the internal half of a Recipe Book; those
/// are not published here (see <see cref="WorldGlyphBinder.Publishes"/>) and the Recipe Book they
/// open is what a caller reads instead.
/// </remarks>
internal readonly struct WorldGlyph : IWorldEntity
{
    internal WorldGlyph(Guid glyphId, int level, int freeLevels, int discoveryRarityLevel, bool learned,
        bool discoverable,
        bool discoveryRequired,
        bool augmentsSpells,
        bool requiresDuration,
        bool requiresToggleable,
        int masteryReqCount,
        BigDouble freeUsages,
        BigDouble freeLoadoutUsages,
        BigDouble maxUsages,
        int maximumUsages = 0,
        int maximumFreeUsages = 0,
        WorldDiscoverableDecision discovery = default,
        WorldLevelableDecision levelDecision = default)
    {
        GlyphId = glyphId;
        Level = level;
        FreeLevels = freeLevels;
        DiscoveryRarityLevel = discoveryRarityLevel;
        Learned = learned;
        Discoverable = discoverable;
        DiscoveryRequired = discoveryRequired;
        AugmentsSpells = augmentsSpells;
        RequiresDuration = requiresDuration;
        RequiresToggleable = requiresToggleable;
        MasteryReqCount = masteryReqCount;
        FreeUsages = freeUsages;
        FreeLoadoutUsages = freeLoadoutUsages;
        MaxUsages = maxUsages;
        MaximumUsages = maximumUsages;
        MaximumFreeUsages = maximumFreeUsages;
        Discovery = discovery;
        LevelDecision = levelDecision;
    }

    internal Guid GlyphId { get; }

    public Guid EntityId => GlyphId;

    internal int Level { get; }

    /// <summary>Levels granted rather than bought.</summary>
    internal int FreeLevels { get; }

    internal int DiscoveryRarityLevel { get; }

    /// <summary>
    /// The native glyph picker's learned/visible fact. For discoverable glyphs this is the
    /// discovery flag; for pool unlockers it is the authored prerequisite edge (for example,
    /// Learn Psionic). The raw <c>GlyphSO.discovered</c> field is not ownership for both systems.
    /// </summary>
    internal bool Learned { get; }

    /// <summary>The rest of the glyph's runtime state: what it may be applied to, and its usage grants.</summary>
    internal bool Discoverable { get; }

    internal bool DiscoveryRequired { get; }

    internal bool AugmentsSpells { get; }

    internal bool RequiresDuration { get; }

    internal bool RequiresToggleable { get; }

    internal int MasteryReqCount { get; }

    internal BigDouble FreeUsages { get; }

    internal BigDouble FreeLoadoutUsages { get; }

    internal BigDouble MaxUsages { get; }

    /// <summary>
    /// How many copies of this glyph fit one spell, after active modifiers — the game's own
    /// <c>GetMaxUsages()</c>, which its level panel prints as <c>[N] Slot</c>.
    /// </summary>
    internal int MaximumUsages { get; }

    /// <summary>
    /// How many of those slots cost no spell usage, after active modifiers — the game's own
    /// <c>GetFreeUsages()</c>, printed beside the slots as <c>[M] Free Slot</c>. Its own number,
    /// not a share of <see cref="MaximumUsages"/>: a level grants one every sixth level.
    /// </summary>
    internal int MaximumFreeUsages { get; }

    internal WorldDiscoverableDecision Discovery { get; }

    /// <summary>The native <c>IDiscoverable.IsDiscovered()</c> fact, distinct from picker availability.</summary>
    internal bool Discovered => Discovery.Discovered;

    internal WorldLevelableDecision LevelDecision { get; }
}

internal sealed class WorldGlyphBinder : WorldPlainBinder<WorldGlyph>
{
    private readonly Func<string, Type?> _resolveType;
    private Func<object, Guid>? _id;
    private Func<object, int>? _level;
    private Func<object, int>? _freeLevels;
    private Func<object, int>? _discRarityLevel;
    private Func<object, bool>? _discoverable;
    private Func<object, bool>? _discoveryRequired;
    private Func<object, bool>? _augmentsSpells;
    private Func<object, bool>? _requiresDuration;
    private Func<object, bool>? _requiresToggleable;
    private Func<object, int>? _masteryReqCount;
    private Func<object, BigDouble>? _freeUsages;
    private Func<object, BigDouble>? _freeLoadoutUsages;
    private Func<object, BigDouble>? _maxUsages;
    private Func<object, bool>? _available;
    private Func<object, int>? _maximumUsages;
    private Func<object, int>? _maximumFreeUsages;
    private Func<object, Guid>? _associatedRecipeBook;
    private WorldDiscoverableBinding? _discovery;
    private WorldLevelableDecisionBinding? _levelDecision;

    internal WorldGlyphBinder(Func<string, Type?> resolveType) =>
        _resolveType = resolveType ?? throw new ArgumentNullException(nameof(resolveType));

    internal override string Category => "augment glyphs";

    internal override string TypeName => "GlyphSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _level = bind.Field<int>("level");
        _freeLevels = bind.Field<int>("freeLevels");
        _discRarityLevel = bind.Field<int>("discRarityLevel");
        _discoverable = bind.Field<bool>("discoverable");
        _discoveryRequired = bind.Field<bool>("discoveryRequired");
        _augmentsSpells = bind.Field<bool>("augmentsSpells");
        _requiresDuration = bind.Field<bool>("requiresDuration");
        _requiresToggleable = bind.Field<bool>("requiresToggleable");
        _masteryReqCount = bind.Field<int>("masteryReqCount");
        _freeUsages = bind.ModifierRecord("freeUsages");
        _freeLoadoutUsages = bind.ModifierRecord("freeLoadoutUsages");
        _maxUsages = bind.ModifierRecord("maxUsages");
        _available = bind.Call<bool>("IsAvailable");
        _maximumUsages = bind.Call<int>("GetMaxUsages");
        _maximumFreeUsages = bind.Call<int>("GetFreeUsages");
        _associatedRecipeBook = bind.ReferenceGuid("associatedRecipeBook");
        _discovery = new WorldDiscoverableBinding(type, TypeName);
        _levelDecision = new WorldLevelableDecisionBinding(type, true, _resolveType);
        return Join(bind.Failure, _discovery.Failure, _levelDecision.Failure);
    }

    /// <summary>
    /// An Augment Glyph is a <c>GlyphSO</c> with no <c>associatedRecipeBook</c>. The twenty-five
    /// that carry one are the internal half of the Recipe Book they name — the game draws them no
    /// grid, prices them no level and gives them no button — so the book is the row and they are
    /// not published at all. The authored field is read rather than inferred, and it agrees exactly
    /// with the other two discriminators the build ships: membership of <c>AugmentSpellGlyphs</c>
    /// (22) and <c>discoverable</c> (22 true).
    /// </summary>
    internal override bool Publishes(object entity) => _associatedRecipeBook!(entity) == Guid.Empty;

    internal override WorldGlyph Read(object entity) =>
        new(
            _id!(entity),
            _level!(entity),
            _freeLevels!(entity),
            _discRarityLevel!(entity),
            _available!(entity),
            _discoverable!(entity),
            _discoveryRequired!(entity),
            _augmentsSpells!(entity),
            _requiresDuration!(entity),
            _requiresToggleable!(entity),
            _masteryReqCount!(entity),
            _freeUsages!(entity),
            _freeLoadoutUsages!(entity),
            _maxUsages!(entity),
            _maximumUsages!(entity),
            _maximumFreeUsages!(entity),
            _discovery!.Read(entity),
            _levelDecision!.Read(entity));

    private static string Join(params string[] values)
    {
        var result = string.Empty;
        foreach (var value in values)
            if (value.Length > 0) result = result.Length == 0 ? value : result + "; " + value;
        return result;
    }
}
