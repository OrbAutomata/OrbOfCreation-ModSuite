using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// The game's identity wrapper. Modelled only as far as the private field a reference edge reads,
/// spelled as the game spells it.
/// </summary>
public sealed class GuidContainer
{
    private Guid _guid;

    public GuidContainer()
    {
    }

    public GuidContainer(Guid guid) => _guid = guid;

    public Guid guid => _guid;
}
