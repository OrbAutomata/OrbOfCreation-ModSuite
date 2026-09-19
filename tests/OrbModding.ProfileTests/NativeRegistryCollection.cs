using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// The one collection that owns the game's process-global registries — <c>StructureSO.All</c>,
/// <c>UpgradeSO.All</c>, <c>PlotNodeSO.All</c> and their siblings, plus the singletons beside them
/// (<c>ActionManager.instance</c>, <c>GlobalVariables</c>, <c>Player</c>).
/// </summary>
/// <remarks>
/// A test class joins this collection if it either mutates one of those registries or collects a
/// world from them, because both halves are the same shared list. Adding entities in one class
/// while another enumerates the same list is not a rare interleaving: it is a
/// <c>Collection was modified</c> throw, and clearing the list under a class that just filled it is
/// a missing row with no exception at all. xUnit runs classes in separate collections in parallel,
/// so serialising here is what makes each class's registry state its own.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NativeRegistryCollection
{
    public const string Name = "Native game registries";
}
