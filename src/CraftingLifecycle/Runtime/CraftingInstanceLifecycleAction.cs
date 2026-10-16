using System;

namespace OrbAutomata;

internal enum CraftingInstanceLifecycleActionKind
{
    Automate = 1,
    CancelManual = 2,
    CancelAutomation = 3,
}

internal readonly struct CraftingInstanceLifecycleAction
{
    internal CraftingInstanceLifecycleAction(
        CraftingInstanceLifecycleActionKind kind,
        Guid recipeId,
        long lifecycleEpoch)
    {
        if (recipeId == Guid.Empty)
            throw new ArgumentException("A crafting recipe identity is required.", nameof(recipeId));
        Kind = kind;
        RecipeId = recipeId;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal CraftingInstanceLifecycleActionKind Kind { get; }
    internal Guid RecipeId { get; }
    internal long LifecycleEpoch { get; }
}
