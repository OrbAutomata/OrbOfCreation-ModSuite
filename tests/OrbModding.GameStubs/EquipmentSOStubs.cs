using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class EquipmentSO : IdScriptableObject, IDiscoverable
{
    public List<PersistentEffectBlock> equipEffects = new List<PersistentEffectBlock>();
    public static List<EquipmentSO> All = new List<EquipmentSO>();
    private readonly ExperienceContainer experienceContainer = new ExperienceContainer();
    private string stableUuid;

    public EquipmentSO() => stableUuid = base.GetGuid().ToString("D");
    public new string uuid
    {
        get => stableUuid;
        set
        {
            stableUuid = value;
            if (Guid.TryParse(value, out var guid)) base.SetGuid(guid);
        }
    }
    public new string name = "Equipment";
    public int masteryLevel;
    public BigDouble masteryXp;
    public int discRarityLevel;
    public bool isCreated = true;
    public EquipmentTypeSO equipmentType = new EquipmentTypeSO();
    public List<EquipmentTypeSO> subEquipmentTypes = new List<EquipmentTypeSO>();
    public ResourceCostList createCost = new ResourceCostList();
    public List<GlyphSO> glyphRecipe = new List<GlyphSO>();
    public List<ResourceSO> resourceRecipe = new List<ResourceSO>();
    public ResourceCostList usageCost = new ResourceCostList();
    public int NativeMaximumStacks { get; set; } = 4;
    public bool NativeDiscoverVisible { get; set; } = true;
    public bool NativeCanDiscover { get; set; } = true;
    public bool SuppressDiscovery { get; set; }
    public bool ThrowBeforeDiscovery { get; set; }
    public bool ThrowAfterDiscovery { get; set; }
    public bool ThrowOnStateRead { get; set; }
    public int DiscoverCalls { get; private set; }
    public new Guid GetGuid() => Guid.Parse(uuid);
    public new Guid GetId() => GetGuid();
    public string GetName() => name;
    public bool IsCreated() => isCreated;
    public bool IsEquipped() => equippedLevel > 0;
    public int GetMaxLevel() => ThrowOnStateRead
        ? throw new InvalidOperationException("injected equipment state-read failure")
        : NativeMaximumStacks;
    public ResourceCostList GetUsageCost() => usageCost;
    public void Equip(int atLevel)
    {
        if (atLevel == equippedLevel) return;
        equippedLevel = atLevel;
        if (atLevel > 0)
        {
            usageCost.Multiply(new BigDouble(atLevel)).PerformUsage(GetGuid());
            attuningLevel = Math.Min(attuningLevel, atLevel);
            attunementTimeLeft = 1d;
        }
        else
        {
            usageCost.RemoveUsage(GetGuid());
            attuningLevel = 0;
            attunementTimeLeft = -1d;
        }
    }
    public ResourceCostList GetDiscoverCost() => createCost;
    public List<GlyphSO> GetGlyphRecipe() => new List<GlyphSO>(glyphRecipe);
    public List<ResourceSO> GetResourceRecipe() => new List<ResourceSO>(resourceRecipe);
    public bool IsDiscoverVisible() => NativeDiscoverVisible;
    public bool CanDiscover() => NativeCanDiscover;
    public bool IsDiscovered() => isCreated;
    public bool IsDiscoverRequired() => isRequiredDiscovery;
    public void Discover()
    {
        DiscoverCalls++;
        if (ThrowBeforeDiscovery)
            throw new InvalidOperationException("injected failure before discovery");
        if (!SuppressDiscovery) isCreated = true;
        if (ThrowAfterDiscovery)
            throw new InvalidOperationException("injected failure after discovery");
    }
    public IExperienceElement GetExperienceElement() => experienceContainer;
    public void IncrementActive(double deltaTime)
    {
    }
    public void SetMasteryState(int level, BigDouble experience, BigDouble experiencePerLevel)
    {
        masteryLevel = level;
        masteryXp = experience;
        experienceContainer.SetState(level, experience, experiencePerLevel);
    }
    private void GainMasteryLevels(int levels) => masteryLevel += levels;
    public bool isRequiredDiscovery;
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord baseLevel = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord experienceRateMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    private int equippedLevel;
    private int attuningLevel;
    private double attunementTimeLeft;
    private BigDouble baseXpRate;
}
