using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public interface IHasRequirements
{
    List<TooltipNode> GetRequirements();
}
