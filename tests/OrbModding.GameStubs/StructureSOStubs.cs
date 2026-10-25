using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class StructureSO : UpgradeableObject, Targeting.ITargetable, IActionable
{
    public static List<StructureSO> All = new List<StructureSO>();
    public StructureTypeSO structureType = new StructureTypeSO();
    public List<StructureTypeSO> structureSubTypes = new List<StructureTypeSO>();
    public EnchantmentSO.EnchantTable enchantTable = new EnchantmentSO.EnchantTable();

    /// <summary>The standing effects the structure applies once built.</summary>
    public List<PersistentEffectDeprecated.Property> structureProperties =
        new List<PersistentEffectDeprecated.Property>();

    public int queuedQuantity;
    public int quantity;
    public bool available = true;
    public bool visible = true;
    public bool purchasable = true;
    public ResourceCostList purchaseCost = new ResourceCostList();

    // The authored cost and the modifier it scales by, which together are what the suite computes
    // GetPurchaseCost() from instead of calling it.
    public ResourceCostList baseCost = new ResourceCostList();
    public ValueModifierRef costPerQuantity = new ValueModifierRef();

    // The structure's own per-level gate, checked at its quantity rather than at a level count.
    public Prerequisites.Container prerequisitesPerLevel = new Prerequisites.Container();

    // The whole-structure gate. IsAvailable() is this container's no-argument Check().
    public Prerequisites.Container prerequisites = new Prerequisites.Container();

    // World collection's reading. Field names mirror the game's.
    public int queuedEchos;
    public int completedEchos;
    public int selfBonusLevels;
    public BigDouble queueTimeLeft;
    private BigDouble currentBuildTime;
    public bool flagged;
    public int baseLevel;
    public float queueTimeTotal = 1f;
    public bool debugStructure;
    public bool disabled;
    private int observableId;
    private bool insufficientReqPenaltyActive;
    private int bufferDevelopedQuantity;
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord powerScaling = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord speed = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord passiveCostMod = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord activeCostMod = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord costScalingMod = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord attributeRankEffectMod = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord drainCostMod = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord bonusLevels = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord effectLevels = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord buildSpeed = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord echoBuildRating = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord powerBuildRating = new ValueModifierRecord(new BigDouble(0.0, 0));
    public bool ApplyPurchaseMutation { get; set; } = true;
    public bool ApplyToggleMutation { get; set; } = true;
    public int ApplyEffectsCalls { get; private set; }
    public int RemoveEffectsCalls { get; private set; }
    public int GetPurchaseCostCalls { get; private set; }
    public bool Available { get => available; set => available = value; }
    public bool Purchasable { get => purchasable; set => purchasable = value; }
    public int PurchaseLevel { get => quantity; set => quantity = value; }
    public int QueuedQuantity { get => queuedQuantity; set => queuedQuantity = value; }
    public ResourceCostList Cost { get => purchaseCost; set => purchaseCost = value; }

    public StructureSO()
    {
        // Portable Auto Buy fixtures normally use one synthetic global list for view ownership. A
        // default structure category points at that same exact registry so the owning-view resolver
        // can prove category membership without each unrelated test hand-authoring a second copy.
        structureType.SetStructuresForTests(All);
    }

    public override string GetName() => "Structure";
    public bool IsAvailable() => available;
    public bool IsVisible() => visible;

    // The shipped StructureSO.CanPurchase() is deliberately thin: it checks only the next-level
    // requirement and ActionManager queue admission. IsAvailable() and affordability are separate
    // player-path terms; Purchase(bool) performs the price check for each submitted level.
    public bool CanPurchase() => HasMetLevelRequirements() && ActionManager.CanLoadAction(this);
    public bool HasMetLevelRequirements() =>
        prerequisitesPerLevel.Check(new Requirements.ConditionInfo(quantity));
    public ResourceCostList GetPurchaseCost()
    {
        GetPurchaseCostCalls++;
        return purchaseCost;
    }
    public BigDouble GetNextCostMod() => new BigDouble(1.0, 2);
    public int GetPurchaseLevel() => quantity;
    public int GetQueuedQuantity() => queuedQuantity;
    public void ToggleDisabled()
    {
        if (!ApplyToggleMutation) return;
        if (disabled) EnableStructure();
        else DisableStructure();
    }
    public void DisableStructure()
    {
        disabled = true;
        RemoveEffects();
    }
    public void EnableStructure()
    {
        disabled = false;
        ApplyEffects();
    }
    public void ApplyEffects() => ApplyEffectsCalls++;
    public void RemoveEffects() => RemoveEffectsCalls++;
    public void Purchase(bool forceOne)
    {
        if (!forceOne || !CanPurchase() || !purchaseCost.HasEnough() || !ApplyPurchaseMutation) return;
        queuedQuantity++;
        // The game charges when a level is queued, not when it completes.
        purchaseCost.PerformCost();
    }
    public void QueueBuild(int amount) => queuedQuantity += amount;
    public void CompleteAction()
    {
        if (queuedQuantity <= 0) return;
        queuedQuantity--;
        quantity++;
    }
    public int Level { get => quantity; set => quantity = value; }
    public int Queued { get => queuedQuantity; set => queuedQuantity = value; }
    public BigDouble CurrentBuildTime { get => currentBuildTime; set => currentBuildTime = value; }
    public int ObservableId { get => observableId; set => observableId = value; }
    public bool InsufficientReqPenaltyActive { get => insufficientReqPenaltyActive; set => insufficientReqPenaltyActive = value; }
    public int BufferDevelopedQuantity { get => bufferDevelopedQuantity; set => bufferDevelopedQuantity = value; }
}
