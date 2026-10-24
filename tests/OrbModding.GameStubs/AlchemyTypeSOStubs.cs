using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class AlchemyTypeSO : StringIdScriptableObject
{
    public static List<AlchemyTypeSO> All = new List<AlchemyTypeSO>();

    // The type's level is a modifier record rather than a persisted integer, which is why world
    // collection reads it through the record's cached field.
    public ValueModifierRecord level = new ValueModifierRecord(new BigDouble(0.0, 0));

    /// <summary>The chosen level, held in the shared variable registry rather than on the type.</summary>
    public IntVariable selectedLevel = null!;

    // Composed records: no cached value of their own, so collection counts their active set.
    public bool maxUsageByMastery;
    public ModifierRecord power = new ModifierRecord();
    public ModifierRecord speed = new ModifierRecord();
    public ModifierRecord special = new ModifierRecord();
    public ModifierRecord drainCostMod = new ModifierRecord();
    public ModifierRecord experienceRate = new ModifierRecord();
    public ModifierRecord overdrivePower = new ModifierRecord();
    public ModifierRecord overdriveSpeed = new ModifierRecord();
    public ModifierRecord overdriveDrainCostMod = new ModifierRecord();
    public ModifierRecord overdriveXpRate = new ModifierRecord();
    public ModifierRecord timeReqMod = new ModifierRecord();
    public ModifierRecord timeScalingMod = new ModifierRecord();
    public ModifierRecord freeUsageSlots = new ModifierRecord();
    public ModifierRecord effectLevels = new ModifierRecord();
    public ValueModifier reqCostPenalty;
    public ValueModifier reqSpeedPenalty;

    public AlchemyTypeSO() { }

    public AlchemyTypeSO(string uuid) => this.uuid = uuid;
}
