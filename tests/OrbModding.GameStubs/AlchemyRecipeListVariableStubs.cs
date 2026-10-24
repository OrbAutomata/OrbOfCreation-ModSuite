using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class AlchemyRecipeListVariable : AbstractListVariable<AlchemyRecipeSO>
{
    public AlchemyRecipeListVariable() { }
    public AlchemyRecipeListVariable(List<AlchemyRecipeSO> entries) => value = entries;
}
