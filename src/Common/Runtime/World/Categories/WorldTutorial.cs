using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One tutorial as published: whether it has been completed.</summary>
internal readonly struct WorldTutorial : IWorldEntity
{
    internal WorldTutorial(
        Guid tutorialId,
        bool isCompleted)
    {
        TutorialId = tutorialId;
        IsCompleted = isCompleted;
    }

    internal Guid TutorialId { get; }

    public Guid EntityId => TutorialId;

    internal bool IsCompleted { get; }
}

internal sealed class WorldTutorialBinder : WorldPlainBinder<WorldTutorial>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _isCompleted;

    internal override string Category => "tutorials";

    internal override string TypeName => "TutorialSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.StableUuid();
        _isCompleted = bind.Field<bool>("isCompleted");
        return bind.Failure;
    }

    internal override WorldTutorial Read(object entity) =>
        new(
            _id!(entity),
            _isCompleted!(entity));
}
