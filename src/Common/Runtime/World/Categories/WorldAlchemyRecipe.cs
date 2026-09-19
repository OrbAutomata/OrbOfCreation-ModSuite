using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>One alchemy recipe as published: what it is, how far it has been taken, and its cost inputs.</summary>
internal readonly struct WorldAlchemyRecipe : IWorldEntity
{
    internal WorldAlchemyRecipe(
        Guid recipeId,
        Guid coreTypeId,
        bool discovered,
        int maxLevel,
        int advancementLevel,
        int discoveryRarityLevel,
        BigDouble masteryXp,
        int masteryLevel,
        BigDouble recipeTime,
        bool isRequiredDiscovery,
        bool isCompletionRecipe,
        bool isAdvancementRecipe,
        double completionTime,
        bool isDebugAlchemy,
        BigDouble power,
        BigDouble speed,
        BigDouble drainCostMod,
        BigDouble special,
        BigDouble timeReqMod,
        BigDouble timeScalingMod,
        BigDouble masteryXpRate,
        BigDouble effectLevels,
        BigDouble overdrivePower,
        BigDouble overdriveSpeed,
        BigDouble overdriveDrainCostMod,
        BigDouble overdriveXpRate,
        BigDouble freeUsageSlots,
        BigDouble maxUsageSlots,
        BigDouble cachedCompletionTime,
        BigDouble requiredExperience,
        WorldDiscoverableDecision discovery = default,
        int currentLevel = 1,
        int visibilityGate = DiscoverGate)
    {
        RecipeId = recipeId;
        CoreTypeId = coreTypeId;
        Discovered = discovered;
        VisibilityGate = visibilityGate;
        MaxLevel = maxLevel;
        AdvancementLevel = advancementLevel;
        DiscoveryRarityLevel = discoveryRarityLevel;
        MasteryXp = masteryXp;
        MasteryLevel = masteryLevel;
        RecipeTime = recipeTime;
        IsRequiredDiscovery = isRequiredDiscovery;
        IsCompletionRecipe = isCompletionRecipe;
        IsAdvancementRecipe = isAdvancementRecipe;
        CompletionTime = completionTime;
        IsDebugAlchemy = isDebugAlchemy;
        Power = power;
        Speed = speed;
        DrainCostMod = drainCostMod;
        Special = special;
        TimeReqMod = timeReqMod;
        TimeScalingMod = timeScalingMod;
        MasteryXpRate = masteryXpRate;
        EffectLevels = effectLevels;
        OverdrivePower = overdrivePower;
        OverdriveSpeed = overdriveSpeed;
        OverdriveDrainCostMod = overdriveDrainCostMod;
        OverdriveXpRate = overdriveXpRate;
        FreeUsageSlots = freeUsageSlots;
        ResolvedMaxUsageSlots = maxUsageSlots;
        CachedCompletionTime = cachedCompletionTime;
        RequiredExperience = requiredExperience;
        Discovery = discovery;
        CurrentLevel = currentLevel;
    }

    internal Guid RecipeId { get; }

    public Guid EntityId => RecipeId;

    /// <summary>The exact native alchemy family this recipe belongs to.</summary>
    internal Guid CoreTypeId { get; }

    internal bool Discovered { get; }

    /// <summary>Which of the two gates the recipe screen shows this recipe on.</summary>
    /// <remarks>
    /// <para>
    /// The row the player can click is <c>UIAlchemyRecipe.IsVisible()</c>, which is
    /// <c>AlchemyRecipeSO.IsAvailable()</c>, which is
    /// <c>visibilityType == Discover ? discovered : visibilityPrerequisites.Check()</c>. So
    /// <see cref="Discovered"/> is the whole of the lock for a <c>Discover</c> recipe and none of
    /// it for a <c>Prerequisite</c> one, and which it is cannot be read off any other published
    /// fact. This is the selector, captured as the field it is: all 125 authored recipes on the
    /// pinned build read <c>Discover</c>, and capturing the selector is what makes the surface's
    /// reading right rather than coincidentally right.
    /// </para>
    /// <para>
    /// The verdict itself is deliberately not captured. <c>IsAvailable()</c> would run
    /// <c>Prerequisites.Container.Check()</c> on the other branch, which latches — a new per-pass
    /// write at the collection boundary, which the manifest forbids outright. A recipe on that
    /// branch therefore has a lock this suite cannot read, and the surface says so rather than
    /// reimplementing the check or guessing past it.
    /// </para>
    /// <para>
    /// Note that <c>visibilityPrerequisites</c> is authored and non-empty on <c>Discover</c>
    /// recipes too, where <c>IsAvailable()</c> never consults it — so the prerequisite container is
    /// not a fallback reading, it is a different question.
    /// </para>
    /// </remarks>
    internal int VisibilityGate { get; }

    /// <summary>
    /// The <c>visibilityType</c> value whose lock is <see cref="Discovered"/>, mirroring
    /// <c>AlchemyRecipeSO.VisibilityType.Discover</c>.
    /// </summary>
    internal const int DiscoverGate = 0;

    /// <summary>
    /// The highest level unlocked. The level currently selected to brew at is not here: the game
    /// writes one into its save record but never reads it back, and the runtime type has no such
    /// field — it is dead save data rather than state.
    /// </summary>
    internal int MaxLevel { get; }

    internal int AdvancementLevel { get; }

    /// <summary>The rarity tier the recipe was discovered at.</summary>
    internal int DiscoveryRarityLevel { get; }

    internal BigDouble MasteryXp { get; }

    internal int MasteryLevel { get; }

    /// <summary>
    /// The level currently selected for this recipe's core type. This is a worker-side join through
    /// <see cref="CoreTypeId"/> and the published IntVariable registry; capture makes no additional
    /// native call for it.
    /// </summary>
    internal int CurrentLevel { get; }

    /// <summary>Seconds one brew takes at the base rate.</summary>
    internal BigDouble RecipeTime { get; }

    /// <summary>
    /// The rest of what the runtime type carries: completion timing the game caches rather than
    /// persists, the flags that classify the recipe, and the fourteen records a batch is a function
    /// of. Required mastery experience is published separately through the game's maintained
    /// accessor.
    /// </summary>
    internal bool IsRequiredDiscovery { get; }

    internal bool IsCompletionRecipe { get; }

    internal bool IsAdvancementRecipe { get; }

    internal double CompletionTime { get; }

    internal bool IsDebugAlchemy { get; }

    internal BigDouble Power { get; }

    internal BigDouble Speed { get; }

    internal BigDouble DrainCostMod { get; }

    internal BigDouble Special { get; }

    internal BigDouble TimeReqMod { get; }

    internal BigDouble TimeScalingMod { get; }

    internal BigDouble MasteryXpRate { get; }

    internal BigDouble EffectLevels { get; }

    internal BigDouble OverdrivePower { get; }

    internal BigDouble OverdriveSpeed { get; }

    internal BigDouble OverdriveDrainCostMod { get; }

    internal BigDouble OverdriveXpRate { get; }

    internal BigDouble FreeUsageSlots { get; }

    /// <summary>
    /// The native resolved quantity limit. This is deliberately <c>GetMaxUsageSlots()</c>, not the
    /// raw modifier record: the raw <c>-1</c> sentinel means mastery-derived or unlimited.
    /// </summary>
    internal BigDouble ResolvedMaxUsageSlots { get; }

    internal BigDouble CachedCompletionTime { get; }

    /// <summary>
    /// The game's maintained mastery threshold, read through <c>GetRequiredExperience()</c>. The
    /// accessor delegates to the recipe's nested <c>ExperienceContainer</c>; it does not read the
    /// orphan <c>AlchemyRecipeSO.cachedRequiredXp</c> field.
    /// </summary>
    internal BigDouble RequiredExperience { get; }

    internal WorldDiscoverableDecision Discovery { get; }
}

internal static class WorldAlchemyRecipeDeriver
{
    internal static PublicationTable<WorldAlchemyRecipe> Build(
        WorldSampleBuffer<WorldAlchemyRecipe, WorldAlchemyRecipe> buffer,
        PublicationTable<WorldAlchemyType> types,
        PublicationTable<WorldNumberVariable> intVariables)
    {
        return buffer.Build(new Deriver(types, intVariables));
    }

    private sealed class Deriver : WorldRowDeriver<WorldAlchemyRecipe, WorldAlchemyRecipe>
    {
        private readonly PublicationTable<WorldAlchemyType> _types;
        private readonly PublicationTable<WorldNumberVariable> _intVariables;

        internal Deriver(
            PublicationTable<WorldAlchemyType> types,
            PublicationTable<WorldNumberVariable> intVariables)
        {
            _types = types;
            _intVariables = intVariables;
        }

        internal override WorldAlchemyRecipe Derive(in WorldAlchemyRecipe sample)
        {
            var level = 1;
            if (WorldLookup.TryFind(_types, sample.CoreTypeId, out var type) &&
                type.SelectedLevelId != Guid.Empty &&
                WorldLookup.TryFind(_intVariables, type.SelectedLevelId, out var selected))
            {
                level = selected.Value.ToInt();
            }

            return new WorldAlchemyRecipe(
                sample.RecipeId, sample.CoreTypeId, sample.Discovered, sample.MaxLevel,
                sample.AdvancementLevel, sample.DiscoveryRarityLevel, sample.MasteryXp,
                sample.MasteryLevel, sample.RecipeTime, sample.IsRequiredDiscovery,
                sample.IsCompletionRecipe, sample.IsAdvancementRecipe, sample.CompletionTime,
                sample.IsDebugAlchemy, sample.Power, sample.Speed, sample.DrainCostMod,
                sample.Special, sample.TimeReqMod, sample.TimeScalingMod, sample.MasteryXpRate,
                sample.EffectLevels, sample.OverdrivePower, sample.OverdriveSpeed,
                sample.OverdriveDrainCostMod, sample.OverdriveXpRate, sample.FreeUsageSlots,
                sample.ResolvedMaxUsageSlots, sample.CachedCompletionTime,
                sample.RequiredExperience, sample.Discovery, level, sample.VisibilityGate);
        }
    }
}

internal sealed class WorldAlchemyRecipeBinder : WorldPlainBinder<WorldAlchemyRecipe>
{
    private Func<object, Guid>? _id;
    private Func<object, Guid>? _coreTypeId;
    private Func<object, bool>? _discovered;
    private Func<object, int>? _visibilityGate;
    private Func<object, int>? _maxLevel;
    private Func<object, int>? _advancementLevel;
    private Func<object, int>? _discRarityLevel;
    private Func<object, BigDouble>? _masteryXp;
    private Func<object, int>? _masteryLevel;
    private Func<object, BigDouble>? _recipeTime;
    private Func<object, bool>? _isRequiredDiscovery;
    private Func<object, bool>? _isCompletionRecipe;
    private Func<object, bool>? _isAdvancementRecipe;
    private Func<object, double>? _completionTime;
    private Func<object, bool>? _isDebugAlchemy;
    private Func<object, BigDouble>? _power;
    private Func<object, BigDouble>? _speed;
    private Func<object, BigDouble>? _drainCostMod;
    private Func<object, BigDouble>? _special;
    private Func<object, BigDouble>? _timeReqMod;
    private Func<object, BigDouble>? _timeScalingMod;
    private Func<object, BigDouble>? _masteryXpRate;
    private Func<object, BigDouble>? _effectLevels;
    private Func<object, BigDouble>? _overdrivePower;
    private Func<object, BigDouble>? _overdriveSpeed;
    private Func<object, BigDouble>? _overdriveDrainCostMod;
    private Func<object, BigDouble>? _overdriveXpRate;
    private Func<object, BigDouble>? _freeUsageSlots;
    private Func<object, int>? _maxUsageSlots;
    private Func<object, BigDouble>? _cachedCompletionTime;
    private Func<object, BigDouble>? _requiredExperience;
    private WorldDiscoverableBinding? _discovery;

    internal override string Category => "alchemy recipes";

    internal override string TypeName => "AlchemyRecipeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _coreTypeId = bind.CallReferenceGuid("GetCoreType");
        _discovered = bind.Field<bool>("discovered");

        // Which gate the row the player clicks is behind. IsAvailable() is the verdict, but asking
        // for it would run Prerequisites.Container.Check() on the other branch, and that latches;
        // the selector is a stored field and answers with nothing written.
        _visibilityGate = bind.EnumField("visibilityType");
        _maxLevel = bind.Field<int>("maxLevel");
        _advancementLevel = bind.Field<int>("advancementLevel");
        _discRarityLevel = bind.Field<int>("discRarityLevel");
        _masteryXp = bind.Field<BigDouble>("masteryXp");
        _masteryLevel = bind.Field<int>("masteryLevel");
        _recipeTime = bind.Field<BigDouble>("recipeTime");
        _isRequiredDiscovery = bind.Field<bool>("isRequiredDiscovery");
        _isCompletionRecipe = bind.Field<bool>("isCompletionRecipe");
        _isAdvancementRecipe = bind.Field<bool>("isAdvancementRecipe");
        _completionTime = bind.Field<double>("completionTime");
        _isDebugAlchemy = bind.Field<bool>("isDebugAlchemy");
        _power = bind.ModifierRecord("power");
        _speed = bind.ModifierRecord("speed");
        _drainCostMod = bind.ModifierRecord("drainCostMod");
        _special = bind.ModifierRecord("special");
        _timeReqMod = bind.ModifierRecord("timeReqMod");
        _timeScalingMod = bind.ModifierRecord("timeScalingMod");
        _masteryXpRate = bind.ModifierRecord("masteryXpRate");
        _effectLevels = bind.ModifierRecord("effectLevels");
        _overdrivePower = bind.ModifierRecord("overdrivePower");
        _overdriveSpeed = bind.ModifierRecord("overdriveSpeed");
        _overdriveDrainCostMod = bind.ModifierRecord("overdriveDrainCostMod");
        _overdriveXpRate = bind.ModifierRecord("overdriveXpRate");
        _freeUsageSlots = bind.ModifierRecord("freeUsageSlots");
        _maxUsageSlots = bind.Call<int>("GetMaxUsageSlots");
        _cachedCompletionTime = bind.Field<BigDouble>("cachedCompletionTime");
        _requiredExperience = bind.Call<BigDouble>("GetRequiredExperience");
        _discovery = new WorldDiscoverableBinding(type, TypeName);
        return Join(bind.Failure, _discovery.Failure);
    }

    internal override WorldAlchemyRecipe Read(object entity) =>
        new(
            _id!(entity),
            _coreTypeId!(entity),
            _discovered!(entity),
            _maxLevel!(entity),
            _advancementLevel!(entity),
            _discRarityLevel!(entity),
            _masteryXp!(entity),
            _masteryLevel!(entity),
            _recipeTime!(entity),
            _isRequiredDiscovery!(entity),
            _isCompletionRecipe!(entity),
            _isAdvancementRecipe!(entity),
            _completionTime!(entity),
            _isDebugAlchemy!(entity),
            _power!(entity),
            _speed!(entity),
            _drainCostMod!(entity),
            _special!(entity),
            _timeReqMod!(entity),
            _timeScalingMod!(entity),
            _masteryXpRate!(entity),
            _effectLevels!(entity),
            _overdrivePower!(entity),
            _overdriveSpeed!(entity),
            _overdriveDrainCostMod!(entity),
            _overdriveXpRate!(entity),
            _freeUsageSlots!(entity),
            new BigDouble(_maxUsageSlots!(entity)),
            _cachedCompletionTime!(entity),
            _requiredExperience!(entity),
            _discovery!.Read(entity),
            currentLevel: 1,
            visibilityGate: _visibilityGate!(entity));

    private static string Join(string left, string right) =>
        left.Length == 0 ? right : right.Length == 0 ? left : left + "; " + right;
}
