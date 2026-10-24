using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class ViewSO : IdScriptableObject
{
    public static List<ViewSO> All = new List<ViewSO>();
    public bool available;
    public bool active;
    public bool IsAvailable() => available;
    public bool alwaysActive;
    public DiscoveryTreeSO? discoveryTree;
    public ViewListVariable? subViews;
    public List<AbstractListVariable> relevantLists = new List<AbstractListVariable>();
    public List<AbstractListVariable> availableLists = new List<AbstractListVariable>();
}
