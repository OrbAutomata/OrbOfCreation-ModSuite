using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// One authored effect block. Effects hang off this rather than off the entity, so a structure's
/// standing effects are a list of lists.
/// </summary>
public class PersistentEffectDeprecated
{
    public sealed class Property : PersistentEffectDeprecated
    {
    }

    public List<ResourceSO.PersistentEffect> resourceEffects =
        new List<ResourceSO.PersistentEffect>();

    public List<UpgradeableObject.UpgradeEffectModifier> upgradeableObjectEffects =
        new List<UpgradeableObject.UpgradeEffectModifier>();

    public List<TupleMod<NumberVariable>> numberVariableEffects =
        new List<TupleMod<NumberVariable>>();
}
