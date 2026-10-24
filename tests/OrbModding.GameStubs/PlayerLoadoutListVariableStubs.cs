using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class PlayerLoadoutListVariable : AbstractListVariable<PlayerLoadout>
{
    public List<PlayerLoadout> GetAll() => new List<PlayerLoadout>();
    public PlayerLoadout? GetActiveLoadout() => value.FirstOrDefault(loadout => loadout.isSelected);
}
