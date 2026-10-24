using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class UpgradeSO : IdScriptableObject, IActionable
{
    public static List<UpgradeSO> All = new List<UpgradeSO>();
    private string stableUuid;

    public UpgradeSO() => stableUuid = base.GetGuid().ToString("D");
    public new string uuid
    {
        get => stableUuid;
        set
        {
            stableUuid = value;
            if (Guid.TryParse(value, out var guid)) base.SetGuid(guid);
        }
    }
    // The game stores one owned level and one in-flight count; every level question it answers is
    // derived from those two and maxLevel. Storing the answers independently let a fixture describe
    // an upgrade the game could never produce, which the suite then read straight out of the fields.
    public int level;
    public int queuedLevels;
    public int maxLevel;
    private int cachedCostLevel = -1;
    public bool available = true;
    public bool purchasable = true;
    public ResourceCostList purchaseCost = new ResourceCostList();

    /// <summary>What one level of this upgrade permanently applies.</summary>
    public PersistentEffectDeprecated permanentEffects = new PersistentEffectDeprecated();

    // The authored cost and the list it grows by per level, which together are what the suite
    // computes GetPurchaseCost() from instead of calling it. An upgrade prices on an entirely
    // different chain than a structure, so it names entirely different fields.
    public ResourceCostList resourceCost = new ResourceCostList();
    public ModifierListRef resourceCostModPerLevel = new ModifierListRef();

    // The gate on the specific level being bought, as distinct from the whole-upgrade gate. The game
    // checks it as prerequisitesPerLevel.Check(level + queuedLevels + 1), which takes a level and so
    // cannot be a latched boolean the way `available` is.
    public Prerequisites.Container prerequisitesPerLevel = new Prerequisites.Container();

    // The whole-upgrade gate, as distinct from the gate on the level being bought. IsAvailable() is
    // this container's no-argument Check() below the level cap, and that call is what leaves
    // `available` latched — which is why the stub's IsAvailable() answers from the latch.
    public Prerequisites.Container prerequisites = new Prerequisites.Container();
    public List<ViewListVariable.ListTuple> viewListAdditions = new List<ViewListVariable.ListTuple>();
    public BigDouble buildTime;
    public double developmentTime = 5.0;
    public string GetName() => "Upgrade";
    public bool IsAvailable() => available;
    public bool CanPurchase() =>
        !IsMaxQueuedLevel() &&
        purchaseCost.HasEnough() &&
        IsAvailable() &&
        HasMetQueuedLevelRequirements() &&
        ActionManager.CanLoadAction(this);
    public ResourceCostList GetPurchaseCost() => purchaseCost;
    public int GetPurchaseLevel() => level;
    public int GetQueuedPurchaseLevel() => level + queuedLevels;
    public bool HasFiniteLevels() => maxLevel > 0;

    /// <summary>
    /// The curve accessor, modelled by its two observable behaviours: which level a given offset
    /// resolves to, and that the answer is memoised against that level.
    /// </summary>
    /// <remarks>
    /// The prices themselves come from <see cref="LeveledCosts"/> rather than from a reimplementation
    /// of <c>SetToLevel</c>. Recomputing them here would put the arithmetic under test on both sides
    /// of the differential comparison, which is the one thing that comparison exists to avoid. What a
    /// fixture can pin is the clamp, the level asked for, and where the cache is left afterwards.
    /// </remarks>
    public readonly List<int> LeveledCostLevelsAsked = new List<int>();
    public readonly Dictionary<int, ResourceCostList> LeveledCosts = new Dictionary<int, ResourceCostList>();
    public int CachedCostLevel => cachedCostLevel;

    public ResourceCostList GetLeveledCostList(int addedLevels)
    {
        var priced = Math.Min(
            level + queuedLevels + addedLevels,
            HasFiniteLevels() ? maxLevel - 1 : int.MaxValue);
        LeveledCostLevelsAsked.Add(priced);
        cachedCostLevel = priced;
        return LeveledCosts.TryGetValue(priced, out var list) ? list : resourceCost;
    }
    public bool IsMaxLevel() => HasFiniteLevels() && level >= maxLevel;
    public bool IsMaxQueuedLevel() => HasFiniteLevels() && level + queuedLevels >= maxLevel;
    public bool HasMetQueuedLevelRequirements() =>
        prerequisitesPerLevel.Check(new Requirements.ConditionInfo(level + queuedLevels + 1));
    public void Purchase()
    {
        // The real upgrade Purchase() honours the global multi-buy multiplier, buying up to that
        // many levels in a single call, each still bounded by CanPurchase() (so a finite maxLevel
        // yields a partial "bought X of N" purchase). A multiplier of 1 keeps the single-level path.
        var target = GlobalVariables.MultiBuy?.Value ?? 1;
        if (target < 1) target = 1;
        for (var bought = 0; bought < target && CanPurchase(); bought++)
        {
            queuedLevels++;
            purchaseCost.PerformCost();
        }
    }
    public void CompleteAction()
    {
        if (queuedLevels <= 0) return;
        queuedLevels--;
        level++;
    }
}
