using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// The base every entity an effect can point at derives from, modelled only as far as the reference
/// edge reads it: the identity a published row carries instead of the object.
/// </summary>
public abstract class UpgradeableObject : TooltipableObject
{
    public new string uuid { get => StringUuid; set => StringUuid = value; }


    /// <summary>One effect's modification of one named property of one upgradeable object.</summary>
    /// <remarks>
    /// The property is a string here because it is a string in the game: the names come from each
    /// type's authored property record rather than from a shared enum, so two types can name
    /// different sets and the same name can mean the same thing across them.
    /// </remarks>
    public sealed class UpgradeEffectModifier : IPersistentEffectScript
    {
        public UpgradeableObject? upgradeableObject;
        public string propertyType = string.Empty;
        public ValueModifier modifier;
        public bool useTargetRef;
        public ModifierRecord? GetRecord() => PropertyRecord(upgradeableObject, propertyType);
    }



    private static ModifierRecord? PropertyRecord(UpgradeableObject? target, string property) => target switch
    {
        HarvestActionTypeSO type => property switch
        {
            "Speed" => type.speed, "Cost" => type.costMod, "GrowthSizeMod" => type.growthSizeMod,
            "Power" => type.power, "RefundRating" => type.refundRating, _ => null,
        },
        PlotNodeActionSO action => property switch
        {
            "Speed" => action.speed, "Cost" => action.costMod, "GrowthSizeMod" => action.growthSizeMod,
            "Power" => action.power, "RefundRating" => action.refundRating, _ => null,
        },
        PlotNodeSO plot => property switch
        {
            "ActionSpeed" => plot.actionSpeed, "ActionCost" => plot.actionCostMod,
            "Yield" => plot.yieldMod, "Special" => plot.specialMod, "Size" => plot.sizeMod,
            "Quality" => plot.qualityMod, _ => null,
        },
        _ => null,
    };

    public class ValueExtractor
    {
        public UpgradeableObject? upgradeableObject;
        public string propertyType = string.Empty;
        public BigDouble plainValue = new BigDouble(100);
        public ModifierRecord? GetRecord() => PropertyRecord(upgradeableObject, propertyType);
        public BigDouble GetValue() => plainValue;
    }
    public sealed class ValueTransmuter : ValueExtractor
    {
        public enum ExtractionType { Percentage, PercentModifier, PercentList, Raw, RawModifier, RawList }
        public ExtractionType extractionType;
        public bool invertExtraction;
        public ValueModifier extractionMod;
        public ModifierListRef extractionModList = new ModifierListRef();
    }
    public sealed class ValueTransmuterList
    {
        public List<ValueTransmuter> transmuters = new List<ValueTransmuter>();
    }
    public sealed class ValueTransmuterMap : ScalingMap<ValueTransmuterList> { }

}
