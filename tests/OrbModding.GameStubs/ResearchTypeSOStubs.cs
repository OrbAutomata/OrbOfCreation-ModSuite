using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class ResearchTypeSO : IdScriptableObject
{
    public static List<ResearchTypeSO> All = new List<ResearchTypeSO>();
    public bool linkedDevelopCost;
    public bool linkedResourceCost;
    public bool linkedResearchTime;
    public bool ignoreWhenLevelDependent;
    public bool persistThroughReset;
    public int cachedTotalLevel;
    public int cachedPeakLevel;
    public int cachedQueuedLevel;
    public int cachedDevelopingLevel;
    public int cachedQueuedValue;
    public int cachedInvestmentLevel;
    public int cachedPurchasedLevel;
    // The pinned build declares this one a ValueModifierRecord: it holds a number of its own
    // rather than distributing one into members. See docs/reverse-engineering/type-model.md.
    public ValueModifierRecord freeBonusLevels = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ModifierRecord levelRequirementAdjust = new ModifierRecord();
    public ValueModifierRecord maxInvestmentLevel = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ModifierRecord maxLevelCap = new ModifierRecord();
    public ModifierRecord power = new ModifierRecord();
    public ValueModifierRecord usedBonusLevels = new ValueModifierRecord(new BigDouble(0.0, 0));
    public int FreeBonusLevels { get; set; }
    public int UsedBonusLevels { get; set; }
    public int CurrentInvestmentLevel { get; set; }
    public int MaximumInvestmentLevel { get; set; }

    public bool HasFreeBonusLevelsLeft() => GetRemainingFreeBonusLevels() > 0;
    public int GetRemainingFreeBonusLevels() => Math.Max(FreeBonusLevels - UsedBonusLevels, 0);
    public int GetCurrentInvestmentLevel() => CurrentInvestmentLevel;
    public int GetMaxInvestmentLevel() => MaximumInvestmentLevel;
    public void UseBonusLevel() => UsedBonusLevels++;
}
