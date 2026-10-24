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
    }



}
