using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static class GlobalVariables
{
    private static IntVariable multiBuy = IntVariable.Register(KnownVariableIds.MultiBuy);
    private static readonly StructureTypeSO GlobalStructureType = new StructureTypeSO();
    private static readonly AttributeSO CastingSpeedAttribute = new AttributeSO();
    private static readonly AttributeSO HarvestSpeedAttribute = new AttributeSO();
    private static readonly AttributeSO MasteryExperienceAttribute = new AttributeSO();
    private static readonly AttributeSO MasteryLevelAttribute = new AttributeSO { displayName = "Mastery" };

    // Registered in IntVariable.All under the uuid the game ships, because world collection finds
    // these by identity in the registry rather than through the accessor beside them.
    public static IntVariable MultiBuy
    {
        get => multiBuy;
        set => multiBuy = IntVariable.Replace(multiBuy, value, KnownVariableIds.MultiBuy);
    }

    public static IntVariable GetMultiBuy() => MultiBuy;
    public static StructureTypeSO GetGlobalStructureType() => GlobalStructureType;
    public static AttributeSO GetCastingSpeedAttr() => CastingSpeedAttribute;
    public static AttributeSO GetHarvestSpeedAttr() => HarvestSpeedAttribute;
    public static AttributeSO GetMasteryExpAttr() => MasteryExperienceAttribute;
    public static AttributeSO GetMasteryLevelAttr() => MasteryLevelAttribute;
    public static UITooltipContainer? TooltipPrefab { get; set; }
    public static UITooltipContainer? GetTooltipPrefab() => TooltipPrefab;
    public static List<UnityEngine.Sprite> CustomSprites { get; } = new List<UnityEngine.Sprite>
    {
        new UnityEngine.Sprite(),
        new UnityEngine.Sprite(),
    };
    public static List<UnityEngine.Color> CustomColors { get; } = new List<UnityEngine.Color>
    {
        new UnityEngine.Color(),
        new UnityEngine.Color(),
    };
    public static List<UnityEngine.Sprite> GetCustomSprites() => CustomSprites;
    public static List<UnityEngine.Color> GetCustomColors() => CustomColors;
    public static AudioInstance PowerBuildSfx { get; set; } = new();
    public static AudioInstance GetPowerBuildSfx() => PowerBuildSfx;
}
