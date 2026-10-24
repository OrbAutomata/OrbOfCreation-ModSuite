using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// Stands in for the game's research entries. Modelled only as far as world collection reads it:
/// identity, level, whether it is developing, and whether it is available.
/// </summary>
public class ResearchSO : ILevelable
{
    public List<PersistentEffectBlock> researchEffectBlocks = new List<PersistentEffectBlock>();
    public static List<ResearchSO> All = new List<ResearchSO>();
    public string uuid = Guid.NewGuid().ToString();
    public int level;
    public int queuedLevels;
    public int researchStage;
    public int selfBonusLevels;
    public int maxLevel = 1;
    public double researchTime = 60.0;
    public bool isDeveloping;
    public bool isActive;
    public bool flagged;
    public bool available = true;
    public List<ResearchTypeSO> researchTypes = new List<ResearchTypeSO>();
    public ResourceCostList researchCost = new ResourceCostList();
    public ResourceFillList resourceFillList = new ResourceFillList();
    public Prerequisites.Container levelPrerequisites = new Prerequisites.Container();

    // The two containers IsVisible() ANDs. `levelVisibilityPrereq` is not authored on its own: the
    // game builds it in Initialize() as levelPrerequisites.Filter().SetAdjustValue(-levelVisibilityRange),
    // which is where the only nonzero threshold adjustment in the game comes from.
    public Prerequisites.Container visibilityPrerequisites = new Prerequisites.Container();

    // Private, as the game declares it: the suite reads it by reflection, and a public stub field
    // would let a binding that cannot see the real one pass here. A test reaches the same instance
    // through the property below.
    private Prerequisites.Container levelVisibilityPrereq = new Prerequisites.Container();

    public Prerequisites.Container LevelVisibilityPrereq => levelVisibilityPrereq;
    public bool hiddenLevel;
    public int levelVisibilityRange = 2;
    public ModifierRecord requirementsAdjust = new ModifierRecord();
    private int requiredStagesCached;
    private BigDouble requiredTimeCached;
    public ValueModifierRecord bonusLevels = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord baseLevels = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord maxLevelCap = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord leewayPoints = new ValueModifierRecord(new BigDouble(0.0, 0));
    public Guid GetGuid() => Guid.Parse(uuid);
    public string GetName() => "Research";
    public bool IsDeveloping() => isDeveloping;
    public bool IsActive() => isActive;
    public bool IsVisible() => available;
    public bool IsAvailable() => IsVisible() && !IsComplete();
    public bool IsComplete() => IsMaxLevel();
    public bool HasMaxLevel() => maxLevel > 0;
    public bool IsMaxLevel() => HasMaxLevel() && GetBaseLevel() >= maxLevel;
    public bool MeetsLevelRequirements() =>
        levelPrerequisites.Check(new Requirements.ConditionInfo(GetRequirementLevel()));
    public bool stillHasLeeway = true;
    public bool belowArtificialMaxLevel = true;
    public bool StillHasLeeway() => stillHasLeeway;
    public bool IsBelowArtificialMaxLevel() => belowArtificialMaxLevel;
    public bool IsBelowMaxInvestmentLevel() => !IsComplete();

    // The game asks the price before the level requirements, and develops on leeway OR on being
    // below both caps, never on all three together.
    public bool IsWithinDevelopRange() =>
        !IsComplete() && GetDevelopmentCost().HasEnough() && MeetsLevelRequirements() &&
        (StillHasLeeway() || (IsBelowArtificialMaxLevel() && IsBelowMaxInvestmentLevel()));
    public bool CanDevelop() => IsWithinDevelopRange() && !IsDeveloping();
    public void PurchaseLevel()
    {
        if (SettingsManager.IsResearchQueueMode()) QueueDevelopment();
        else Develop();
    }
    public void Develop()
    {
        if (SuppressAction) return;
        isActive = true;
        isDeveloping = true;
        if (queuedLevels > 0) queuedLevels--;
        researchCost.PerformUsage(GetGuid());
        if (ThrowAfterAction) throw new InvalidOperationException("injected research action failure");
    }
    public void QueueDevelopment()
    {
        if (SuppressAction) return;
        var limit = Math.Max(GlobalVariables.GetMultiBuy().AsInt(), 0);
        if (maxLevel > 0) limit = Math.Min(limit, Math.Max(maxLevel - level - GetQueuedLevels(), 0));
        var aggregate = new ResourceCostList();
        var accepted = 0;
        for (var index = 0; index < limit; index++)
        {
            var atLevel = level + GetQueuedLevels() + index;
            aggregate.Add(GetDevelopmentCostAtLevel(atLevel + 1));
            if (!aggregate.HasEnough() || !IsWithinDevelopRangeAt(atLevel)) break;
            accepted++;
        }
        if (accepted <= 0) return;
        queuedLevels += accepted;
        if (!isDeveloping) Develop();
        if (ThrowAfterAction) throw new InvalidOperationException("injected research action failure");
    }
    public void CancelDevelopment()
    {
        if (!isDeveloping || SuppressAction) return;
        isActive = false;
        isDeveloping = false;
        queuedLevels = 0;
        resourceFillList.ClearInvestment();
        if (ThrowAfterAction) throw new InvalidOperationException("injected research action failure");
    }
    public void PauseResearch()
    {
        if (!isDeveloping || SuppressAction) return;
        isActive = false;
        if (ThrowAfterAction) throw new InvalidOperationException("injected research action failure");
    }
    public void ResumeResearch()
    {
        if (!isDeveloping || SuppressAction) return;
        isActive = true;
        if (ThrowAfterAction) throw new InvalidOperationException("injected research action failure");
    }
    public void SubmitBonusLevel()
    {
        if (SuppressAction) return;
        selfBonusLevels++;
        if (researchTypes.Count > 0) researchTypes[0].UseBonusLevel();
        if (ThrowAfterAction) throw new InvalidOperationException("injected research action failure");
    }
    public bool SuppressAction { get; set; }
    public bool ThrowAfterAction { get; set; }
    public bool CanApplyBonusLevels() => researchTypes.Any(type => type.HasFreeBonusLevelsLeft());
    public int GetFreeBonusLevelsLeft() => researchTypes.Count == 0
        ? 0
        : researchTypes.Max(type => type.GetRemainingFreeBonusLevels());
    public ResourceCostList GetDevelopmentCost() => researchCost;
    public ResourceCostList GetDevelopmentCostAtLevel(int level) =>
        researchCost.Multiply(new BigDouble(1));
    public bool IsWithinDevelopRangeAt(int level) => IsWithinDevelopRange();
    public int GetQueuedLevels() => queuedLevels + (isDeveloping ? 1 : 0);
    public int GetCurrentInvestmentLevel() => Math.Max(GetQueuedLevels() + level, 0);
    public BigDouble GetCurrentTime() => resourceFillList.GetAverageRatio() * GetRequiredTime();
    public BigDouble GetRemainingTime() => (new BigDouble(1) - resourceFillList.GetLowestRatio()) * GetRequiredTime();
    public BigDouble GetTimeRatio() => GetCurrentTime() / GetRequiredTime();
    public BigDouble GetRequiredTime() => requiredTimeCached == BigDouble.Zero
        ? new BigDouble(researchTime)
        : requiredTimeCached;
    public int GetPurchasedLevels() => level;
    public int GetBaseLevel() => level + baseLevels.GetValue().ToInt();
    public int GetBonusLevels() => bonusLevels.GetValue().ToInt();
    public int GetLevel() => GetBaseLevel() + GetBonusLevels();
    public int GetMaxLevel() => maxLevel;
    public int GetArtificialMaxLevel() => maxLevelCap.GetValue().ToInt();
    public int GetRequirementLevel() => requirementsAdjust.Adjust(new BigDouble(GetBaseLevel())).ToInt();
    public Guid Identity { get => GetGuid(); set => uuid = value.ToString("D"); }
    public bool Available { get => available; set => available = value; }
    public int RequiredStagesCached { get => requiredStagesCached; set => requiredStagesCached = value; }
    public BigDouble RequiredTimeCached { get => requiredTimeCached; set => requiredTimeCached = value; }
}
