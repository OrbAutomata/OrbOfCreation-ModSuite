using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class GlobalValues
{
    public static GlobalValues instance = new GlobalValues();
    public ModifierListVariable spellCostStandard = new ModifierListVariable();
    public ValueModifier insufficientReqCostMod;
    public ModifierListVariable spellLevelingStandard = new ModifierListVariable();
    public double spellTypeXpPerMastery;
}
