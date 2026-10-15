using Xunit;

namespace OrbModding.Tests;

/// <summary>
/// The classes that point <c>GameActionFaultLog</c>'s process-global sink at their own list.
/// </summary>
/// <remarks>
/// Each class puts the sink back to <see langword="null"/> when it is done, so one class finishing
/// while another reads its fault references drops them, and a fault another class raises lands in
/// the wrong list. Running these classes alone and one at a time keeps each class's sink its own.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GameActionFaultLogCollection
{
    public const string Name = "Game action fault log sink";
}
