using System;
using System.Collections.Generic;
using System.Linq;

public sealed class StructureTypeSO : UpgradeableObject
{
    private List<StructureSO> structures = StructureSO.All;

    public void RegisterStructure(StructureSO structure) => structures.Add(structure);
    public List<StructureSO> GetAllStructures() => structures;

    // Purpose-built malformed fixtures can replace the authored membership without reflecting the
    // private game field themselves.
    public void SetStructuresForTests(List<StructureSO> value) => structures = value;
}

public sealed class SpellTypeSO : IdScriptableObject
{
    public static List<SpellTypeSO> All = new List<SpellTypeSO>();
    public int typeLevel;
    public BigDouble typeXp;
    public double typeXpRequiredBase;
    public double augmentPowerMod;
    public bool hasNoLevels;
    public bool isElemental;
    public bool isLoadoutUnique;
    public bool hasNotTypeSignificance;
    public bool isVisible;
    public bool debugMode;
    public ValueModifierRecord typeXpMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord cooldownSpeed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord cooldownTime = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord costMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord drainCostMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord durationMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord elementalResonance = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord augmentResonance = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord maxStacksMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord scalingMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord usageCostReduction = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord bonusCritRate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord critEffectMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord critDurationMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord bonusDoubleCastRate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord doubleCastEffectMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord chargeTimeMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord chargeEffectMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord chargeSpecialMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord bonusFlashRate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord flashEffectMod = new ValueModifierRecord(new BigDouble(0.0, 0));
}


public interface ILevelable
{
}

public interface ILevelableHasFree
{
}

public sealed class EquipmentTypeSO : IdScriptableObject, ILevelable, ILevelableHasFree
{
    public static List<EquipmentTypeSO> All = new List<EquipmentTypeSO>();
    public int level;
    public int freeLevels;
    public int baseUsage;
    public ValueModifierRecord masteryLevel = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord maxTypeSlots = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ModifierRecord powerMod = new ModifierRecord();
    public ModifierRecord experienceRateMod = new ModifierRecord();
    public ResourceCostList LevelCost = new ResourceCostList();
    public ResourceCostList BonusLevelCost = new ResourceCostList();
    public bool NativeCanLevel = true;
    public bool NativeVisible = true;
    public bool NativeAvailable = true;
    public bool SuppressLevelPurchase;
    public bool SuppressBonusPurchase;
    public int GetMaxTypeSlots() => maxTypeSlots.GetValue().ToInt();
    public int GetLevel() => level + freeLevels;
    public int GetFreeLevels() => freeLevels;
    public bool CanLevel() => NativeCanLevel;
    public bool IsVisible() => NativeVisible;
    public bool IsAvailable() => NativeAvailable;
    public ResourceCostList GetLevelCost() => LevelCost;
    public ResourceCostList GetFreeLevelCost() => BonusLevelCost;
    public void PurchaseLevel() { if (!SuppressLevelPurchase) level++; }
    public void PurchaseFreeLevel() { if (!SuppressBonusPurchase) freeLevels++; }
}


public sealed class ResourceTypeSO : IdScriptableObject, ILevelable, ILevelableHasFree
{
    public static List<ResourceTypeSO> All = new List<ResourceTypeSO>();
    public int level;
    public int freeLevels;
    public bool specialHidden;
    public bool ignoreAudit;
    public bool ignoreEffects;
    public bool auditHasMaxQuantity;
    public ModifierRecord rateMod = new ModifierRecord();
    public ModifierRecord maxQuantityMod = new ModifierRecord();
    public ModifierRecord maxQuantityRateMod = new ModifierRecord();
    public ModifierRecord qualityMod = new ModifierRecord();
    public ModifierRecord gainRateMod = new ModifierRecord();
    public ModifierRecord drainMod = new ModifierRecord();
    public ModifierRecord lossPercentMod = new ModifierRecord();
    public ModifierRecord restMod = new ModifierRecord();
    public ModifierRecord splashRate = new ModifierRecord();
    public ModifierRecord splashRateMaxPercent = new ModifierRecord();
    public ModifierRecord splashRateInterest = new ModifierRecord();
    public ModifierRecord splashRateMissing = new ModifierRecord();
    public ModifierRecord splashRateLifetime = new ModifierRecord();
    public ModifierRecord rawMaxQuantity = new ModifierRecord();
    public ModifierRecord attributeCostMod = new ModifierRecord();
    public ModifierRecord reservationMod = new ModifierRecord();
    public ModifierRecord reverberateMod = new ModifierRecord();
    public ModifierRecord reverberateTimeMod = new ModifierRecord();
    public ModifierRecord replenishRatio = new ModifierRecord();
    public ModifierRecord replenishTimeMod = new ModifierRecord();
    public ModifierRecord decayRatio = new ModifierRecord();
    public ModifierRecord decayTimeMod = new ModifierRecord();
    public ResourceCostList LevelCost = new ResourceCostList();
    public ResourceCostList BonusLevelCost = new ResourceCostList();
    public bool NativeCanLevel = true;
    public bool NativeVisible = true;
    public bool NativeAvailable = true;
    public bool SuppressLevelPurchase;
    public bool SuppressBonusPurchase;
    public int GetLevel() => level + freeLevels;
    public int GetFreeLevels() => freeLevels;
    public bool CanLevel() => NativeCanLevel;
    public bool IsVisible() => NativeVisible && !specialHidden;
    public bool IsAvailable() => NativeAvailable && IsVisible();
    public ResourceCostList GetLevelCost() => LevelCost;
    public ResourceCostList GetFreeLevelCost() => BonusLevelCost;
    public void PurchaseLevel() { if (!SuppressLevelPurchase) level++; }
    public void PurchaseFreeLevel() { if (!SuppressBonusPurchase) freeLevels++; }
}


/// <summary>Its two levels are the same numbers as the others, under the game's own names.</summary>
public sealed class CraftingRecipeTypeSO : IdScriptableObject
{
    public static List<CraftingRecipeTypeSO> All = new List<CraftingRecipeTypeSO>();
    public int startingLevel;
    public int maxStartingLevel;
    public string craftVerb;
    public bool isLevelType;
    public bool initiated;
    public double magnitudeLoss;
    public double magnitudeTime;
    public ValueModifierRecord magnitudeIncrement = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ModifierRecord power = new ModifierRecord();
    public ModifierRecord speed = new ModifierRecord();
    public ModifierRecord costMod = new ModifierRecord();
    public ModifierRecord costIncrementMod = new ModifierRecord();
    public ModifierRecord efficiencyMod = new ModifierRecord();
    public ModifierRecord autoPenaltyMod = new ModifierRecord();
    public ModifierRecord multiPenaltyMod = new ModifierRecord();
}

public sealed class CraftingRecipeListVariable : GenericListVariable<CraftingRecipeSO>
{
}

public class AbstractRefInstance<T> where T : IdScriptableObject
{
    public T reference = null!;
    public T get_reference() => reference;
    public bool IsEmpty() => reference is null;
    public Guid GetGuidReference() => reference.GetGuid();
}

public sealed class CraftingRecipeSO : IdScriptableObject
{
    public static List<CraftingRecipeSO> All = new List<CraftingRecipeSO>();
    public List<CraftingRecipeTypeSO> craftingTypes = new List<CraftingRecipeTypeSO>();
    public ResourceCostList recipeCost = new ResourceCostList();
    public ResourceCostList generatedResources = new ResourceCostList();
    public List<PersistentEffectBlock> engagementEffects = new List<PersistentEffectBlock>();
    public List<InstantEffectBlock> completeEffects = new List<InstantEffectBlock>();
    public bool useQuantityAsLevel;
    public double timeToComplete;
    public bool visible;
    public bool BuyAllowed = true;
    public int MaximumAffordableLevel = int.MaxValue;
    public BigDouble StartingQuantity = BigDouble.One;
    public ResourceCostList TotalCost = new ResourceCostList();
    public CraftingRecipeTypeSO MainType = new CraftingRecipeTypeSO
    {
        maxStartingLevel = 1,
        isLevelType = true,
    };
    public bool InstantCraftEnabled;
    public ConsumableSO? InstantOutput;
    public bool ThrowAfterPurchase;
    public bool ThrowDuringConstruction;
    public bool ThrowAfterInitiation;
    public bool ThrowAfterInstantAdmission;
    public bool ThrowAfterExecute;
    public bool SuppressEffectPublication;
    public bool SuppressInstantAdmission;
    public int PurchaseCalls;
    public int VisibilityCalls;
    public int StartingQuantityCalls;
    public int CanBuyCalls;
    public int ExecuteCalls;
    public BigDouble? MultiBuyQuantityOverride;
    private readonly PassiveObservable.Channel effectChannel = new("craft");

    public bool IsVisible()
    {
        VisibilityCalls++;
        return visible;
    }
    public bool CanBuyAt(BigDouble quantity)
    {
        CanBuyCalls++;
        return BuyAllowed &&
            quantity > BigDouble.Zero &&
            quantity.ToDouble() <= MaximumAffordableLevel &&
            TotalCost.HasEnough();
    }
    public bool CanBuy() => IsVisible() && CanBuyAt(GetPurchaseQuantity(BigDouble.One));
    public BigDouble GetPurchaseQuantity(BigDouble previousQuantity) =>
        useQuantityAsLevel ? GetStartingQuantity() : BigDouble.One;
    public BigDouble GetMultiBuyQuantity(BigDouble previousQuantity) =>
        MultiBuyQuantityOverride ?? previousQuantity + GlobalVariables.GetMultiBuy().AsInt();
    public static int CalcAutomatedQuantity(BigDouble quantity) => quantity.ToInt();
    public BigDouble GetStartingQuantity()
    {
        StartingQuantityCalls++;
        return StartingQuantity;
    }
    public ResourceCostList GetTotalCost(BigDouble previousQuantity, BigDouble purchasedQuantity) =>
        TotalCost;
    public CraftingRecipeTypeSO GetMainType() => MainType;

    public void Execute()
    {
        ExecuteCalls++;
        if (!CanBuy()) return;
        recipeCost.Multiply(GetPurchaseQuantity(BigDouble.One)).PerformCost();
        if (!SuppressEffectPublication) effectChannel.Update("craft");
        if (ThrowAfterExecute)
            throw new InvalidOperationException("injected failure after direct execution");
    }

    public void PurchaseQuantity(BigDouble purchasedQuantity, BigDouble previousQuantity)
    {
        PurchaseCalls++;
        TotalCost.PerformCost();
        effectChannel.Update("craft");
        if (MainType.isLevelType)
            MainType.maxStartingLevel = Math.Max(
                MainType.maxStartingLevel,
                purchasedQuantity.ToInt() + previousQuantity.ToInt());
        if (ThrowAfterPurchase)
            throw new InvalidOperationException("injected failure after purchase");
    }

    public PassiveObservable.Channel GetEffectChannel() => effectChannel;
}

public sealed class CraftingInstanceListVariable : GenericListVariable<CraftingInstance>
{
    public bool isAutoList;
    public bool SuppressAutomation;
    public bool SuppressAutomationRemoval;

    public BigDouble GetQuantity(CraftingRecipeSO recipe)
    {
        var total = BigDouble.Zero;
        for (var index = 0; index < value.Count; index++)
            if (ReferenceEquals(value[index].reference, recipe)) total += value[index].Quantity;
        return total;
    }

    public CraftingInstance? GetInstance(CraftingRecipeSO recipe) =>
        value.Find(instance => ReferenceEquals(instance.reference, recipe));

    public CraftingInstance AutomateCraft(CraftingRecipeSO recipe, int amount)
    {
        var instance = GetInstance(recipe);
        if (instance is not null)
        {
            if (!SuppressAutomation) instance.AddAutomationQuantity(amount);
            return instance;
        }
        instance = new CraftingInstance(recipe, amount).SetAuto(true);
        instance.Initiate();
        if (!SuppressAutomation) instance.SetAutomationQuantity(amount);
        Add(instance);
        return instance;
    }

    public void RemoveAutomation(CraftingInstance instance, int amount)
    {
        if (SuppressAutomationRemoval) return;
        instance.AddAutomationQuantity(amount);
        if (instance.Quantity <= BigDouble.Zero)
        {
            instance.Remove();
            Remove(instance);
        }
    }
}

public sealed class UICraftingPage : UnityEngine.Object
{
    public CraftingRecipeListVariable availableRecipes = new CraftingRecipeListVariable();
    public CraftingInstanceListVariable craftingQueueInstances = new CraftingInstanceListVariable();
    public CraftingInstanceListVariable craftingAutomationInstances =
        new CraftingInstanceListVariable { isAutoList = true };
    public IntVariable craftMode = new IntVariable();
    public CraftingRecipeTypeSO mainCraftType = new CraftingRecipeTypeSO();

    private int GetAutoCraftingQuantity(CraftingRecipeSO recipe) =>
        craftingAutomationInstances.GetInstance(recipe)?.GetAutomationQuantity() ?? 0;
}

public sealed class CraftingInstance : AbstractRefInstance<CraftingRecipeSO>
{
    public BigDouble Quantity = BigDouble.One;
    public bool Automatic;
    public bool Expired;
    public bool Initiated;
    public bool SuppressAddQuantity;
    public bool ThrowAfterAddQuantity;
    public int AutomationQuantity;
    public bool Removed;

    public CraftingInstance()
    {
    }

    public CraftingInstance(CraftingRecipeSO recipe, BigDouble quantity)
    {
        reference = recipe;
        Quantity = quantity;
        if (recipe.ThrowDuringConstruction)
            throw new InvalidOperationException("injected failure during construction");
    }

    public void Initiate()
    {
        Initiated = true;
        if (reference.ThrowAfterInitiation)
            throw new InvalidOperationException("injected failure after initiation");
    }

    public bool CheckInstantCraft() => reference.InstantCraftEnabled;

    public void AddQuantity(BigDouble quantity)
    {
        if (!SuppressAddQuantity) Quantity += quantity;
        if (ThrowAfterAddQuantity)
            throw new InvalidOperationException("injected failure after quantity addition");
    }

    public CraftingInstance SetAuto(bool automatic)
    {
        Automatic = automatic;
        return this;
    }

    public int GetAutomationQuantity() => AutomationQuantity;
    public void AddAutomationQuantity(int amount) =>
        SetAutomationQuantity(AutomationQuantity + amount);
    public void SetAutomationQuantity(int amount)
    {
        AutomationQuantity = amount;
        Quantity = new BigDouble(amount);
    }

    public void Remove() => Removed = true;
    public void CancelCraft() => Remove();

    public void InstantCraft()
    {
        if (!reference.SuppressInstantAdmission && reference.InstantOutput is not null)
        {
            var level = Quantity.ToInt();
            var count = reference.InstantOutput.consumableCounts.Find(row => row.Level == level);
            if (count is null)
            {
                count = new ConsumableCount { Level = level };
                reference.InstantOutput.consumableCounts.Add(count);
            }
            count.Quantity++;
        }
        if (!reference.SuppressInstantAdmission) Expired = true;
        if (reference.ThrowAfterInstantAdmission)
            throw new InvalidOperationException("injected failure after instant admission");
    }

    public BigDouble GetQuantity() => Quantity;
    public bool IsAuto() => Automatic;
    public bool IsExpired() => Expired;
}

public sealed class EnchantmentSO : IdScriptableObject
{
    public sealed class EnchantTable
    {
        public List<EnchantmentInstance> enchantments = new List<EnchantmentInstance>();
    }

    public sealed class EnchantItemScript : IInstantEffectScript
    {
        public EnchantmentSO enchantment = new EnchantmentSO();
        public Targeting.TargetReference targetReference = new Targeting.TargetReference();
    }
}

public sealed class EnchantmentInstance : AbstractRefInstance<EnchantmentSO>
{
    public int Level = 1;
    public int GetLevel() => Level;
}


public sealed class HarvestElementSO : UpgradeableObject
{
    public static List<HarvestElementSO> All = new List<HarvestElementSO>();
    public List<HarvestTypeSO> harvestTypes = new List<HarvestTypeSO>();
    public BigDouble masteryXp;
    public int masteryLevel;
    public double harvestTime;
    public double growthTime;
    public double rarityValue;
    public double initialMaxQuantity;
    public double requiredXpToLevel;
    public ValueModifierRecord instances = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord harvestSpeedMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord drainCostMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord autoGenerationMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord experienceRateMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord actionXpRate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord actionPower = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord actionSpeed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord actionCostMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    private BigDouble harvestRate;
    private BigDouble lastOutputRate;
    public bool visible = true;
    public bool available = true;
    public ResourceCostList usageCost = new ResourceCostList();
    public int MaximumAdditional = 16;
    private readonly List<HarvestActionInstance> actionInstances = new();

    public bool IsVisible() => visible;
    public bool IsAvailable() => available;
    public BigDouble MaximumNumberInstances() => new BigDouble(MaximumAdditional);
    public List<HarvestActionInstance> GetActionInstances() => actionInstances;
    public void AddAction(HarvestActionSO action) =>
        actionInstances.Add(new HarvestActionInstance(this, action));

    /// <summary>
    /// Private in the game, created by the element rather than registered, which is why the resource
    /// registry cannot reach it and it is read through its owner.
    /// </summary>
    private ResourceSO harvestResource = new ResourceSO();
}

public sealed class HarvestActionSO : UpgradeableObject
{
    public static List<HarvestActionSO> All = new List<HarvestActionSO>();
    public List<HarvestActionTypeSO> actionTypes = new List<HarvestActionTypeSO>();
    public bool visible = true;
    public ResourceCostList DrainCost = new ResourceCostList();
    public BigDouble NextDrainPercent = new BigDouble(100);
}

public sealed class HarvestActionInstance
{
    private readonly HarvestElementSO elementReference;
    private readonly HarvestActionSO reference;

    public HarvestActionInstance(HarvestElementSO element, HarvestActionSO action)
    {
        elementReference = element;
        reference = action;
    }

    public int instances;
    public HarvestActionSO GetAction() => reference;
    public HarvestElementSO GetElement() => elementReference;
    public bool IsVisible() => reference.visible;
    public int GetMaximumInstances() => elementReference.masteryLevel + 1;
    public ScalingInfo GetScalingInfo(int count) => new ScalingInfo
    {
        Level = count,
        DrainCostMod = reference.NextDrainPercent,
    };
    private ResourceCostList ComputeResourceCost() => reference.DrainCost;
    public bool IsSameRef(HarvestActionInstance other) => other is not null &&
        ReferenceEquals(other.elementReference, elementReference) &&
        ReferenceEquals(other.reference, reference);
    public bool IsEmpty() => instances <= 0;
    public void ChangeInstance(int amount) =>
        instances = Math.Max(0, Math.Min(instances + amount, GetMaximumInstances()));
}

public sealed class HarvestElementListVariable : StackableListVariable<HarvestElementSO>
{
    public bool SuppressMutation { get; set; }

    public void AddInstance(HarvestElementSO element, BigDouble amount)
    {
        if (!SuppressMutation) Stack(element, amount.ToInt());
    }

    public void RemoveInstance(HarvestElementSO element, BigDouble amount)
    {
        if (!SuppressMutation) Unstack(element, amount.ToInt());
    }
}

public sealed class HarvestActionInstanceListVariable : GenericListVariable<HarvestActionInstance>
{
    public bool SuppressMutation { get; set; }

    public HarvestActionInstance? FindInstance(HarvestActionInstance prototype) =>
        value.Find(candidate => candidate.IsSameRef(prototype));

    public void AddInstance(HarvestActionInstance prototype, int amount)
    {
        if (SuppressMutation) return;
        var current = FindInstance(prototype);
        if (current is null)
        {
            if (!HasEmptySpot()) return;
            current = new HarvestActionInstance(prototype.GetElement(), prototype.GetAction());
            Add(current);
        }
        current.ChangeInstance(amount);
    }

    public void RemoveInstance(HarvestActionInstance instance, int amount)
    {
        if (SuppressMutation) return;
        var current = FindInstance(instance);
        if (current is null) return;
        current.ChangeInstance(-amount);
        if (current.IsEmpty()) Remove(current);
    }
}


public sealed class TimeRuneSO : UpgradeableObject, IDiscoverable, ILevelable
{
    public static List<TimeRuneSO> All = new List<TimeRuneSO>();
    public List<TimeRuneTypeSO> timeRuneTypes = new List<TimeRuneTypeSO>();
    public bool discovered;
    public int level;
    public int discRarityLevel;
    public BigDouble masteryXp;
    public int masteryLevel;
    public bool isDiscoverRequired;
    public bool seen;
    public ResourceCostList discoveryCost = new ResourceCostList();
    public List<GlyphSO> glyphRecipe = new List<GlyphSO>();
    public List<ResourceSO> resourceRecipe = new List<ResourceSO>();
    public bool NativeDiscoverVisible { get; set; } = true;
    public bool NativeCanDiscover { get; set; } = true;
    public bool SuppressDiscovery { get; set; }
    public bool ThrowBeforeDiscovery { get; set; }
    public bool ThrowAfterDiscovery { get; set; }
    public int DiscoverCalls { get; private set; }
    public ValueModifierRecord freeUsages = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord powerScalingMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord masteryXpMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ResourceCostList LevelCost = new ResourceCostList();
    public bool NativeCanLevel = true;
    public bool NativeVisible = true;
    public bool NativeAvailable = true;
    public bool SuppressLevelPurchase;

    public ResourceCostList GetDiscoverCost() => discoveryCost;
    public List<GlyphSO> GetGlyphRecipe() => new List<GlyphSO>(glyphRecipe);
    public List<ResourceSO> GetResourceRecipe() => new List<ResourceSO>(resourceRecipe);
    public bool IsDiscoverVisible() => NativeDiscoverVisible;
    public bool CanDiscover() => NativeCanDiscover;
    public bool IsDiscovered() => discovered;
    public bool IsDiscoverRequired() => isDiscoverRequired;
    public int GetLevel() => level;
    public bool CanLevel() => NativeCanLevel;
    public bool IsVisible() => NativeVisible;
    public bool IsAvailable() => NativeAvailable;
    public ResourceCostList GetLevelCost() => LevelCost;
    public void PurchaseLevel() { if (!SuppressLevelPurchase) level++; }
    public void Discover()
    {
        DiscoverCalls++;
        if (ThrowBeforeDiscovery)
            throw new InvalidOperationException("injected failure before discovery");
        if (!SuppressDiscovery) discovered = true;
        if (ThrowAfterDiscovery)
            throw new InvalidOperationException("injected failure after discovery");
    }
}


public sealed class GlyphSO : IdScriptableObject, ITooltipable, IDiscoverable, ILevelable, ILevelableHasFree
{
    public static List<GlyphSO> All = new List<GlyphSO>();
    public List<GlyphTypeSO> glyphTypes = new List<GlyphTypeSO>();
    public string DisplayName = string.Empty;
    public string Description = string.Empty;
    public int level;
    public int freeLevels;
    public int discRarityLevel;
    public bool discovered;
    public bool discoverable;
    public bool discoveryRequired;
    public bool augmentsSpells;
    public bool requiresDuration;
    public bool requiresToggleable;
    public bool NativeAvailable { get; set; } = true;
    public bool NativeVisible { get; set; } = true;
    public ResourceCostList discoveryCost = new ResourceCostList();
    public List<GlyphSO> glyphRecipe = new List<GlyphSO>();
    public List<ResourceSO> resourceRecipe = new List<ResourceSO>();
    public bool NativeDiscoverVisible { get; set; } = true;
    public bool NativeCanDiscover { get; set; } = true;
    public bool SuppressDiscovery { get; set; }
    public bool ThrowBeforeDiscovery { get; set; }
    public bool ThrowAfterDiscovery { get; set; }
    public int DiscoverCalls { get; private set; }
    public int masteryReqCount;
    public ValueModifierRecord freeUsages = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord freeLoadoutUsages = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord maxUsages = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifier creationCostMod;
    public ResourceCostList LevelCost = new ResourceCostList();
    public ResourceCostList BonusLevelCost = new ResourceCostList();
    public bool NativeCanLevel = true;
    public bool SuppressLevelPurchase;
    public bool SuppressBonusPurchase;

    public string GetName() => DisplayName;
    public bool IsAvailable() => NativeAvailable;
    public bool IsVisible() => NativeVisible;
    public ResourceCostList GetDiscoverCost() => discoveryCost;
    public List<GlyphSO> GetGlyphRecipe() => new List<GlyphSO>(glyphRecipe);
    public List<ResourceSO> GetResourceRecipe() => new List<ResourceSO>(resourceRecipe);
    public bool IsDiscoverVisible() => NativeDiscoverVisible;
    public bool CanDiscover() => NativeCanDiscover;
    public bool IsDiscovered() => discovered;
    public int GetLevel() => level + freeLevels;
    public int GetFreeLevels() => freeLevels;
    public bool CanLevel() => NativeCanLevel;
    public ResourceCostList GetLevelCost() => LevelCost;
    public ResourceCostList GetFreeLevelCost() => BonusLevelCost;
    public void PurchaseLevel() { if (!SuppressLevelPurchase) level++; }
    public void PurchaseFreeLevel() { if (!SuppressBonusPurchase) freeLevels++; }
    public bool IsDiscoverRequired() => discoveryRequired;
    public void Discover()
    {
        DiscoverCalls++;
        if (ThrowBeforeDiscovery)
            throw new InvalidOperationException("injected failure before discovery");
        if (!SuppressDiscovery) discovered = true;
        if (ThrowAfterDiscovery)
            throw new InvalidOperationException("injected failure after discovery");
    }
    public bool IsSpellAugment() => augmentsSpells;
    public int GetMaxUsages() => (int)maxUsages.GetValue().ToDouble();
    public static ResourceCostList GetCreationCostOfList(
        ResourceCostList startingCost,
        IEnumerable<GlyphSO> glyphs)
    {
        var result = startingCost;
        foreach (var glyph in glyphs)
            if (glyph is not null) result = result.Apply(glyph.creationCostMod);
        return result;
    }
    public static bool MeetsNonLvRequirements(List<GlyphSO> glyphs, Spell spell)
    {
        foreach (var glyph in glyphs)
            if (glyph.requiresDuration && !spell.IsDurationSpell() ||
                glyph.requiresToggleable && !spell.IsToggledSpell())
                return false;
        return true;
    }
    public static int GetMasterReqOfList(List<GlyphSO> glyphs)
    {
        var result = 0;
        foreach (var glyph in glyphs) result = Math.Max(result, glyph.masteryReqCount);
        return result;
    }
    public string GetDisplayType() => "Glyph";
    public UnityEngine.Sprite GetIcon() => new UnityEngine.Sprite();
    public UnityEngine.Color GetColor() => default;
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => Description;
    public List<TooltipNode> GetTooltipNodes() => new List<TooltipNode>();
    public List<TooltipNode> GetAltTooltipNodes() => new List<TooltipNode>();
}

public sealed class ConsumableTypeSO : IdScriptableObject
{
    public string DisplayName = string.Empty;
    public IntVariable maximumCarryLoad = new IntVariable();

    public string GetName() => DisplayName;
}

public sealed partial class ConsumableSO : IdScriptableObject
{
    public static List<ConsumableSO> All = new List<ConsumableSO>();
    public string DisplayName = string.Empty;
    public UnityEngine.Sprite Icon = new UnityEngine.Sprite();
    public bool visible;
    public bool randomized;
    public int maxCreatedLv;
    public BigDouble currentPrepTime;
    public BigDouble currentCooldown;
    public BigDouble currentCooldownTime;
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord durationMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord special = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord prepSpeed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord bonusLevels = new ValueModifierRecord(new BigDouble(0.0, 0));
    public List<ConsumableTypeSO> consumableTypes = new List<ConsumableTypeSO>();
    public List<InstantEffectBlock> onUseEffects = new List<InstantEffectBlock>();
    public ResourceCostList consumeCost = new ResourceCostList();
    public ResourceCostList usageCost = new ResourceCostList();
    public List<ConsumableUsage> consumableUsages = new List<ConsumableUsage>();
    public List<ConsumableCount> consumableCounts = new List<ConsumableCount>();

    /// <summary>Private in the game too, which is exactly why the save record hid the stock count.</summary>
    private int quantity;
    private int queuedQuantity;
    private int gainedSince;
    private ConsumableUsage? nextUsage;
    public bool FireAllowed = true;
    public bool SelectionNoOp;
    public int MaximumCarryLoad;

    public void SetStock(int quantityN, int queuedN, int gainedSinceN)
    {
        quantity = quantityN;
        queuedQuantity = queuedN;
        gainedSince = gainedSinceN;
    }

    public bool CanFire() =>
        FireAllowed &&
        quantity > 0 &&
        currentCooldown <= BigDouble.Zero &&
        consumeCost.HasEnough();

    public bool IsVisible() => visible;

    public string GetName() => DisplayName;

    public UnityEngine.Sprite GetIcon() => Icon;

    public void SelectAndFire()
    {
        if (!CanFire() || !Inventory.CanUseConsumable()) return;
        if (SelectionNoOp) return;

        var accepted = Math.Min(GlobalVariables.GetMultiBuy().AsInt(), quantity);
        queuedQuantity += accepted;
        if (accepted > 0)
        {
            quantity--;
            consumeCost.PerformCost();
            if (hasDuration)
            {
                nextUsage = new ConsumableUsage
                {
                    en = false,
                    dr = new BigDouble(durationBase),
                    maxDr = new BigDouble(durationBase),
                };
                consumableUsages.Add(nextUsage);
            }
        }
        Inventory.BeginPreparing();
    }

    public void SetRandomization(bool randomizationN) => randomized = randomizationN;

    public void CancelUsage()
    {
        var usage = nextUsage ?? consumableUsages.Find(candidate => !candidate.en);
        if (usage is null) return;
        queuedQuantity = Math.Max(0, queuedQuantity - 1);
        usage.GetResultInfo().Cancel();
        consumableUsages.Remove(usage);
        if (ReferenceEquals(nextUsage, usage)) nextUsage = null;
        Inventory.Preparing = false;
    }

    public void Discard(int amount)
    {
        var actual = Math.Min(Math.Max(0, amount), quantity);
        quantity -= actual;
    }

    public bool IsRandomized() => canBeRandomized && randomized;

    public int GetQuantity() => quantity;

    public int GetQueued() => queuedQuantity;

    public int GetMaximumCarryLoad() => MaximumCarryLoad;

    public ConsumableCount GetStrongest()
    {
        ConsumableCount? strongest = null;
        foreach (var count in consumableCounts)
            if (count.Quantity > 0 && (strongest is null || count.Level > strongest.Level))
                strongest = count;
        return strongest ?? new ConsumableCount();
    }

    public int GetStrongestLevel() => GetStrongest().GetLevel();

    public ScalingInfo GetCountScalingInfo(ConsumableCount count) =>
        new ScalingInfo { Level = count.GetLevel() };

    public double preparationTime;
    public bool canBeRandomized;
    public bool hasDuration;
    public double durationBase;
    private bool queueOnStart;
}

public sealed class ConsumableCount
{
    public int Level = 1;
    public int Quantity;
    public int fr;

    public int GetLevel() => Level;
    public int GetQuantity() => Quantity;
}

public sealed class ConsumableUsage
{
    public Guid Identity = Guid.NewGuid();
    public ScalingInfo baseSi = new ScalingInfo();
    public bool en;
    public BigDouble dr;
    public BigDouble maxDr;
    private readonly EffectResultInfo resultInfo = new EffectResultInfo();

    public Guid GetGuid() => Identity;
    public EffectResultInfo GetResultInfo() => resultInfo;
}

public sealed class ScalingInfo
{
    public int Level = 1;
    public BigDouble DrainCostMod = new BigDouble(100);

    public int GetLevelInt() => Level;
    public BigDouble GetDrainCostMod() => DrainCostMod;
    public static ScalingInfo Basic(BigDouble level) =>
        new ScalingInfo { Level = level.ToInt() };
}

public interface IInstantEffectScript
{
}

public sealed class UnexpectedInstantEffectScript : IInstantEffectScript
{
}

public sealed class RequestTargetEffectScript : IInstantEffectScript
{
    public Targeting.TargetSelectOptions targetOptions = new Targeting.TargetSelectOptions();
}

namespace Targeting
{
    public enum TargetReferenceTypes
    {
        Target = 0,
    }

    public sealed class TargetReference
    {
        public TargetReferenceTypes refType;
    }

    public interface ITargetable
    {
    }

    public class BaseTargetSelection
    {
    }

    public sealed class TargetStructure : BaseTargetSelection
    {
        public List<ITargetable> Candidates = new List<ITargetable>();

        public List<ITargetable> GetRandomList(ScalingInfo scaling) =>
            new List<ITargetable>(Candidates);
    }

    public sealed class TargetSelectOptions
    {
        public BaseTargetSelection Targeting = new TargetStructure();

        public BaseTargetSelection GetTargeting() => Targeting;
        public TargetReferenceTypes GetTargetRefType() => TargetReferenceTypes.Target;
    }
}

public sealed class Inventory
{
    private static Inventory _instance = new Inventory();
    public ConsumableRefListVariable hotBar = new ConsumableRefListVariable();
    public ConsumableRefListVariable allConsumables = new ConsumableRefListVariable();
    public static bool Preparing { get; set; }

    public static bool CanUseConsumable() => !Preparing;

    public static void BeginPreparing() => Preparing = true;

    public static Inventory Current => _instance;

    public static void ResetLists()
    {
        _instance = new Inventory();
    }
}

public sealed class ConsumableRefListVariable : GenericListVariable<ConsumableSO>
{
}

public sealed class RitualSO : IdScriptableObject, IDiscoverable
{
    public static List<RitualSO> All = new List<RitualSO>();
    public List<RitualTypeSO> ritualTypes = new List<RitualTypeSO>();
    public bool discovered;
    public bool inBattle;
    public int lastReachedLevel;
    public int reachedLevel;
    public int wavesCompleted;
    public int selectedLevel;
    public int discRarityLevel;
    public ResourceCostList discoveryCost = new ResourceCostList();
    public List<GlyphSO> glyphRecipe = new List<GlyphSO>();
    public List<ResourceSO> resourceRecipe = new List<ResourceSO>();
    public bool NativeDiscoverVisible { get; set; } = true;
    public bool NativeCanDiscover { get; set; } = true;
    public bool SuppressDiscovery { get; set; }
    public bool ThrowBeforeDiscovery { get; set; }
    public bool ThrowAfterDiscovery { get; set; }
    public int DiscoverCalls { get; private set; }
    public ResourceCostList activationCost = new ResourceCostList();
    public ResourceCostList completionCost = new ResourceCostList();
    public ModifierListRef completionCostPerLevel = new ModifierListRef();
    public ResourceFillList resourceFillList = new ResourceFillList();
    public bool NativeUsageRequirementsMet { get; set; } = true;
    public int NativeMaximumSelectedLevel { get; set; } = 1;
    public bool SuppressLevelChange { get; set; }
    public bool SuppressCancel { get; set; }

    /// <summary>
    /// Left null by default on purpose: the game leaves these null before first use, and the count
    /// accessor has to read that as zero rather than throw.
    /// </summary>
    public List<object>? ritualInstances;

    public List<object> durationRewardBlocks = new List<object>();
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord speed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord special = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord durationMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord echoRating = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord echoPower = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord critRating = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord critPower = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord critDurationMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord chainLengthBonus = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord chainPower = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord completionCostMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord completionRateMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    /// <summary>Private in the game, as the run tiers are.</summary>
    private BigDouble battleTotalWeight;

    private int critLevel;
    private int echoLevel;
    private int chainLevel;

    public ResourceCostList GetDiscoverCost() => discoveryCost;
    public List<GlyphSO> GetGlyphRecipe() => new List<GlyphSO>(glyphRecipe);
    public List<ResourceSO> GetResourceRecipe() => new List<ResourceSO>(resourceRecipe);
    public bool IsDiscoverVisible() => NativeDiscoverVisible;
    public bool CanDiscover() => NativeCanDiscover;
    public bool IsDiscovered() => discovered;
    public bool IsDiscoverRequired() => isDiscoverRequired;
    public void Discover()
    {
        DiscoverCalls++;
        if (ThrowBeforeDiscovery)
            throw new InvalidOperationException("injected failure before discovery");
        if (!SuppressDiscovery) discovered = true;
        if (ThrowAfterDiscovery)
            throw new InvalidOperationException("injected failure after discovery");
    }

    public void SetRunState(int critLevelN, int echoLevelN, int chainLevelN, BigDouble totalWeight)
    {
        critLevel = critLevelN;
        echoLevel = echoLevelN;
        chainLevel = chainLevelN;
        battleTotalWeight = totalWeight;
    }
    public int GetMaxSelectedLevel() => NativeMaximumSelectedLevel;
    public bool HasMetUsageRequirements() => NativeUsageRequirementsMet;
    public ResourceCostList GetActivationCost() => activationCost;
    public ResourceCostList GetSelectedCompletionCost() => completionCost;
    public void ChangeStartingLevel(int value)
    {
        if (!SuppressLevelChange)
            selectedLevel = Math.Max(0, Math.Min(value, GetMaxSelectedLevel()));
    }
    public bool IsDurationRitual() => durationRewardBlocks.Count > 0;
    public bool IsDurationActive() => ritualInstances is { Count: > 0 };
    public void Cancel()
    {
        if (!SuppressCancel) ritualInstances?.Clear();
    }
    public bool hideEndScreenResults;
    public bool isDiscoverRequired;
    public bool forceLevel;
    public int forceLevelValue;
    public int baseWaves;
    public int maxWaves;
    public int wavesPerLevel;
    public double baseWeight;
    public int minimumEffectLevel;

    /// <summary>The results screen's own record: what the run banked, and the game's verdict on it.</summary>
    public List<SpoilsRecordEntry> currentSpoils = new List<SpoilsRecordEntry>();

    public bool IsFailedRun() => wavesCompleted < 5;
    public int GetCurrentLevel() => forceLevel ? forceLevelValue : selectedLevel;
    public int GetRequiredWaves() => Math.Min(
        baseWaves + (wavesPerLevel * GetCurrentLevel()),
        maxWaves > 0 ? maxWaves : int.MaxValue);
}

public sealed class SpoilsRecordEntry
{
    public SpoilsRecordEntry(ResourceSO resource, BigDouble quantity)
    {
        this.resource = resource;
        this.quantity = quantity;
    }

    public BigDouble quantity;
    public ResourceSO resource { get; }
}

public sealed class RitualVariable
{
    public RitualSO? Value { get; private set; }
    public bool SuppressToggle { get; set; }

    public bool IsItem(RitualSO ritual) => ReferenceEquals(Value, ritual);
    public void ToggleValue(RitualSO ritual)
    {
        if (SuppressToggle) return;
        Value = ReferenceEquals(Value, ritual) ? null : ritual;
    }
}

public sealed class RitualManager
{
    public static RitualManager? instance;
    public RitualVariable selectedRitual = new RitualVariable();
    public bool SuppressActivation { get; set; }

    public void ActivateSelectedRitual()
    {
        if (!SuppressActivation && selectedRitual.Value is { } ritual)
        {
            ritual.inBattle = true;
            if (BattleManager.instance?.activeRitual.Value is null)
                BattleManager.instance?.activeRitual.ToggleValue(ritual);
        }
    }
}

public sealed class BattleManager
{
    public static BattleManager? instance;
    public RitualVariable activeRitual = new RitualVariable();
    public bool SuppressEndRitual { get; set; }
    public bool IsInCombat() => activeRitual.Value is not null;
    public void EndRitual()
    {
        if (SuppressEndRitual || activeRitual.Value is not { } ritual) return;
        ritual.inBattle = false;
        activeRitual.ToggleValue(ritual);
    }
}

public sealed class UIModal : UnityEngine.MonoBehaviour
{
    private bool isOpen;
    private bool isClosing;
    private float graceTime;
    public TMPro.TextMeshProUGUI modalTitle = new TMPro.TextMeshProUGUI();

    public UIModal()
    {
        var root = new UnityEngine.GameObject("Modal");
        gameObject = root;
        transform = root.transform;
        name = root.name;
    }

    public bool SuppressClose { get; set; }
    public bool SuppressOpen { get; set; }
    public bool IsOpen() => isOpen;
    public void OpenForTest(float grace = 0f, string title = "")
    {
        isOpen = true;
        isClosing = false;
        graceTime = grace;
        modalTitle.text = title;
    }
    public void FinishCloseForTest() => isOpen = false;
    public void CloseModal()
    {
        if (!isOpen || isClosing || graceTime > 0f || SuppressClose) return;
        isClosing = true;
    }

    /// <summary>The visible half of the game's open: the panel's own subtree becomes live.</summary>
    public void PerformOpen()
    {
        if (SuppressOpen) return;
        isOpen = true;
        Activate(transform);
    }

    private static void Activate(UnityEngine.Transform node)
    {
        node.gameObject.SetActive(true);
        for (var index = 0; index < node.childCount; index++) Activate(node.GetChild(index));
    }
}

public sealed class UIModalActivator : UnityEngine.MonoBehaviour
{
    private readonly UnityEngine.UI.Button button;
    private readonly UIModal createdModal;
    private readonly bool modalCreated;

    public UIModalActivator(UIModal modal, string label = "Panel button")
    {
        var root = new UnityEngine.GameObject(label);
        gameObject = root;
        transform = root.transform;
        name = label;
        button = root.AddComponent<UnityEngine.UI.Button>();
        createdModal = modal;
        modalCreated = modal is not null;
    }

    public void SetLiveForTest(bool value)
    {
        gameObject.SetActive(value);
        button.interactable = value;
    }

    public void OpenModal()
    {
        if (!modalCreated) return;
        createdModal.PerformOpen();
    }
}

public sealed class AchievementSO : IdScriptableObject
{
    public static List<AchievementSO> All = new List<AchievementSO>();
    public int level;
    public bool seen;
    public bool logProgress;
    public string steamApiName;
    public int maxLevels;
    public int achievementStrength;
}


public sealed class AdvancementSO : IdScriptableObject
{
    public static List<AdvancementSO> All = new List<AdvancementSO>();
    public BigDouble levels;
    public BigDouble xp;
    public bool isPersistent;
    public double baseRequiredXp;
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(0.0, 0));
}


public sealed class ChallengeSO : IdScriptableObject
{
    public enum ChallengeState
    {
        None,
        QueuedStart,
        CurrentlyActive,
        Passed,
        Failed,
    }

    public static List<ChallengeSO> All = new List<ChallengeSO>();
    public List<ChallengeTypeSO> challengeTypes = new List<ChallengeTypeSO>();
    public int level;
    public ChallengeState state;
    public bool hasBeenSeen;
    public bool rewardQueued;
    public int maxLevel;
    public int weight;
    public double difficulty;
    public double baseReward;
    public bool NativeAvailableToRun { get; set; } = true;
    public bool SuppressQueueActivation { get; set; }
    public bool SuppressQueueToggle { get; set; }
    public bool SuppressAbandon { get; set; }
    public bool ThrowAfterQueueToggle { get; set; }
    public bool ThrowAfterAbandon { get; set; }
    public int QueueToggleCalls { get; private set; }
    public int AbandonCalls { get; private set; }

    public bool IsAvailableToRun() => NativeAvailableToRun && !IsMaxLevel();
    public bool IsCompletedOnce() => state == ChallengeState.Passed || level > 0;
    public bool IsMaxLevel() => maxLevel >= 0 && level >= maxLevel;
    public BigDouble GetDifficulty() => new BigDouble(difficulty, 0);
    public BigDouble GetNextInstanceBaseReward() => new BigDouble(baseReward, 0);
    public void QueueActivation()
    {
        if (!SuppressQueueActivation) state = ChallengeState.QueuedStart;
        hasBeenSeen = true;
    }
    public void EmptyState() { state = ChallengeState.None; }
    public void ToggleQueueActivation()
    {
        QueueToggleCalls++;
        if (!SuppressQueueToggle)
        {
            if (state == ChallengeState.None) QueueActivation();
            else if (state == ChallengeState.QueuedStart) state = ChallengeState.None;
        }
        if (ThrowAfterQueueToggle) throw new InvalidOperationException("injected failure after queue toggle");
    }
    public void AbandonChallenge()
    {
        AbandonCalls++;
        if (!SuppressAbandon) state = ChallengeState.Failed;
        if (ThrowAfterAbandon) throw new InvalidOperationException("injected failure after abandon");
    }
}

/// <summary>
/// The keyword taxonomies whose only member the suite reads is the stable identity behind the word.
/// </summary>
/// <remarks>
/// A real type asset is a full modifier-bearing sibling entity; these carry identity alone because
/// the keyword capture reads identity alone. The members that make them modifier-bearing belong to
/// whichever category publishes their bonus math.
/// </remarks>
public sealed class PassiveAbilityTypeSO : UpgradeableObject
{
    public static List<PassiveAbilityTypeSO> All = new List<PassiveAbilityTypeSO>();
}

public sealed class TimeRuneTypeSO : UpgradeableObject
{
    public static List<TimeRuneTypeSO> All = new List<TimeRuneTypeSO>();
}

public sealed class GlyphTypeSO : TooltipableObject
{
    public static List<GlyphTypeSO> All = new List<GlyphTypeSO>();
}

public sealed class HarvestActionTypeSO : UpgradeableObject
{
    public static List<HarvestActionTypeSO> All = new List<HarvestActionTypeSO>();
}

public sealed class RitualTypeSO : UpgradeableObject
{
    public static List<RitualTypeSO> All = new List<RitualTypeSO>();
}

public sealed class CharacterTypeSO : TooltipableObject
{
    public static List<CharacterTypeSO> All = new List<CharacterTypeSO>();
}

public sealed class PlotNodeTypeSO : UpgradeableObject
{
    public static List<PlotNodeTypeSO> All = new List<PlotNodeTypeSO>();
}

public sealed class HarvestTypeSO : UpgradeableObject
{
    public static List<HarvestTypeSO> All = new List<HarvestTypeSO>();
}

public sealed class ChallengeTypeSO : IdScriptableObject
{
    public bool limitedToOneInstance;
    public bool IsLimitedToOneInstance() => limitedToOneInstance;
}

public sealed class ChallengeListVariable : GenericListVariable<ChallengeSO>
{
    /// <summary>
    /// The restricted set is built from the types held by the challenges <em>currently in this
    /// list</em>, so a row leaving the list takes its restriction with it. A fixed answer could not
    /// tell a conflict from a swap whose give-up press clears the conflict.
    /// </summary>
    public bool IsChallengeRestricted(ChallengeSO challenge)
    {
        var restricted = new HashSet<ChallengeTypeSO>();
        foreach (var held in value)
        {
            foreach (var type in held.challengeTypes)
            {
                if (type.IsLimitedToOneInstance()) restricted.Add(type);
            }
        }
        return restricted.Count > 0 && challenge.challengeTypes.Any(restricted.Contains);
    }

    public void CycleOut()
    {
        foreach (var challenge in value) challenge.EmptyState();
    }
    public void Instantiate()
    {
        foreach (var challenge in value) challenge.QueueActivation();
    }
}

public sealed class ChallengeManager
{
    public static ChallengeManager instance = new ChallengeManager();
    public ChallengeListVariable activeChallenges = new ChallengeListVariable();
    public ChallengeListVariable preferredChallenges = new ChallengeListVariable();
    public List<ChallengeSO> NextChallenges { get; } = new List<ChallengeSO>();
    public bool SuppressFetch { get; set; }
    public bool ThrowAfterFetch { get; set; }
    public int FetchCalls { get; private set; }

    public void LoadNewActiveChallenges()
    {
        FetchCalls++;
        if (!SuppressFetch)
        {
            activeChallenges.CycleOut();
            activeChallenges.value = new List<ChallengeSO>(NextChallenges);
            activeChallenges.Instantiate();
        }
        if (ThrowAfterFetch) throw new InvalidOperationException("injected failure after time challenge fetch");
    }
}


public sealed class ThoughtStreamSO : IdScriptableObject
{
    public enum StreamState
    {
        Idle,
        Running,
    }

    public static List<ThoughtStreamSO> All = new List<ThoughtStreamSO>();
    public StreamState state;
}


public sealed class TutorialSO : IdScriptableObject
{
    public static List<TutorialSO> All = new List<TutorialSO>();
    public bool isCompleted;
}


public sealed class PlotNodeActionSO : IdScriptableObject
{
    public List<HarvestActionTypeSO> actionTypes = new List<HarvestActionTypeSO>();

    /// <summary>How an action charges its element cost. The game nests this here.</summary>
    public enum CostType
    {
        OnStart,
        OnExitPhase,
    }

    public static List<PlotNodeActionSO> All = new List<PlotNodeActionSO>();
    public bool hasBeenUsed;
    public bool isGrowingAction;
    public bool showExitTooltip;
    public int elementCost;
    public bool useSizeModForCost;
    public bool useAnyStateForCost;
    public bool parallelAction;
    public double baseTime;
    public bool useSpaceUsageForTimeMult;
    public bool ignoreNodeYield;
    public bool useParentSize;
    public bool useParentQuality;
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord speed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord costMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord growthSizeMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord refundRating = new ValueModifierRecord(new BigDouble(0.0, 0));

    // What the action's author decided it costs: on what, on leaving which phase, and out of which
    // resources. The suite reads the two enums and the length of the drain, never the drain itself.
    public CostType elementCostType;
    public PlotNodePhases elementCostExitPhase;
    public ResourceCostList actionDrain = new ResourceCostList();

    // Which other nodes the action takes its size multiplier from, and the prerequisites that gate
    // it. Collection reads the list's length and latch; the Auto Harvest action boundary calls the
    // exact container Check() after current UUID/type/lifecycle resolution; the native action-row
    // visibility method reaches that same Check() again.
    public List<PlotNodeSO> sizeModNodes = new List<PlotNodeSO>();
    public Prerequisites.Container prerequisites = new Prerequisites.Container();

    /// <summary>What the action applies for as long as it runs, as against on completing.</summary>
    public List<PersistentEffectBlock> actionEffects = new List<PersistentEffectBlock>();

    /// <summary>What completing one run of the action applies.</summary>
    public List<InstantEffectBlock> completeEffects = new List<InstantEffectBlock>();
}

/// <summary>
/// An authored effect block. The suite reads its shape — which class, how many modifiers, how many
/// scripts, and what the one modifier and the one script name — and never applies any of it.
/// </summary>
public class EffectBlock
{
    public Prerequisites.Container prerequisites = new Prerequisites.Container();
    public List<object> effectMods = new List<object>();
    public BigDouble NecessaryDrainRatio = BigDouble.One;

    private BigDouble GetEffectNecessaryDrainRatio() => NecessaryDrainRatio;
}

public class InstantEffectBlock : EffectBlock
{
    public List<IInstantEffectScript> effectScripts = new List<IInstantEffectScript>();
}

public partial class ConsumableSO
{
    public sealed class ConsumableGainEffect : IInstantEffectScript
    {
        public ConsumableSO consumable = new ConsumableSO();
    }
}

public class PersistentEffectBlock : EffectBlock
{
}

/// <summary>The weight a completion effect scales its payout by.</summary>
public sealed class ScalingWeightSO : IdScriptableObject
{
    public static List<ScalingWeightSO> All = new List<ScalingWeightSO>();
}

/// <summary>The game's serialized edge to a scaling weight.</summary>
public sealed class ScalingWeightRef
{
    public ScalingWeightSO? scalingWeight;

    public sealed class Value
    {
        public double baseValue;
    }
}

/// <summary>An effect modifier that scales what it modifies by an authored weight.</summary>
public sealed class ScalingWeightEffectMod
{
    public ScalingWeightRef scalingWeightRef = new ScalingWeightRef();
}

/// <summary>How an effect filters the scalings it applies to.</summary>
public sealed class FilterEffectMod
{
    public enum FilterType
    {
        BlackList,
        WhiteList,
    }

    public FilterType listType;
    public List<object> listContents = new List<object>();
}


public sealed class PassiveAbilitySO : IdScriptableObject
{
    public static List<PassiveAbilitySO> All = new List<PassiveAbilitySO>();
    public List<PassiveAbilityTypeSO> passiveTypes = new List<PassiveAbilityTypeSO>();
    public bool muted;
    public bool touched;
    public bool hidden;
    public bool silent;
    public bool global;
    public bool startOnCooldown;
    public bool ignoreReactionCooldown;
    public double reactionTokenCost;
    public double maxTokens;
    public double minTokenForEffect;
    public bool tokenIndividuateDuration;
    public bool applyWhileRecharging;
    public bool expireAttachedStatusEffect;
    public ValueModifierRecord tokenRate = new ValueModifierRecord(new BigDouble(0.0, 0));
}


public sealed class CharacterSO : IdScriptableObject
{
    public static List<CharacterSO> All = new List<CharacterSO>();
    public List<CharacterTypeSO> characterTypes = new List<CharacterTypeSO>();
    public bool discovered;

    /// <summary>A double in the game too: the count runs past what an int holds.</summary>
    public double numberSlain;
    public bool floats;
}

public interface IHasGuid
{
    Guid GetGuid();
}

public interface IDiscoverable : IHasGuid
{
    List<GlyphSO> GetGlyphRecipe();
    List<ResourceSO> GetResourceRecipe();
    ResourceCostList GetDiscoverCost();
    bool IsDiscoverVisible();
    bool CanDiscover();
    bool IsDiscovered();
    bool IsDiscoverRequired();
    void Discover();
}

public sealed class DiscoveryTestItemSO : IdScriptableObject, IDiscoverable
{
    public ResourceCostList discoverCost = new ResourceCostList();
    public bool discoverVisible = true;
    public bool canDiscover = true;
    public bool discovered;
    public bool required;
    public bool suppressDiscover;
    public bool throwBeforeDiscover;
    public bool throwAfterDiscover;
    public int discoverCalls;
    public List<GlyphSO> glyphRecipe = new List<GlyphSO>();
    public List<ResourceSO> resourceRecipe = new List<ResourceSO>();

    List<GlyphSO> IDiscoverable.GetGlyphRecipe() => new List<GlyphSO>(glyphRecipe);
    List<ResourceSO> IDiscoverable.GetResourceRecipe() => new List<ResourceSO>(resourceRecipe);
    ResourceCostList IDiscoverable.GetDiscoverCost() => discoverCost;
    bool IDiscoverable.IsDiscoverVisible() => discoverVisible;
    bool IDiscoverable.CanDiscover() => canDiscover;
    bool IDiscoverable.IsDiscovered() => discovered;
    bool IDiscoverable.IsDiscoverRequired() => required;
    Guid IHasGuid.GetGuid() => GetGuid();

    void IDiscoverable.Discover()
    {
        discoverCalls++;
        if (throwBeforeDiscover)
            throw new InvalidOperationException("injected failure before discovery");
        if (!suppressDiscover) discovered = true;
        if (throwAfterDiscover)
            throw new InvalidOperationException("injected failure after discovery");
    }
}

public sealed class DiscoveryTreeSO : IdScriptableObject
{
    public enum DiscoveryTreeModes
    {
        Idle,
        Crafting,
        Choice,
    }

    public static List<DiscoveryTreeSO> All = new List<DiscoveryTreeSO>();
    public DiscoveryTreeModes actionMode;
    public BigDouble actionTime;
    public int rerollsLeft;
    public bool usedRerollsLastDiscover;
    public List<GuidContainer> currentChoiceIds = new List<GuidContainer>();
    public List<GuidContainer> nextExcludedIds = new List<GuidContainer>();

    /// <summary>An identity the game already holds as one, rather than a live reference.</summary>
    public GuidContainer selectedChoiceId = new GuidContainer();

    public IntVariable overrideDiscoveryRerolls;
    public IntVariable overrideDiscoveryChoices;
    public int additionalDiscoveryChoices;
    public int discoveryBonusLevelCost;
    public bool debugMode;
    private int totalDiscoveredCount;
    private int poolDiscoveredCount;
    private bool hasRequiredDiscovery;
    private bool hasRemainingDiscovery = true;
    private bool hasCompletedAllDiscoveries;

    public ResourceCostList nextItemCost = new ResourceCostList();
    public List<IDiscoverable> allDiscoverableItems = new List<IDiscoverable>();
    public List<IDiscoverable> mainDiscoverableItemPool = new List<IDiscoverable>();
    public int maximumRerolls = 1;
    public bool visible = true;
    public bool immediateRequired;
    public bool suppressInitiate;
    public bool suppressSelect;
    public bool suppressConfirm;
    public bool suppressReroll;
    public bool throwAfterInitiate;
    public bool throwAfterSelect;
    public bool throwAfterConfirmReset;
    public bool throwAfterReroll;
    public bool throwAfterSelectionClear;
    public bool driftInitiateEvidence;
    public bool driftSelectEvidence;
    public bool driftConfirmEvidence;
    public bool driftRerollEvidence;
    public int initiateCalls;
    public int selectCalls;
    public int confirmCalls;
    public int rerollCalls;

    public bool IsVisible() => visible;
    public bool IsInIdleMode() => actionMode == DiscoveryTreeModes.Idle;
    public bool IsInCraftingMode() => actionMode == DiscoveryTreeModes.Crafting;
    public bool IsInChoiceMode() => actionMode == DiscoveryTreeModes.Choice;
    public bool HasCurrentlyRemMainPoolDiscoveries() => hasRemainingDiscovery;
    public bool HasImmediateRequiredDiscover() => immediateRequired;
    public int GetCurrentRerolls() => rerollsLeft;
    public int GetMaxRerolls() => maximumRerolls;
    public ResourceCostList GetNextItemCost() => nextItemCost;

    public IDiscoverable? GetItemFromGuid(Guid guid)
    {
        for (var index = 0; index < allDiscoverableItems.Count; index++)
            if (allDiscoverableItems[index].GetGuid() == guid) return allDiscoverableItems[index];
        return null;
    }

    public void InitiateCraftingMode()
    {
        initiateCalls++;
        if (!suppressInitiate)
        {
            if (!usedRerollsLastDiscover)
                rerollsLeft = Math.Min(rerollsLeft + 1, maximumRerolls);
            usedRerollsLastDiscover = false;
            actionMode = DiscoveryTreeModes.Crafting;
            actionTime = BigDouble.Zero;
            if (driftInitiateEvidence)
            {
                actionTime = new BigDouble(7, 0);
                rerollsLeft = 99;
                usedRerollsLastDiscover = true;
                currentChoiceIds.Add(new GuidContainer(Guid.NewGuid()));
                totalDiscoveredCount += 3;
                poolDiscoveredCount += 2;
            }
        }
        if (throwAfterInitiate)
            throw new InvalidOperationException("injected failure after initiate");
    }

    public void SelectItemId(Guid guid)
    {
        selectCalls++;
        if (!suppressSelect)
        {
            selectedChoiceId = new GuidContainer(guid);
            if (driftSelectEvidence)
            {
                actionMode = DiscoveryTreeModes.Idle;
                actionTime = new BigDouble(8, 0);
                currentChoiceIds.Clear();
                nextExcludedIds.Add(new GuidContainer(Guid.NewGuid()));
                rerollsLeft += 4;
                usedRerollsLastDiscover = !usedRerollsLastDiscover;
                totalDiscoveredCount += 2;
                poolDiscoveredCount += 1;
            }
        }
        if (throwAfterSelect || (guid == Guid.Empty && throwAfterSelectionClear))
            throw new InvalidOperationException("injected failure after selection");
    }

    public void DiscoverSelectedItem()
    {
        confirmCalls++;
        var selected = selectedChoiceId.guid;
        if (selected == Guid.Empty) return;
        var item = GetItemFromGuid(selected);
        if (item is null) return;
        if (!suppressConfirm)
        {
            totalDiscoveredCount++;
            if (!item.IsDiscoverRequired()) poolDiscoveredCount++;
            actionMode = DiscoveryTreeModes.Idle;
            actionTime = BigDouble.Zero;
            currentChoiceIds.Clear();
            selectedChoiceId = new GuidContainer();
        }
        if (throwAfterConfirmReset)
            throw new InvalidOperationException("injected failure after confirm reset");
        item.Discover();
        if (driftConfirmEvidence)
        {
            actionMode = DiscoveryTreeModes.Choice;
            actionTime = new BigDouble(9, 0);
            currentChoiceIds.Add(new GuidContainer(selected));
            selectedChoiceId = new GuidContainer(selected);
            totalDiscoveredCount += 4;
            poolDiscoveredCount += 3;
            rerollsLeft += 2;
            usedRerollsLastDiscover = !usedRerollsLastDiscover;
        }
    }

    public void RerollChoices()
    {
        rerollCalls++;
        if (actionMode != DiscoveryTreeModes.Choice || rerollsLeft <= 0) return;
        if (!suppressReroll)
        {
            nextExcludedIds = new List<GuidContainer>(currentChoiceIds);
            currentChoiceIds = new List<GuidContainer>();
            rerollsLeft--;
            usedRerollsLastDiscover = true;
            actionMode = DiscoveryTreeModes.Crafting;
            actionTime = BigDouble.Zero;
            if (driftRerollEvidence)
            {
                actionTime = new BigDouble(10, 0);
                rerollsLeft += 5;
                usedRerollsLastDiscover = false;
                currentChoiceIds.Add(new GuidContainer(Guid.NewGuid()));
                nextExcludedIds.Clear();
                totalDiscoveredCount += 2;
                poolDiscoveredCount += 1;
            }
        }
        if (throwAfterReroll)
            throw new InvalidOperationException("injected failure after reroll");
    }
}

public sealed class RecipeBookSO : IdScriptableObject
{
    public static List<RecipeBookSO> All = new List<RecipeBookSO>();
    public bool available;

    public bool IsAvailable() => available;
}

public sealed class PlotNodeSO : IdScriptableObject
{
    public static List<PlotNodeSO> All = new List<PlotNodeSO>();
    public List<PlotNodeTypeSO> nodeTypes = new List<PlotNodeTypeSO>();
    public bool visible;
    public BigDouble currentTime;
    public BigDouble nextErraticTime;
    public BigDouble sizeLevel;
    public BigDouble masteryXp;
    public int masteryLevel;
    public bool noMastery;
    public bool noSizeDisplay;
    public bool useVisibilityPrereq;
    public bool hasErraticGrowth;
    public bool debugMode;
    public int erraticQuantity;
    public ValueModifierRecord actionQuantityUsageMain = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord actionQuantityUsageAny = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord actionXpRate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord yieldMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord specialMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord actionSpeed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord actionCostMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord growingSpeed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord restingSpeed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord sizeMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord qualityMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord recoverySizeMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord naturalGrowth = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord naturalGrowthPower = new ValueModifierRecord(new BigDouble(0.0, 0));
    private int lastQuantity;

    /// <summary>The action the node runs by itself, which most nodes do not author.</summary>
    public PlotNodeActionSO autoAction;

    // The phases the node authors and the instances holding how many are in each. The game reaches
    // these through GetPhaseInstance(), which lazily creates a missing instance — a write — so both
    // lists are shaped as the game shapes them and read directly.
    public List<PlotNodePhaseInfo> phaseInfos = new List<PlotNodePhaseInfo>
    {
        new PlotNodePhaseInfo { phase = PlotNodePhases.Idle },
    };

    public List<PlotNodePhaseInstance> phaseInstances = new List<PlotNodePhaseInstance>();

    // The actions this node offers and the runtime instances of them it currently holds. Both are
    // walked directly: the game's GetActionInstances() is a plain getter, but the pairing question
    // needs the list either way.
    public List<PlotNodeActionSO> availableActions = new List<PlotNodeActionSO>();
    private List<PlotNodeActionInstance> actionInstances = new List<PlotNodeActionInstance>();

    /// <summary>
    /// The two answers the differential pass compares its ports against, held as fixture values.
    /// </summary>
    /// <remarks>
    /// Deliberately not recomputed here. A stub that evaluated the same expression as the port would
    /// agree with it by construction, and the pass would report a clean run for arithmetic nobody
    /// checked. What offline can prove is the wiring — which members are bound, which inputs are fed
    /// into the port, and that a disagreement surfaces as one.
    /// </remarks>
    public int RemainingQuantityAnswer;

    public int RemainingTotalQuantityAnswer;

    public bool IsVisible() => visible;

    public int GetQuantity(PlotNodePhases phase)
    {
        var total = 0;
        foreach (var instance in phaseInstances)
            if (instance.phase == phase) total += instance.timers.q;
        return total;
    }

    public int GetTotalQuantity()
    {
        var total = 0;
        foreach (var instance in phaseInstances) total += instance.timers.q;
        return total;
    }

    public int GetRemainingQuantity() => RemainingQuantityAnswer;

    public int GetRemainingTotalQuantity() => RemainingTotalQuantityAnswer;

    public List<PlotNodeActionInstance> GetActionInstances() => actionInstances;

    public PlotNodeActionInstance AddAction(PlotNodeActionSO action)
    {
        availableActions.Add(action);
        var instance = new PlotNodeActionInstance(this, action);
        actionInstances.Add(instance);
        return instance;
    }
}

/// <summary>
/// One running action on a plot. Its identity travels as the serialized reference the game holds,
/// which is a struct with a memoised guid beside the string it was parsed from.
/// </summary>
public sealed class PlotNodeActionInstance
{
    public IdObjectRef refObj;
    public int quantity;
    public PlotNodeSO? plotNodeRefObj;
    private PlotNodeActionSO? action;
    private bool engaged;
    public bool EnoughForOneInstance = true;
    public int MaximumInstances = int.MaxValue;
    public int MaximumRemainingInstances = int.MaxValue;

    public PlotNodeActionInstance()
    {
    }

    public PlotNodeActionInstance(PlotNodeActionSO action)
    {
        refObj = new IdObjectRef(action);
        this.action = action;
    }

    public PlotNodeActionInstance(PlotNodeSO plot, PlotNodeActionSO action)
        : this(action) => plotNodeRefObj = plot;

    public bool IsEmpty() => quantity <= 0;

    public bool IsEngaged() => engaged;

    public int GetActualQuantity() => quantity;

    public PlotNodeSO? GetElement() => plotNodeRefObj;

    public PlotNodeActionSO? GetAction() => action;

    /// <summary>The native row asks the exact action prerequisite container again.</summary>
    public bool IsVisible() => action?.prerequisites.Check() == true;

    public bool HasEnoughForOneInstance() => EnoughForOneInstance;

    public int GetMaximumRemInstances() => MaximumRemainingInstances;

    public int GetMaximumInstances() => MaximumInstances;

    public bool IsAtMinimumQuantity() => quantity == 1;

    public void Cancel()
    {
        quantity = 0;
        engaged = false;
    }

    /// <summary>
    /// Models the shipped existing-row path: it clamps against the absolute maximum, not the
    /// remaining maximum, and performs no affordability check.
    /// </summary>
    public void PlayerChangeInstanceQuantity(int change) =>
        quantity = Math.Max(1, Math.Min(quantity + change, GetMaximumInstances()));

    public void Engage() => engaged = true;
}

public struct IdObjectRef
{
    public string idStr;
    private Guid _guid;

    public IdObjectRef(PlotNodeActionSO action)
    {
        idStr = action.GetGuid().ToString();
        _guid = action.GetGuid();
    }
}

/// <summary>The game nests this inside PlotNodeSO; the suite reads it by name and not by nesting.</summary>
public enum PlotNodePhases
{
    Idle,
    Growing,
    Resting,
}

public sealed class PlotNodePhaseInfo
{
    public PlotNodePhases phase;
    public double phaseTime;
    public TimerList.TimerType processType;
    public PlotNodePhases exitPhase;
}

public sealed class PlotNodePhaseInstance
{
    public PlotNodePhases phase;
    public TimerList timers = new TimerList();

    public PlotNodePhaseInstance()
    {
    }

    public PlotNodePhaseInstance(PlotNodePhases phaseN, int quantity)
    {
        phase = phaseN;
        timers.q = quantity;
    }
}

/// <summary>
/// The game's timer bag. Only the count matters to collection, and the game holds it in a plain
/// field that GetCount() returns unchanged.
/// </summary>
public sealed class TimerList
{
    /// <summary>How a phase's timers run. The game nests this here; the suite reads its integer.</summary>
    public enum TimerType
    {
        Single,
        Parallel,
    }

    public int q;
    public List<BigDouble> ds = new List<BigDouble>();

    public int GetCount() => q;
}

public sealed class TreasurePoolSO : IdScriptableObject
{
    /// <summary>
    /// What one completed run draws out of a pool. The game nests this inside the pool type, which is
    /// why a consumer that wants to know it is a treasure payout compares the class name.
    /// </summary>
    public sealed class TreasurePoolInstantEffect : IInstantEffectScript
    {
        public TreasurePoolSO? treasurePool;
        public string effectType = string.Empty;
        public double effectValue;
        public FilterEffectMod filterScaling = new FilterEffectMod();
    }

    public static List<TreasurePoolSO> All = new List<TreasurePoolSO>();
    public int treasuresFound;
    public BigDouble partialTreasureReward;
    public bool forceLevel;
    private int treasureLevel;
    private bool calculatedTreasureLevel;
}

public sealed class CraftingStructureSO : TooltipableObject
{
    public sealed class TypeElement
    {
        private readonly ITooltipable? value;
        public TypeElement(ITooltipable? value = null) => this.value = value;
        public ITooltipable? GetTooltipable() => value;
        public bool Available { get; set; } = true;
        public bool IsAvailable() => Available;
    }

    public sealed class TypeListElement
    {
        public List<TypeElement> elements = new List<TypeElement>();
        public List<TypeElement> GetElements() => elements;
    }

    public sealed class Recipe
    {
        public GuidContainer guidContainer = new GuidContainer(Guid.NewGuid());
        public List<TypeElement> ingredients = new List<TypeElement>();
        public TypeElement output = new TypeElement();
        public Guid GetGuid() => guidContainer.guid;
        public TypeElement GetOutput() => output;
    }

    public static List<CraftingStructureSO> All = new List<CraftingStructureSO>();
    public CraftingStructureListVariable instances = new CraftingStructureListVariable();
    public List<TypeListElement> ingredientLists = new List<TypeListElement>();
    public List<Recipe> recipes = new List<Recipe>();
}

public sealed class CraftingStructureListVariable
{
    public List<CraftingStructure> value = new List<CraftingStructure>();

    public List<CraftingStructure> GetAll() => value;
}

public sealed class CraftingStructure
{
    public GuidContainer guidContainer = new GuidContainer(Guid.NewGuid());
    public CraftingStructureSO referenceObj;
    public GuidContainer recipeId = new GuidContainer(Guid.Empty);
    public bool active;
    public int selectedLevel = 1;
    private bool isLoaded;
    private readonly List<CraftingStructureSO.TypeElement?> ingredients =
        new List<CraftingStructureSO.TypeElement?> { null, null };
    private ResourceCostList resourceDrain = new ResourceCostList();
    public bool SuppressMutation { get; set; }

    public CraftingStructure(CraftingStructureSO reference)
    {
        referenceObj = reference;
        reference.instances.value.Add(this);
    }

    public CraftingStructureSO get_reference() => referenceObj;
    public Guid GetGuid() => guidContainer.guid;
    public CraftingStructureSO.TypeElement? GetIngredient(int index) => ingredients[index];
    public CraftingStructureSO.TypeElement? GetOutput()
    {
        for (var index = 0; index < referenceObj.recipes.Count; index++)
            if (referenceObj.recipes[index].GetGuid() == recipeId.guid)
                return referenceObj.recipes[index].GetOutput();
        return null;
    }
    public List<CraftingStructureSO.TypeElement> GetOutputList()
    {
        var result = new List<CraftingStructureSO.TypeElement>();
        for (var index = 0; index < referenceObj.recipes.Count; index++)
            result.Add(referenceObj.recipes[index].GetOutput());
        return result;
    }
    public bool IsOutputVisible(CraftingStructureSO.TypeElement element) => element.IsAvailable();
    public bool IsLoaded() => isLoaded;
    public bool IsActive() => active;
    public int GetLevel() => selectedLevel;
    public int GetMinSelectedLevel() => 1;
    public int GetMaxSelectedLevel() => 10;
    public ResourceCostList GetCurrentDrain() => resourceDrain;

    public void SetIngredient(int index, CraftingStructureSO.TypeElement element)
    {
        if (SuppressMutation) return;
        ingredients[index] = element;
        MatchRecipe();
    }

    public void SetOutput(CraftingStructureSO.TypeElement element)
    {
        if (SuppressMutation) return;
        for (var index = 0; index < referenceObj.recipes.Count; index++)
        {
            var recipe = referenceObj.recipes[index];
            if (!ReferenceEquals(recipe.GetOutput().GetTooltipable(), element.GetTooltipable())) continue;
            ingredients[0] = recipe.ingredients.Count > 0 ? recipe.ingredients[0] : null;
            ingredients[1] = recipe.ingredients.Count > 1 ? recipe.ingredients[1] : null;
            recipeId = new GuidContainer(recipe.GetGuid());
            isLoaded = true;
            active = false;
            return;
        }
    }

    public void SetSelectedLevel(int level)
    {
        if (SuppressMutation) return;
        selectedLevel = Math.Max(GetMinSelectedLevel(), Math.Min(level, GetMaxSelectedLevel()));
        active = false;
    }

    public void SetActive(bool value)
    {
        if (!SuppressMutation && (!value || isLoaded)) active = value;
    }

    public void SetDrain(ResourceCostList drain) => resourceDrain = drain;

    private void MatchRecipe()
    {
        isLoaded = false;
        recipeId = new GuidContainer(Guid.Empty);
        for (var index = 0; index < referenceObj.recipes.Count; index++)
        {
            var recipe = referenceObj.recipes[index];
            if (recipe.ingredients.Count != ingredients.Count) continue;
            var matches = true;
            for (var ingredientIndex = 0; ingredientIndex < ingredients.Count; ingredientIndex++)
                matches &= ReferenceEquals(
                    recipe.ingredients[ingredientIndex].GetTooltipable(),
                    ingredients[ingredientIndex]?.GetTooltipable());
            if (!matches) continue;
            recipeId = new GuidContainer(recipe.GetGuid());
            isLoaded = true;
            return;
        }
    }
}
