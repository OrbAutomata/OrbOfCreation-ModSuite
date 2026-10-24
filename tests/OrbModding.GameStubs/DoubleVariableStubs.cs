using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>The game's global scalars. Same shape as <see cref="IntVariable"/>; separate registry.</summary>
public sealed class DoubleVariable
{
    public List<PersistentEffectBlock> persistentEffectBlocks = new List<PersistentEffectBlock>();
    public static List<DoubleVariable> All = new List<DoubleVariable>();
    public Guid uuid = Guid.NewGuid();
    public ValueModifierRecord value = new ValueModifierRecord(new BigDouble(0.0, 0));
    public bool isPercentVariable;

    // The two flags that decide the value is a count of seconds, and how finely the game draws it.
    public bool isTimeVariable;
    public bool isTimeAccurateVariable;

    public BigDouble AsPercent() => value.AsPercent();

    public Guid GetGuid() => uuid;
    public Guid Identity { get => GetGuid(); set => uuid = value; }
}
