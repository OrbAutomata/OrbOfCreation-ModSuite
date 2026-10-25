using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class SettingsManager
{
    public static SettingsManager instance = new SettingsManager();

    public BoolVariable enableQueueResearch = new BoolVariable();
    public BoolVariable cancellableSpells = new BoolVariable { value = true };
    public StringVariable numDisplay = new StringVariable { value = "Named" };

    public static bool ResearchQueueMode
    {
        get => instance.enableQueueResearch.GetValue();
        set => instance.enableQueueResearch.SetValue(value);
    }

    public static bool CancellableSpells
    {
        get => instance.cancellableSpells.GetValue();
        set => instance.cancellableSpells.SetValue(value);
    }

    public static bool IsResearchQueueMode() => ResearchQueueMode;
    public static bool CanCancelSpells() => CancellableSpells;
    public static string GetNumberDisplayOption() => instance.numDisplay.GetValue();
}
