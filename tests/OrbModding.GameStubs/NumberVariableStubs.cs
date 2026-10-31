using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// The base the game's scalar variables share, modelled only as far as an effect's reference edge
/// reads it: the identity the published row carries instead of the object.
/// </summary>
public class NumberVariable
{
    public List<PersistentEffectBlock> persistentEffectBlocks = new List<PersistentEffectBlock>();
    public ValueModifierRecord value = new ValueModifierRecord(new BigDouble(0));
    public bool isPercentVariable;
    public bool isTimeVariable;
    public bool isTimeAccurateVariable;
    public Guid uuid = Guid.NewGuid();

    public virtual Guid GetGuid() => uuid;

    public float AsFloat() => value.AsBigDouble().ToFloat();

    /// <summary>The game stores the value as a raw modifier on the record; the stub stores it as the memo.</summary>
    public void SetValue(BigDouble val) => value.WithMemo(val);

    /// <summary>One effect's standing modification of one variable.</summary>
    public sealed class PersistentEffect : IPersistentEffectScript
    {
        public NumberVariable? numberVariable;
        public ValueModifier modifier;
    }
    public Guid Identity { get => GetGuid(); set => uuid = value; }
}
