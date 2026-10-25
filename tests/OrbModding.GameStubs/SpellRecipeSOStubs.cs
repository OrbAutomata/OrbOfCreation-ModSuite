using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class SpellRecipeSO : StringIdScriptableObject, IDiscoverable, ILevelable
{
    public List<InstantEffectBlock> onCastEffects = new List<InstantEffectBlock>();
    public List<PersistentEffectBlock> toggledEffects = new List<PersistentEffectBlock>();
#pragma warning disable CS0414 // Native private field read by the collector through a bound accessor.
    private List<SpellTypeSO> notSpellTypes = new List<SpellTypeSO>();
#pragma warning restore CS0414
    public static List<SpellRecipeSO> All = new List<SpellRecipeSO>();

    /// <summary>
    /// Fixture-facing string form of the inherited identity. The game declares <c>GetGuid()</c> on
    /// <see cref="IdScriptableObject"/>, so this surface keeps that base identity synchronized and
    /// deliberately does not redeclare the method.
    /// </summary>

    public int masteryLevel;
    public BigDouble masteryExperience;
    public ExperienceContainer masteryXpContainer = new ExperienceContainer();
    public bool discovered;
    public int discRarityLevel;
    public bool readyToLevel;
    public bool SuppressMasteryGain { get; set; }
    public bool SuppressLevelMutation { get; set; }
    public Exception? ThrowAfterLevelMutation { get; set; }
    public int ExtraLevelsPerPurchase { get; set; }
    public int MasteryGrantCalls { get; private set; }
    public List<BigDouble> GrantedMasteryExperience { get; } = new List<BigDouble>();
    public Prerequisites.Container levelingPrerequisites = new Prerequisites.Container();
    public ResourceCostList levelCost = new ResourceCostList();
    public List<GlyphSO> coreRecipe = new List<GlyphSO>();
    public ResourceCostList baseDiscoveryCost = new ResourceCostList();
    public ResourceCostList baseLevelingCost = new ResourceCostList();
    public ResourceCostList baseResourceCost = new ResourceCostList();
    public ResourceCostList baseUsageCost = new ResourceCostList();
    public ResourceCostList holdDrain = new ResourceCostList();
    public List<SpellTypeSO> spellTypes = new List<SpellTypeSO>();
    public RecipeBookList recipeBookList = new RecipeBookList();
    public Prerequisites.Container usagePrerequisites = new Prerequisites.Container();
    public SpellCastType castType;
    public Duration.Entry baseRecharge = new Duration.Entry();
    public ScalingWeightRef.Value maxChannel = new ScalingWeightRef.Value();
    public Scaling.Value repeatInstantEffectRate = new Scaling.Value();
    public bool NativeCanDiscover { get; set; } = true;
    public bool NativeDiscoverVisible { get; set; } = true;
    public bool NativeDiscoveryRequired { get; set; }
    public bool SuppressDiscovery { get; set; }
    public bool ThrowBeforeDiscovery { get; set; }
    public bool ThrowAfterDiscovery { get; set; }
    public Exception? DiscoveredReadFailure { get; set; }
    public int DiscoverCalls { get; private set; }
    public bool NativeIsCreatable { get; set; } = true;
    public bool NativeUsageRequirementsMet { get; set; } = true;
    public int NativeSelectedSpellLevel { get; set; } = 1;
    public bool NativeUniqueSpell { get; set; }
    public bool CandidateDurationSpell { get; set; }
    public bool CandidateToggledSpell { get; set; }
    public void GainMasteryExp(BigDouble exp)
    {
        MasteryGrantCalls++;
        if (SuppressMasteryGain) return;
        GrantedMasteryExperience.Add(exp);
        masteryExperience = Add(masteryExperience, exp);
    }
    public bool IsDiscovered() => DiscoveredReadFailure is { } failure ? throw failure : discovered;
    public bool CanDiscover() => NativeCanDiscover;
    public bool IsDiscoverVisible() => NativeDiscoverVisible;
    public bool IsDiscoverRequired() => NativeDiscoveryRequired;
    public bool IsCreatable() => NativeIsCreatable;
    public List<GlyphSO> GetGlyphRecipe() => new List<GlyphSO>(coreRecipe);
    public List<ResourceSO> GetResourceRecipe() => new List<ResourceSO>();
    public ResourceCostList GetDiscoverCost() => baseDiscoveryCost;

    /// <summary>
    /// The game's own: <c>baseUsageCost</c>, plus <c>penaltyUsageCost</c> on every row while the
    /// "use effectively" requirements are unmet.
    /// </summary>
    public ResourceCostList GetUsageCost()
    {
        if (HasMetUsageRequirements()) return baseUsageCost;
        var penalized = new ResourceCostList
        {
            WithinCapacity = baseUsageCost.WithinCapacity,
            affordable = baseUsageCost.affordable,
            AffordableLevels = baseUsageCost.AffordableLevels,
        };
        foreach (var row in baseUsageCost.costs)
            penalized.costs.Add(new ResourceTuple(
                row.resource, row.GetValue() + new BigDouble(penaltyUsageCost)));
        return penalized;
    }

    public Spell CreateEmpty(int _) => new Spell(this)
    {
        NativeUnique = NativeUniqueSpell,
        DurationSpell = CandidateDurationSpell,
        ToggledSpell = CandidateToggledSpell,
    };
    public int GetSelectedSpellLevel() => NativeSelectedSpellLevel;
    public bool HasMetUsageRequirements() => NativeUsageRequirementsMet;
    public void Discover()
    {
        DiscoverCalls++;
        if (ThrowBeforeDiscovery)
            throw new InvalidOperationException("injected failure before discovery");
        if (!SuppressDiscovery) discovered = true;
        SpellManager.instance?.PostDiscoverRecipe(this);
        if (ThrowAfterDiscovery)
            throw new InvalidOperationException("injected failure after discovery");
    }
    public bool IsReadyToLevelMastery() => readyToLevel;
    public ValueModifierList GetCostModPerLevel() => GlobalValues.instance.spellCostStandard.value;
    public ResourceCostList GetLevelCost() => levelCost;
    public void PurchaseLevel()
    {
        if (!readyToLevel) return;
        if (!SuppressLevelMutation)
        {
            masteryLevel += 1 + ExtraLevelsPerPurchase;
            readyToLevel = false;
        }
        if (ThrowAfterLevelMutation is { } failure) throw failure;
    }
    public string GetName() => "Spell";

    private static BigDouble Add(BigDouble left, BigDouble right) => left + right;
    public bool hiddenDiscovery;
    public bool isRequiredDiscovery;
    public int penaltyUsageCost;
    public double castSpeed;
    public int baseCharges;
    public bool repeatInstantEffects;
    public ValueModifierRecord spellPowerMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord spellCostMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord spellCdSpeedMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord spellDurationMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord spellSpecialMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord spellXpMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    private bool hasAlertedThisMastery;
}
