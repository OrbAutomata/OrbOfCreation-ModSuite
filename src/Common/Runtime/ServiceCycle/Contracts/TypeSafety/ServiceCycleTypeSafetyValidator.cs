using System;
using OrbModding.Common.Runtime.Configuration;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

internal enum ServiceCycleTypeRole
{
    Frame,
    Configuration,
    State,
    Action,
    Strategy,

    /// <summary>
    /// A published world snapshot. Structurally the same bargain as <see cref="Strategy"/> — one
    /// immutable value every service reads and none mutates — but named separately so a violation
    /// message says which publication is at fault.
    /// </summary>
    World,
}

internal static class ServiceCycleTypeSafetyValidator
{
    internal static void EnsureServiceTypes<TState, TAction>()
    {
        var violation = ClosedServiceTypes<TState, TAction>.Violation;
        if (violation.HasValue) throw new InvalidOperationException(violation.Value.Message);
    }

    internal static void EnsureWorldType<TWorld>()
    {
        var violation = ClosedWorldType<TWorld>.Violation;
        if (violation.HasValue) throw new InvalidOperationException(violation.Value.Message);
    }

    /// <summary>
    /// The suite's one configuration record, audited once instead of on every registration.
    /// </summary>
    /// <remarks>
    /// There is one, it is the same one every time, and re-proving it per service proves nothing a
    /// single audit does not.
    /// </remarks>
    internal static ServiceCycleTypeViolation? ValidateSuiteConfiguration() =>
        ServiceCycleTypeGraphWalker.Validate(
            typeof(SuiteRuntimeConfiguration),
            ServiceCycleTypeRole.Configuration,
            "configuration");

    /// <summary>
    /// The suite's one strategy bulletin, audited once on the same terms as the configuration.
    /// </summary>
    /// <remarks>
    /// There is one bulletin shape, hard-pinned by the publisher, so the audit belongs where the type
    /// is named rather than behind a type parameter that could only ever be closed one way.
    /// </remarks>
    internal static ServiceCycleTypeViolation? ValidateSuiteStrategy() =>
        ServiceCycleTypeGraphWalker.Validate(
            typeof(Strategy.SuiteStrategy),
            ServiceCycleTypeRole.Strategy,
            "strategy");

    /// <summary>
    /// The buffer the source capture fills, audited once instead of on every registration.
    /// </summary>
    /// <remarks>
    /// The same move the configuration made: there is one capture buffer in the suite, so proving its
    /// shape belongs where the type is named rather than wherever a service happens to register.
    /// </remarks>
    internal static ServiceCycleTypeViolation? ValidateCaptureBuffer() =>
        ServiceCycleTypeGraphWalker.Validate(
            typeof(World.GameWorldCycleFrame),
            ServiceCycleTypeRole.Frame,
            "frame");

    private static ServiceCycleTypeViolation? ValidateServiceTypes(
        Type state,
        Type action)
    {
        var violation = ServiceCycleTypeGraphWalker.Validate(state, ServiceCycleTypeRole.State, "state");
        if (violation.HasValue) return violation;
        return ServiceCycleTypeGraphWalker.Validate(action, ServiceCycleTypeRole.Action, "action");
    }

    private static class ClosedServiceTypes<TState, TAction>
    {
        internal static readonly ServiceCycleTypeViolation? Violation = ValidateServiceTypes(
            typeof(TState), typeof(TAction));
    }

    private static class ClosedWorldType<TWorld>
    {
        internal static readonly ServiceCycleTypeViolation? Violation = ServiceCycleTypeGraphWalker.Validate(
            typeof(TWorld), ServiceCycleTypeRole.World, "world");
    }
}
