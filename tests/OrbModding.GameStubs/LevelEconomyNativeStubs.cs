using System.Collections.Generic;

/// <summary>Data-only model of the native ordered modifier distributor.</summary>
public sealed class OrderedMultiplierRecord : ModifierRecord
{
    public Dictionary<int, System.Guid> orderGuid = new Dictionary<int, System.Guid>();
    public List<ModifierRecord> records = new List<ModifierRecord>();
}
