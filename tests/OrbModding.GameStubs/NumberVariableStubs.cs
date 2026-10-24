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
    public Guid uuid = Guid.NewGuid();

    public Guid GetGuid() => uuid;

    /// <summary>One effect's standing modification of one variable.</summary>
    public sealed class PersistentEffect : IPersistentEffectScript
    {
        public NumberVariable? numberVariable;
        public ValueModifier modifier;
    }
}
