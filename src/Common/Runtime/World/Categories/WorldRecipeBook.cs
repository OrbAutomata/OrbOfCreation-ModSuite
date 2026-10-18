using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One recipe book and whether its prerequisites currently unlock it.</summary>
internal readonly struct WorldRecipeBook : IWorldEntity
{
    internal WorldRecipeBook(Guid recipeBookId, bool available)
    {
        RecipeBookId = recipeBookId;
        Available = available;
    }

    internal Guid RecipeBookId { get; }

    public Guid EntityId => RecipeBookId;

    /// <summary>The game's own prerequisite answer, not an inference from discovered recipes.</summary>
    internal bool Available { get; }
}

internal sealed class WorldRecipeBookBinder : WorldPlainBinder<WorldRecipeBook>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _available;

    internal override string Category => "recipe books";

    internal override string TypeName => "RecipeBookSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.StableUuid();
        _available = bind.Call<bool>("IsAvailable");
        return bind.Failure;
    }

    internal override WorldRecipeBook Read(object entity) =>
        new(_id!(entity), _available!(entity));
}
