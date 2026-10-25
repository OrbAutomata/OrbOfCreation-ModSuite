using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class AlchemyRecipeSO : StringIdScriptableObject, IDiscoverable
{
    public List<PersistentEffectBlock> persistentEffectBlocks = new List<PersistentEffectBlock>();
    public List<PersistentEffectBlock> equipEffectBlocks = new List<PersistentEffectBlock>();
    public List<PersistentEffectBlock> advancementEffectBlocks = new List<PersistentEffectBlock>();
    public static List<AlchemyRecipeSO> All = new List<AlchemyRecipeSO>();
    private ExperienceContainer experienceContainer = new ExperienceContainer();



    public AlchemyRecipeSO() { }

    public AlchemyRecipeSO(string uuid, string name, IEnumerable<AlchemyTypeSO> types)
    {
        this.uuid = uuid;
        this.name = name;
        alchemyTypes.AddRange(types);
    }


    public new string name = "Alchemy";
    public bool discovered = true;

    /// <summary>
    /// Which gate this recipe's row is behind. <c>Discover</c> makes <c>IsAvailable()</c> read
    /// <c>discovered</c>; <c>Prerequisite</c> makes it run the visibility container instead. Every
    /// authored recipe on the pinned build is <c>Discover</c>, which is the default here too.
    /// </summary>
    public VisibilityTypeKind visibilityType = VisibilityTypeKind.Discover;

    public enum VisibilityTypeKind
    {
        Discover = 0,
        Prerequisite = 1,
    }

    public int masteryLevel;
    public BigDouble masteryXp;
    public int maxLevel = 1;
    public int advancementLevel;
    public int discRarityLevel;
    public BigDouble recipeTime;
    public readonly List<AlchemyTypeSO> alchemyTypes = new List<AlchemyTypeSO>();
    public ResourceCostList drainCost = new ResourceCostList();
    public ResourceCostList baseDiscoveryCost = new ResourceCostList();
    public ResourceCostList usageCost = new ResourceCostList();
    public ResourceCostList bandwidthCost = new ResourceCostList();
    public ModifierListRef completionCostAdvanceMod = new ModifierListRef();
    public ModifierListRef drainCostLevelMod = new ModifierListRef();
    public Prerequisites.Container usagePrerequisites = new Prerequisites.Container { NativeCheckResult = true };
    public InstanceScalingRef instanceScaling = new InstanceScalingRef();
    public List<GlyphSO> glyphRecipe = new List<GlyphSO>();
    public List<ResourceSO> resourceRecipe = new List<ResourceSO>();
    public bool NativeDiscoverVisible { get; set; } = true;
    public bool NativeCanDiscover { get; set; } = true;
    public bool NativeDiscoveryRequired { get; set; }
    public bool SuppressDiscovery { get; set; }
    public bool ThrowBeforeDiscovery { get; set; }
    public bool ThrowAfterDiscovery { get; set; }
    public int DiscoverCalls { get; private set; }
    public AlchemyTypeSO coreType = new AlchemyTypeSO("scholar-slot");
    public List<BigDouble> GrantedMasteryExperience { get; } = new List<BigDouble>();

    public new Guid GetGuid() => base.GetGuid();
    public new Guid GetId() => GetGuid();
    public new void SetGuid(Guid guid)
    {
        base.SetGuid(guid);
        uuid = guid.ToString();
    }
    public Exception? DiscoveredReadFailure { get; set; }
    public bool IsDiscovered() => DiscoveredReadFailure is { } failure ? throw failure : discovered;
    public ResourceCostList GetDiscoverCost() => baseDiscoveryCost;
    public ResourceCostList GetUsageCost() => usageCost;
    public int GetFreeUsageSlots() => freeUsageSlots.GetValue().ToInt();
    public List<GlyphSO> GetGlyphRecipe() => new List<GlyphSO>(glyphRecipe);
    public List<ResourceSO> GetResourceRecipe() => new List<ResourceSO>(resourceRecipe);
    public bool IsDiscoverVisible() => NativeDiscoverVisible;
    public bool CanDiscover() => NativeCanDiscover;
    public bool IsDiscoverRequired() => NativeDiscoveryRequired;
    public bool IsAvailable() => discovered;
    public int GetExperienceLevel() => masteryLevel;
    public BigDouble GetExperience() => masteryXp;
    public BigDouble GetRequiredExperience() => experienceContainer.GetRequiredExperience();
    public Exception? MaxUsageReadFailure { get; set; }
    public int GetMaxUsageSlots()
    {
        if (MaxUsageReadFailure is { } failure) throw failure;
        if (coreType.maxUsageByMastery) return masteryLevel + 1;
        var maximum = maxUsageSlots.GetValue();
        return maximum < BigDouble.Zero ? int.MaxValue : BigDouble.Floor(maximum).ToInt();
    }
    public AlchemyTypeSO GetCoreType() => coreType;
    public string GetName() => name;
    public void Discover()
    {
        DiscoverCalls++;
        if (ThrowBeforeDiscovery)
            throw new InvalidOperationException("injected failure before discovery");
        if (!SuppressDiscovery) discovered = true;
        if (ThrowAfterDiscovery)
            throw new InvalidOperationException("injected failure after discovery");
    }
    public void ApplyMastery() => masteryLevel++;

    public void SetRequiredMasteryExperience(BigDouble perLevel) =>
        experienceContainer.SetState(masteryLevel, masteryXp, perLevel);

    public void GainMasteryXp(BigDouble amount)
    {
        GrantedMasteryExperience.Add(amount);
        masteryXp = Add(masteryXp, amount);
        var required = experienceContainer.GetRequiredExperience();
        while (masteryXp.CompareTo(required) >= 0)
        {
            masteryXp -= required;
            masteryLevel++;
        }
    }

    private static BigDouble Add(BigDouble left, BigDouble right) => left + right;
    public bool isRequiredDiscovery;
    public bool isCompletionRecipe;
    public bool isAdvancementRecipe;
    public double completionTime;
    public bool isDebugAlchemy;
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord speed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord drainCostMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord special = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord timeReqMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord timeScalingMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord masteryXpRate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord effectLevels = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord overdrivePower = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord overdriveSpeed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord overdriveDrainCostMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord overdriveXpRate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord freeUsageSlots = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord maxUsageSlots = new ValueModifierRecord(new BigDouble(0.0, 0));
    private BigDouble cachedCompletionTime;
    private BigDouble cachedRequiredXp;
    public BigDouble CachedCompletionTime { get => cachedCompletionTime; set => cachedCompletionTime = value; }
    public BigDouble CachedRequiredXp { get => cachedRequiredXp; set => cachedRequiredXp = value; }
    public ExperienceContainer ExperienceContainer { get => experienceContainer; set => experienceContainer = value; }
}
