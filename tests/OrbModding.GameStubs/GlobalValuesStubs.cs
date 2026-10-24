using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class GlobalValues
{
    public static GlobalValues instance = new GlobalValues();
    public ModifierListVariable spellLevelingStandard = new ModifierListVariable();
    public double spellTypeXpPerMastery;
}
