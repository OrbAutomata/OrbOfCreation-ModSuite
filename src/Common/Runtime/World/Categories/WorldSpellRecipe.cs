using System;
using System.Collections;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal readonly struct RawSpellRecipeSample : IWorldEntity
{
    internal RawSpellRecipeSample(
        Guid spellRecipeId,
        bool discovered,
        int discRarityLevel,
        BigDouble masteryXp,
        int masteryLevel,
        bool masteryLevelReady,
        bool hiddenDiscovery,
        bool isRequiredDiscovery,
        int penaltyUsageCost,
        double castSpeed,
        int baseCharges,
        bool repeatInstantEffects,
        BigDouble spellPowerMod,
        BigDouble spellCostMod,
        BigDouble spellCdSpeedMod,
        BigDouble spellDurationMod,
        BigDouble spellSpecialMod,
        BigDouble spellXpMod,
        bool hasAlertedThisMastery,
        BigDouble requiredMasteryXp = default,
        bool requiredMasteryXpCaptured = false)
        : this(
            spellRecipeId, discovered, discRarityLevel, masteryXp, masteryLevel,
            masteryLevelReady, hiddenDiscovery, isRequiredDiscovery, penaltyUsageCost,
            castSpeed, baseCharges, repeatInstantEffects, spellPowerMod, spellCostMod,
            spellCdSpeedMod, spellDurationMod, spellSpecialMod, spellXpMod,
            hasAlertedThisMastery, PublicationTable<WorldSpellRecipeGlyph>.Empty,
            PublicationTable<WorldDiscoverableCost>.Empty, false, default,
            requiredMasteryXp, requiredMasteryXpCaptured)
    {
    }

    internal RawSpellRecipeSample(
        Guid spellRecipeId,
        bool discovered,
        int discRarityLevel,
        BigDouble masteryXp,
        int masteryLevel,
        bool masteryLevelReady,
        bool hiddenDiscovery,
        bool isRequiredDiscovery,
        int penaltyUsageCost,
        double castSpeed,
        int baseCharges,
        bool repeatInstantEffects,
        BigDouble spellPowerMod,
        BigDouble spellCostMod,
        BigDouble spellCdSpeedMod,
        BigDouble spellDurationMod,
        BigDouble spellSpecialMod,
        BigDouble spellXpMod,
        bool hasAlertedThisMastery,
        PublicationTable<WorldSpellRecipeGlyph> coreGlyphs,
        PublicationTable<WorldDiscoverableCost> discoveryCosts,
        bool discoveryAffordable,
        WorldDiscoverableDecision discovery,
        BigDouble requiredMasteryXp = default,
        bool requiredMasteryXpCaptured = false)
    {
        SpellRecipeId = spellRecipeId;
        Discovered = discovered;
        DiscRarityLevel = discRarityLevel;
        MasteryXp = masteryXp;
        MasteryLevel = masteryLevel;
        MasteryLevelReady = masteryLevelReady;
        HiddenDiscovery = hiddenDiscovery;
        IsRequiredDiscovery = isRequiredDiscovery;
        PenaltyUsageCost = penaltyUsageCost;
        CastSpeed = castSpeed;
        BaseCharges = baseCharges;
        RepeatInstantEffects = repeatInstantEffects;
        SpellPowerMod = spellPowerMod;
        SpellCostMod = spellCostMod;
        SpellCdSpeedMod = spellCdSpeedMod;
        SpellDurationMod = spellDurationMod;
        SpellSpecialMod = spellSpecialMod;
        SpellXpMod = spellXpMod;
        HasAlertedThisMastery = hasAlertedThisMastery;
        RequiredMasteryXp = requiredMasteryXp;
        RequiredMasteryXpCaptured = requiredMasteryXpCaptured;
        CoreGlyphs = coreGlyphs ?? throw new ArgumentNullException(nameof(coreGlyphs));
        DiscoveryCosts = discoveryCosts ?? throw new ArgumentNullException(nameof(discoveryCosts));
        DiscoveryAffordable = discoveryAffordable;
        Discovery = discovery;
    }

    public Guid EntityId => SpellRecipeId;
    internal Guid SpellRecipeId { get; }
    internal bool Discovered { get; }
    internal int DiscRarityLevel { get; }
    internal BigDouble MasteryXp { get; }

    internal int MasteryLevel { get; }
    internal bool MasteryLevelReady { get; }
    internal BigDouble RequiredMasteryXp { get; }
    internal bool RequiredMasteryXpCaptured { get; }
    internal bool HiddenDiscovery { get; }
    internal bool IsRequiredDiscovery { get; }
    internal int PenaltyUsageCost { get; }
    internal double CastSpeed { get; }
    internal int BaseCharges { get; }
    internal bool RepeatInstantEffects { get; }
    internal BigDouble SpellPowerMod { get; }
    internal BigDouble SpellCostMod { get; }
    internal BigDouble SpellCdSpeedMod { get; }
    internal BigDouble SpellDurationMod { get; }
    internal BigDouble SpellSpecialMod { get; }
    internal BigDouble SpellXpMod { get; }
    internal bool HasAlertedThisMastery { get; }
    internal PublicationTable<WorldSpellRecipeGlyph> CoreGlyphs { get; }
    internal PublicationTable<WorldDiscoverableCost> DiscoveryCosts { get; }
    internal bool DiscoveryAffordable { get; }
    internal WorldDiscoverableDecision Discovery { get; }
}

/// <summary>One spell recipe as published: discovery, its mastery track, and the six cached records that say what a cast of it is worth.</summary>
internal readonly struct WorldSpellRecipe : IWorldEntity
{
    internal WorldSpellRecipe(
        Guid spellRecipeId,
        bool discovered,
        int discRarityLevel,
        BigDouble masteryXp,
        int masteryLevel,
        bool masteryLevelReady,
        bool hiddenDiscovery,
        bool isRequiredDiscovery,
        int penaltyUsageCost,
        double castSpeed,
        int baseCharges,
        bool repeatInstantEffects,
        BigDouble spellPowerMod,
        BigDouble spellCostMod,
        BigDouble spellCdSpeedMod,
        BigDouble spellDurationMod,
        BigDouble spellSpecialMod,
        BigDouble spellXpMod,
        bool hasAlertedThisMastery,
        PublicationTable<WorldSpellRecipeGlyph>? coreGlyphs = null,
        PublicationTable<WorldDiscoverableCost>? discoveryCosts = null,
        bool discoveryAffordable = false,
        WorldDiscoverableDecision discovery = default)
        : this(
            spellRecipeId, discovered, discRarityLevel, masteryXp, masteryLevel,
            masteryLevelReady, false, hiddenDiscovery,
            isRequiredDiscovery, penaltyUsageCost, castSpeed, baseCharges,
            repeatInstantEffects, spellPowerMod, spellCostMod, spellCdSpeedMod,
            spellDurationMod, spellSpecialMod, spellXpMod, hasAlertedThisMastery,
            default, coreGlyphs, discoveryCosts, discoveryAffordable,
            discovery)
    {
    }

    internal WorldSpellRecipe(
        Guid spellRecipeId,
        bool discovered,
        int discRarityLevel,
        BigDouble masteryXp,
        int masteryLevel,
        bool masteryLevelReady,
        bool masteryLevelAffordable,
        bool hiddenDiscovery,
        bool isRequiredDiscovery,
        int penaltyUsageCost,
        double castSpeed,
        int baseCharges,
        bool repeatInstantEffects,
        BigDouble spellPowerMod,
        BigDouble spellCostMod,
        BigDouble spellCdSpeedMod,
        BigDouble spellDurationMod,
        BigDouble spellSpecialMod,
        BigDouble spellXpMod,
        bool hasAlertedThisMastery,
        BigDouble requiredMasteryXp = default,
        PublicationTable<WorldSpellRecipeGlyph>? coreGlyphs = null,
        PublicationTable<WorldDiscoverableCost>? discoveryCosts = null,
        bool discoveryAffordable = false,
        WorldDiscoverableDecision discovery = default)
        : this(
            spellRecipeId, discovered, discRarityLevel, masteryXp, masteryLevel,
            masteryLevelReady, masteryLevelAffordable, 0, Guid.Empty, hiddenDiscovery,
            isRequiredDiscovery, penaltyUsageCost, castSpeed, baseCharges, repeatInstantEffects,
            spellPowerMod, spellCostMod, spellCdSpeedMod, spellDurationMod, spellSpecialMod,
            spellXpMod, hasAlertedThisMastery, requiredMasteryXp,
            coreGlyphs, discoveryCosts, discoveryAffordable, discovery)
    {
    }

    internal WorldSpellRecipe(
        Guid spellRecipeId,
        bool discovered,
        int discRarityLevel,
        BigDouble masteryXp,
        int masteryLevel,
        bool masteryLevelReady,
        bool masteryLevelAffordable,
        int masteryLevelCostCount,
        Guid masteryLevelBindingResourceId,
        bool hiddenDiscovery,
        bool isRequiredDiscovery,
        int penaltyUsageCost,
        double castSpeed,
        int baseCharges,
        bool repeatInstantEffects,
        BigDouble spellPowerMod,
        BigDouble spellCostMod,
        BigDouble spellCdSpeedMod,
        BigDouble spellDurationMod,
        BigDouble spellSpecialMod,
        BigDouble spellXpMod,
        bool hasAlertedThisMastery,
        BigDouble requiredMasteryXp = default,
        PublicationTable<WorldSpellRecipeGlyph>? coreGlyphs = null,
        PublicationTable<WorldDiscoverableCost>? discoveryCosts = null,
        bool discoveryAffordable = false,
        WorldDiscoverableDecision discovery = default)
    {
        SpellRecipeId = spellRecipeId;
        Discovered = discovered;
        DiscRarityLevel = discRarityLevel;
        MasteryXp = masteryXp;
        MasteryLevel = masteryLevel;
        MasteryLevelReady = masteryLevelReady;
        MasteryLevelAffordable = masteryLevelAffordable;
        MasteryLevelCostCount = masteryLevelCostCount;
        MasteryLevelBindingResourceId = masteryLevelBindingResourceId;
        HiddenDiscovery = hiddenDiscovery;
        IsRequiredDiscovery = isRequiredDiscovery;
        PenaltyUsageCost = penaltyUsageCost;
        CastSpeed = castSpeed;
        BaseCharges = baseCharges;
        RepeatInstantEffects = repeatInstantEffects;
        SpellPowerMod = spellPowerMod;
        SpellCostMod = spellCostMod;
        SpellCdSpeedMod = spellCdSpeedMod;
        SpellDurationMod = spellDurationMod;
        SpellSpecialMod = spellSpecialMod;
        SpellXpMod = spellXpMod;
        HasAlertedThisMastery = hasAlertedThisMastery;
        RequiredMasteryXp = requiredMasteryXp;
        CoreGlyphs = coreGlyphs ?? PublicationTable<WorldSpellRecipeGlyph>.Empty;
        DiscoveryCosts = discoveryCosts ?? PublicationTable<WorldDiscoverableCost>.Empty;
        DiscoveryAffordable = discoveryAffordable;
        Discovery = discovery;
    }

    internal Guid SpellRecipeId { get; }

    public Guid EntityId => SpellRecipeId;

    internal bool Discovered { get; }

    internal int DiscRarityLevel { get; }

    internal BigDouble MasteryXp { get; }

    /// <summary>The cached threshold the native readiness check compares mastery XP against.</summary>
    internal BigDouble RequiredMasteryXp { get; }

    internal int MasteryLevel { get; }

    /// <summary>
    /// Whether the mastery track has banked enough experience for the next level to be bought.
    /// </summary>
    /// <remarks>
    /// Derived off-thread by comparing <see cref="MasteryXp"/> with
    /// <see cref="RequiredMasteryXp"/>; normal capture does not call
    /// <c>IsReadyToLevelMastery()</c>.
    /// </remarks>
    internal bool MasteryLevelReady { get; }

    /// <summary>
    /// Whether the game's current level-cost list says the next mastery level is affordable.
    /// </summary>
    /// <remarks>
    /// This is planning evidence, not mutation authority. The action boundary asks the same native
    /// cost again immediately before spending because resources may move after publication.
    /// </remarks>
    internal bool MasteryLevelAffordable { get; }

    /// <summary>The number of independently evaluated rows in the derived next-level cost vector.</summary>
    internal int MasteryLevelCostCount { get; }

    /// <summary>The first unaffordable row's resource, or empty when every row is affordable.</summary>
    internal Guid MasteryLevelBindingResourceId { get; }

    internal bool HiddenDiscovery { get; }

    internal bool IsRequiredDiscovery { get; }

    internal int PenaltyUsageCost { get; }

    internal double CastSpeed { get; }

    internal int BaseCharges { get; }

    internal bool RepeatInstantEffects { get; }

    internal BigDouble SpellPowerMod { get; }

    internal BigDouble SpellCostMod { get; }

    internal BigDouble SpellCdSpeedMod { get; }

    internal BigDouble SpellDurationMod { get; }

    internal BigDouble SpellSpecialMod { get; }

    internal BigDouble SpellXpMod { get; }

    internal bool HasAlertedThisMastery { get; }
    internal PublicationTable<WorldSpellRecipeGlyph> CoreGlyphs { get; }
    internal PublicationTable<WorldDiscoverableCost> DiscoveryCosts { get; }
    internal bool DiscoveryAffordable { get; }
    internal WorldDiscoverableDecision Discovery { get; }
}

internal readonly struct WorldSpellRecipeGlyph
{
    internal WorldSpellRecipeGlyph(int position, Guid glyphId)
    {
        Position = position;
        GlyphId = glyphId;
    }

    internal int Position { get; }
    internal Guid GlyphId { get; }
}

internal sealed class WorldSpellRecipeBinder : WorldRowBinder<RawSpellRecipeSample, WorldSpellRecipe>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _discovered;
    private Func<object, int>? _discRarityLevel;
    private Func<object, BigDouble>? _masteryXp;
    private Func<object, int>? _masteryLevel;
    private Func<object, BigDouble>? _requiredMasteryXp;
    private Func<object, bool>? _hiddenDiscovery;
    private Func<object, bool>? _isRequiredDiscovery;
    private Func<object, int>? _penaltyUsageCost;
    private Func<object, double>? _castSpeed;
    private Func<object, int>? _baseCharges;
    private Func<object, bool>? _repeatInstantEffects;
    private Func<object, BigDouble>? _spellPowerMod;
    private Func<object, BigDouble>? _spellCostMod;
    private Func<object, BigDouble>? _spellCdSpeedMod;
    private Func<object, BigDouble>? _spellDurationMod;
    private Func<object, BigDouble>? _spellSpecialMod;
    private Func<object, BigDouble>? _spellXpMod;
    private Func<object, bool>? _hasAlertedThisMastery;
    private Func<object, IList?>? _coreGlyphs;
    private Func<object, Guid>? _glyphId;
    private WorldDiscoverableBinding? _discovery;

    internal override string Category => "spell recipes";

    internal override string TypeName => "SpellRecipeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _discovered = bind.Field<bool>("discovered");
        _discRarityLevel = bind.Field<int>("discRarityLevel");
        _masteryXp = bind.Field<BigDouble>("masteryExperience");
        _masteryLevel = bind.Field<int>("masteryLevel");
        _requiredMasteryXp = bind.NestedField<BigDouble>("masteryXpContainer", "cachedRequiredXp");
        _hiddenDiscovery = bind.Field<bool>("hiddenDiscovery");
        _isRequiredDiscovery = bind.Field<bool>("isRequiredDiscovery");
        _penaltyUsageCost = bind.Field<int>("penaltyUsageCost");
        _castSpeed = bind.Field<double>("castSpeed");
        _baseCharges = bind.Field<int>("baseCharges");
        _repeatInstantEffects = bind.Field<bool>("repeatInstantEffects");
        _spellPowerMod = bind.ModifierRecord("spellPowerMod");
        _spellCostMod = bind.ModifierRecord("spellCostMod");
        _spellCdSpeedMod = bind.ModifierRecord("spellCdSpeedMod");
        _spellDurationMod = bind.ModifierRecord("spellDurationMod");
        _spellSpecialMod = bind.ModifierRecord("spellSpecialMod");
        _spellXpMod = bind.ModifierRecord("spellXpMod");
        _hasAlertedThisMastery = bind.Field<bool>("hasAlertedThisMastery");
        const System.Reflection.BindingFlags instance =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;
        var glyphMethod = type.GetMethod("GetGlyphRecipe", instance, null, Type.EmptyTypes, null);
        var glyphType = glyphMethod?.ReturnType is { IsGenericType: true } glyphList
            ? glyphList.GetGenericArguments()[0]
            : null;
        _coreGlyphs = bind.CallList("GetGlyphRecipe", glyphType);
        _glyphId = NativeAccessorBinder.Call<Guid>(glyphType, "GetGuid");
        _discovery = new WorldDiscoverableBinding(type, TypeName);
        if (_glyphId is null || _discovery.Failure.Length != 0)
        {
            var failure = bind.Failure.Length == 0 ? _discovery.Failure : bind.Failure;
            return failure.Length == 0
                ? "SpellRecipeSO decision members did not expose their complete nested identity and cost shape on this build"
                : failure;
        }
        return bind.Failure;
    }

    internal override RawSpellRecipeSample Read(object entity)
    {
        var glyphValues = _coreGlyphs!(entity);
        var glyphs = new WorldSpellRecipeGlyph[glyphValues?.Count ?? 0];
        for (var index = 0; index < glyphs.Length; index++)
        {
            var glyph = glyphValues![index];
            glyphs[index] = new WorldSpellRecipeGlyph(
                index,
                glyph is null ? Guid.Empty : _glyphId!(glyph));
        }
        var discovery = _discovery!.Read(entity);
        return new RawSpellRecipeSample(
            _id!(entity),
            _discovered!(entity),
            _discRarityLevel!(entity),
            _masteryXp!(entity),
            _masteryLevel!(entity),
            false,
            _hiddenDiscovery!(entity),
            _isRequiredDiscovery!(entity),
            _penaltyUsageCost!(entity),
            _castSpeed!(entity),
            _baseCharges!(entity),
            _repeatInstantEffects!(entity),
            _spellPowerMod!(entity),
            _spellCostMod!(entity),
            _spellCdSpeedMod!(entity),
            _spellDurationMod!(entity),
            _spellSpecialMod!(entity),
            _spellXpMod!(entity),
            _hasAlertedThisMastery!(entity),
            PublicationTable<WorldSpellRecipeGlyph>.Create(glyphs),
            discovery.Costs,
            discovery.Affordable,
            discovery,
            _requiredMasteryXp!(entity),
            requiredMasteryXpCaptured: true);
    }
}

internal sealed class WorldSpellRecipeDeriver : WorldRowDeriver<RawSpellRecipeSample, WorldSpellRecipe>
{
    private readonly PublicationTable<WorldMasteryCost> _costs;

    internal WorldSpellRecipeDeriver(PublicationTable<WorldMasteryCost> costs) => _costs = costs;

    internal override WorldSpellRecipe Derive(in RawSpellRecipeSample sample)
    {
        var affordable = true;
        var binding = Guid.Empty;
        var count = 0;
        if (OwnedMasteryCostMath.TryFindRange(_costs, sample.SpellRecipeId, out var start, out count))
        {
            for (var index = 0; index < count; index++)
            {
                var cost = _costs[start + index];
                if (cost.Affordable) continue;
                affordable = false;
                binding = cost.ResourceId;
                break;
            }
        }

        return new WorldSpellRecipe(
            sample.SpellRecipeId,
            sample.Discovered,
            sample.DiscRarityLevel,
            sample.MasteryXp,
            sample.MasteryLevel,
            sample.RequiredMasteryXpCaptured
                ? sample.MasteryXp >= sample.RequiredMasteryXp
                : sample.MasteryLevelReady,
            affordable,
            count,
            binding,
            sample.HiddenDiscovery,
            sample.IsRequiredDiscovery,
            sample.PenaltyUsageCost,
            sample.CastSpeed,
            sample.BaseCharges,
            sample.RepeatInstantEffects,
            sample.SpellPowerMod,
            sample.SpellCostMod,
            sample.SpellCdSpeedMod,
            sample.SpellDurationMod,
            sample.SpellSpecialMod,
            sample.SpellXpMod,
            sample.HasAlertedThisMastery,
            sample.RequiredMasteryXp,
            sample.CoreGlyphs,
            sample.DiscoveryCosts,
            sample.DiscoveryAffordable,
            sample.Discovery);
    }
}
