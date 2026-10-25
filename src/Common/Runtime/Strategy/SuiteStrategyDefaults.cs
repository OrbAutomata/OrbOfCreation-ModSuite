namespace OrbModding.Common.Runtime.Strategy;

/// <summary>
/// Well-known bulletins. These live outside <see cref="SuiteStrategy"/> because a published
/// service-cycle shape may not own non-constant static storage — an ambient static on a published
/// type is exactly the kind of side channel the structural validator exists to reject, and the rule
/// does not get an exception just because this particular static happens to be immutable.
/// </summary>
internal static class SuiteStrategyDefaults
{
    /// <summary>
    /// The generation-1 fallback and the policy restored when no goal is offered or the lifecycle
    /// changes. It carries no goal or stances, so consumers preserve their normal ordering and
    /// resolve <see cref="SuiteResourceStanceKind.Free"/> for every resource.
    /// </summary>
    internal static readonly SuiteStrategy Neutral = new();
}
