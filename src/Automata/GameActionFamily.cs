using OrbModding.Common;

namespace OrbAutomata;

internal sealed class GameActionFamily
{
    internal const string McpOperation = "The current MCP operation";

    internal GameActionFamily(string holder, params AutomationActionFamily[] members)
    {
        Members = members;
        NotHeld = holder + " does not hold " + string.Join(" and ", members) + ".";
    }

    internal AutomationActionFamily[] Members { get; }

    internal string NotHeld { get; }
}

internal sealed class GameActionFamilyHandle
{
    private readonly AutomataActionFamilyOwnership _ownership;
    private readonly GameActionFamily _family;

    internal GameActionFamilyHandle(AutomataActionFamilyOwnership ownership, GameActionFamily family)
    {
        _ownership = ownership;
        _family = family;
    }

    public bool TryCapture() => _ownership.TryCapture(_family);

    public string Failure() => _ownership.Failure(_family);
}
