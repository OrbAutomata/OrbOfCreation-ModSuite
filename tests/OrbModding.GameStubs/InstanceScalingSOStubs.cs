using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class InstanceScalingSO : IdScriptableObject
{
    public static List<InstanceScalingSO> All = new List<InstanceScalingSO>();
    public ScalingConversion instanceScaling = new ScalingConversion();
    public bool useRarity;
    public List<ScalingType> rarityAttributeBlacklist = new List<ScalingType>();
}
